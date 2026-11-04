using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Simple3D.Core;
using static Simple3D.Maui.Gles;
namespace Simple3D.Maui;

// All calls, including disposal, require the owning EGL context current on the calling thread.
internal sealed unsafe class GlesRenderer : IDisposable
{
    private uint _program, _screenProgram, _uniform, _vao, _fbo, _color, _mask, _depth;
    private int _width, _height;
    private readonly int _selectedLocation, _maxTextureSize, _uniformStride;
    private readonly string _name = "";
    private byte[] _uniformData = [];
    // One VAO per resident mesh: attribute layout is specified once at upload, not per draw.
    private readonly Dictionary<GpuGeometry, (uint Buffer, uint Vao)> _meshes = new();
    private readonly Dictionary<Texture2D, uint> _textures = new();
    private static readonly Texture2D White = new(1, 1, new[] { 0xFFFFFFFFu });
    internal string Name => _name;
    internal int GeometryUploads { get; private set; }
    internal int TextureUploads { get; private set; }
    internal GlesRenderer()
    {
        try
        {
            _program = Program(GpuShaders.GlslVertex, GpuShaders.GlslFragment);
            _screenProgram = Program(GpuShaders.GlslScreenVertex, GpuShaders.GlslScreenFragment);
            glGenVertexArrays(1, out _vao); glGenBuffers(1, out _uniform);
            glUniformBlockBinding(_program, glGetUniformBlockIndex(_program, "Params"), 0);
            glUseProgram(_program);
            glUniform1i(glGetUniformLocation(_program, "image"), 0);
            _selectedLocation = glGetUniformLocation(_program, "selected");
            glUseProgram(_screenProgram);
            glUniform1i(glGetUniformLocation(_screenProgram, "sceneColor"), 0);
            glUniform1i(glGetUniformLocation(_screenProgram, "sceneMask"), 1);
            _name = "OpenGL ES 3 / " + Marshal.PtrToStringAnsi(glGetString(GL_RENDERER));
            glGetIntegerv(GL_MAX_TEXTURE_SIZE, out _maxTextureSize);
            glGetIntegerv(GL_UNIFORM_BUFFER_OFFSET_ALIGNMENT, out var alignment);
            alignment = Math.Max(1, alignment);
            _uniformStride = (sizeof(GpuUniforms) + alignment - 1) / alignment * alignment;
            Check();
        }
        catch { Dispose(); throw; }
    }
    internal double Render(GpuFrame frame, int width, int height, int outputWidth = 0, int outputHeight = 0)
    {
        var start = Stopwatch.GetTimestamp();
        GeometryUploads = TextureUploads = 0;
        Resize(width, height);
        // Bound resident resources to this scene and cap upload size before allocating native storage.
        var usedMeshes = frame.Geometry.Items.Select(i => i.Geometry).ToHashSet();
        var usedTextures = frame.Geometry.Items.Select(i => i.Shape.Material.Texture ?? White).ToHashSet();
        long bytes = usedMeshes.Sum(m => (long)m.Vertices.Length * 4) + usedTextures.Sum(t => (long)t.Width * t.Height * 4);
        if (bytes > 256L * 1024 * 1024) throw new InvalidOperationException("GPU scene resources exceed 256 MiB.");
        PruneMeshes(usedMeshes); Prune(_textures, usedTextures);
        UploadUniforms(frame);
        glBindFramebuffer(GL_FRAMEBUFFER, _fbo); glViewport(0, 0, width, height);
        glDrawBuffers(2, [GL_COLOR_ATTACHMENT0, GL_COLOR_ATTACHMENT1]);
        var bg = GpuGeometry.Rgba(frame.Background);
        glClearBufferfv(GL_COLOR, 0, [bg.X, bg.Y, bg.Z, 1]); glClearBufferfv(GL_COLOR, 1, [0, 0, 0, 0]);
        glDepthMask(1); glClearBufferfv(GL_DEPTH, 0, [1]);
        glEnable(GL_DEPTH_TEST); glDepthFunc(GL_LESS); glDisable(GL_BLEND);
        glFrontFace(GL_CCW); glCullFace(GL_BACK);
        glUseProgram(_program);
        var items = frame.Geometry.Items;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            glBindVertexArray(Mesh(item.Geometry));
            glBindBufferRange(GL_UNIFORM_BUFFER, 0, _uniform, (nint)index * _uniformStride, sizeof(GpuUniforms));
            glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, Texture(item.Shape.Material.Texture ?? White));
            glUniform1f(_selectedLocation, ReferenceEquals(item.Shape, frame.Selected) ? 1 : 0);
            if (item.Shape.Material.DoubleSided) glDisable(GL_CULL_FACE); else glEnable(GL_CULL_FACE);
            glDrawArrays(GL_TRIANGLES, 0, item.Geometry.Vertices.Length / GpuGeometry.FloatsPerVertex);
        }
        glBindFramebuffer(GL_FRAMEBUFFER, 0); glViewport(0, 0, outputWidth > 0 ? outputWidth : width, outputHeight > 0 ? outputHeight : height); glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE);
        glUseProgram(_screenProgram); glBindVertexArray(_vao);
        glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, _color);
        glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, _mask);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        Check();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    internal uint[] ReadPixels()
    {
        var rgba = new byte[_width * _height * 4];
        fixed (byte* p = rgba) glReadPixels(0, 0, _width, _height, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Check();
        var result = new uint[_width * _height];
        for (var y = 0; y < _height; y++) for (var x = 0; x < _width; x++)
            { var i = ((_height - 1 - y) * _width + x) * 4; result[y * _width + x] = 0xFF000000u | ((uint)rgba[i] << 16) | ((uint)rgba[i + 1] << 8) | rgba[i + 2]; }
        return result;
    }
    // Every item's uniforms in one upload per frame, each at an aligned offset bound per draw.
    private void UploadUniforms(GpuFrame frame)
    {
        var items = frame.Geometry.Items;
        if (items.Count == 0) return;
        var size = items.Count * _uniformStride;
        if (_uniformData.Length < size) _uniformData = new byte[Math.Max(size, _uniformData.Length * 2)];
        fixed (byte* data = _uniformData)
        {
            for (var i = 0; i < items.Count; i++) *(GpuUniforms*)(data + i * _uniformStride) = GpuUniforms.Create(frame, items[i]);
            glBindBuffer(GL_UNIFORM_BUFFER, _uniform);
            glBufferData(GL_UNIFORM_BUFFER, size, data, GL_STREAM_DRAW);
        }
    }
    private uint Mesh(GpuGeometry geometry)
    {
        if (_meshes.TryGetValue(geometry, out var mesh)) return mesh.Vao;
        glGenVertexArrays(1, out var vao); glGenBuffers(1, out var buffer); _meshes.Add(geometry, (buffer, vao));
        glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, buffer);
        fixed (float* p = geometry.Vertices) glBufferData(GL_ARRAY_BUFFER, geometry.Vertices.Length * 4, p, GL_STATIC_DRAW);
        for (uint a = 0; a < 4; a++) { glEnableVertexAttribArray(a); glVertexAttribPointer(a, 4, GL_FLOAT, 0, GpuGeometry.FloatsPerVertex * sizeof(float), (nint)(a * 16)); }
        GeometryUploads++;
        return vao;
    }
    private uint Texture(Texture2D texture)
    {
        if (_textures.TryGetValue(texture, out var id)) return id;
        if (texture.Width > _maxTextureSize || texture.Height > _maxTextureSize)
            throw new InvalidOperationException($"Texture {texture.Width}x{texture.Height} exceeds this device's OpenGL ES limit of {_maxTextureSize}x{_maxTextureSize}.");
        glGenTextures(1, out id); _textures.Add(texture, id); glBindTexture(GL_TEXTURE_2D, id);
        Parameters(texture.Filter == TextureFilter.Nearest ? GL_NEAREST : GL_LINEAR, texture.AddressMode == TextureAddressMode.Repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE);
        var bytes = new byte[texture.Width * texture.Height * 4];
        for (var i = 0; i < texture.Pixels.Count; i++) { var c = texture.Pixels[i]; bytes[i * 4] = (byte)(c >> 16); bytes[i * 4 + 1] = (byte)(c >> 8); bytes[i * 4 + 2] = (byte)c; bytes[i * 4 + 3] = 255; }
        fixed (byte* p = bytes) glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, texture.Width, texture.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        TextureUploads++; return id;
    }
    private void Resize(int width, int height)
    {
        if (width == _width && height == _height) return;
        ReleaseTargets(); _width = width; _height = height;
        glGenFramebuffers(1, out _fbo); glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        _color = Target(GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, GL_COLOR_ATTACHMENT0);
        // RGBA8 avoids implementation-specific single-channel render target support.
        _mask = Target(GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, GL_COLOR_ATTACHMENT1);
        _depth = Target(GL_DEPTH_COMPONENT24, GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, GL_DEPTH_ATTACHMENT);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("GPU framebuffer is incomplete.");
    }
    private uint Target(int format, uint pixelFormat, uint type, uint attachment)
    {
        glGenTextures(1, out var id); glBindTexture(GL_TEXTURE_2D, id); Parameters(GL_NEAREST, GL_CLAMP_TO_EDGE);
        glTexImage2D(GL_TEXTURE_2D, 0, format, _width, _height, 0, pixelFormat, type, null);
        glFramebufferTexture2D(GL_FRAMEBUFFER, attachment, GL_TEXTURE_2D, id, 0); return id;
    }
    private static void Parameters(int filter, int wrap)
    { glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, wrap); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, wrap); }
    private static void Prune(Dictionary<Texture2D, uint> cache, HashSet<Texture2D> used)
    { foreach (var key in cache.Keys.Where(k => !used.Contains(k)).ToArray()) { var id = cache[key]; glDeleteTextures(1, ref id); cache.Remove(key); } }
    private void PruneMeshes(HashSet<GpuGeometry> used)
    {
        foreach (var key in _meshes.Keys.Where(k => !used.Contains(k)).ToArray())
        { var (buffer, vao) = _meshes[key]; glDeleteVertexArrays(1, ref vao); glDeleteBuffers(1, ref buffer); _meshes.Remove(key); }
    }
    private static uint Program(string vertex, string fragment)
    {
        var v = Shader(GL_VERTEX_SHADER, vertex); uint f = 0, p = 0;
        try
        {
            f = Shader(GL_FRAGMENT_SHADER, fragment); p = glCreateProgram(); glAttachShader(p, v); glAttachShader(p, f); glLinkProgram(p); glGetProgramiv(p, GL_LINK_STATUS, out var ok);
            if (ok == 0) { var log = new StringBuilder(8192); glGetProgramInfoLog(p, log.Capacity, out _, log); throw new InvalidOperationException(log.ToString()); }
            return p;
        }
        catch { if (p != 0) glDeleteProgram(p); throw; }
        finally { glDeleteShader(v); if (f != 0) glDeleteShader(f); }
    }
    private static uint Shader(uint type, string source)
    {
        var id = glCreateShader(type); glShaderSource(id, 1, [source], null); glCompileShader(id); glGetShaderiv(id, GL_COMPILE_STATUS, out var ok);
        if (ok != 0) return id;
        var log = new StringBuilder(8192); glGetShaderInfoLog(id, log.Capacity, out _, log); glDeleteShader(id); throw new InvalidOperationException(log.ToString());
    }
    private static void Check() { var error = glGetError(); if (error != 0) throw new InvalidOperationException($"OpenGL ES error 0x{error:X}."); }
    private void ReleaseTargets()
    { glDeleteTextures(1, ref _color); glDeleteTextures(1, ref _mask); glDeleteTextures(1, ref _depth); glDeleteFramebuffers(1, ref _fbo); _color = _mask = _depth = _fbo = 0; _width = _height = 0; }
    public void Dispose()
    { PruneMeshes([]); Prune(_textures, []); ReleaseTargets(); glDeleteBuffers(1, ref _uniform); glDeleteVertexArrays(1, ref _vao); glDeleteProgram(_program); glDeleteProgram(_screenProgram); _uniform = _vao = _program = _screenProgram = 0; }
}

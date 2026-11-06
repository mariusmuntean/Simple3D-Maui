// Android (and the desktop GLES tests, which link this file): never compiled into Apple builds.
#if ANDROID || SIMPLE3D_GLES_TESTS
using System.Runtime.InteropServices;
using System.Text;
using Simple3D.Core;
using static Simple3D.Maui.Gles;
namespace Simple3D.Maui;

// All calls, including disposal, require the owning EGL context current on the calling thread.
internal sealed unsafe class GlesRenderer : IDisposable
{
    private uint _program, _maskProgram, _skinnedProgram, _skinnedMaskProgram, _screenProgram, _uniform, _palette, _vao, _fbo, _color, _mask, _depth;
    private int _targetWidth, _targetHeight;
    private readonly int _maxTextureSize, _uniformStride, _uniformAlignment, _outlineLocation;
    private byte[] _uniformData = [], _paletteData = [];
    private int[] _paletteOffsets = [];
    // One VAO per resident mesh: attribute layout is specified once at upload, not per draw.
    private readonly GpuResources<(uint Buffer, uint Vao, uint Deltas), uint> _resources = new(
        mesh => { var (buffer, vao, deltas) = mesh; glDeleteTextures(1, ref deltas); glDeleteVertexArrays(1, ref vao); glDeleteBuffers(1, ref buffer); },
        texture => glDeleteTextures(1, ref texture));
    internal string Name { get; } = "";
    internal int GeometryUploads { get; private set; }
    internal int TextureUploads { get; private set; }
    internal int PaletteUploads { get; private set; }
    internal int PaletteUploadBytes { get; private set; }
    internal int DeltaUploads { get; private set; }
    internal long DeltaUploadBytes { get; private set; }
    internal int MorphUploads { get; private set; }
    internal int MorphUploadBytes { get; private set; }
    /// <summary>Always use the offscreen mask/outline composite. For validation that both paths match.</summary>
    internal bool ForceComposite { get; set; }
    /// <summary>Whether the last frame rendered straight to the window surface (no offscreen targets or fullscreen pass).</summary>
    internal bool LastFrameDirect { get; private set; }
    /// <summary>The window surface has a depth buffer (EGL_DEPTH_SIZE), which direct rendering needs.</summary>
    internal bool SupportsDirect { get; }
    internal GlesRenderer()
    {
        try
        {
            _program = Program(GlesShaders.GlslVertex, GlesShaders.GlslFragment);
            _maskProgram = Program(GlesShaders.GlslVertex, GlesShaders.WithDefine(GlesShaders.GlslFragment, GlesShaders.SelectionMaskDefine));
            var skinned = GlesShaders.WithDefine(GlesShaders.GlslVertex, GlesShaders.SkinnedDefine);
            _skinnedProgram = Program(skinned, GlesShaders.GlslFragment);
            _skinnedMaskProgram = Program(skinned, GlesShaders.WithDefine(GlesShaders.GlslFragment, GlesShaders.SelectionMaskDefine));
            _screenProgram = Program(GlesShaders.GlslScreenVertex, GlesShaders.GlslScreenFragment);
            glGenVertexArrays(1, out _vao); glGenBuffers(1, out _uniform); glGenBuffers(1, out _palette);
            foreach (var program in new[] { _program, _maskProgram, _skinnedProgram, _skinnedMaskProgram })
            {
                glUniformBlockBinding(program, glGetUniformBlockIndex(program, "Params"), 0);
                if (program == _skinnedProgram || program == _skinnedMaskProgram)
                    glUniformBlockBinding(program, glGetUniformBlockIndex(program, "Joints"), 1);
                glUseProgram(program);
                glUniform1i(glGetUniformLocation(program, "image"), 0);
                if (program == _skinnedProgram || program == _skinnedMaskProgram)
                    glUniform1i(glGetUniformLocation(program, "morphDeltas"), 2);
            }
            glUseProgram(_screenProgram);
            glUniform1i(glGetUniformLocation(_screenProgram, "sceneColor"), 0);
            glUniform1i(glGetUniformLocation(_screenProgram, "sceneMask"), 1);
            _outlineLocation = glGetUniformLocation(_screenProgram, "outlineRadius");
            Name = "OpenGL ES 3 / " + Marshal.PtrToStringAnsi(glGetString(GL_RENDERER));
            glGetIntegerv(GL_MAX_TEXTURE_SIZE, out _maxTextureSize);
            glGetIntegerv(GL_UNIFORM_BUFFER_OFFSET_ALIGNMENT, out var alignment);
            alignment = Math.Max(1, alignment);
            _uniformStride = (sizeof(GpuUniforms) + alignment - 1) / alignment * alignment;
            _uniformAlignment = alignment;
            // Queried with the window surface current and the default framebuffer bound.
            glGetIntegerv(GL_DEPTH_BITS, out var depthBits);
            SupportsDirect = depthBits >= 16;
            Check();
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Renders at width x height and presents to the window surface at outputWidth x outputHeight.</summary>
    internal void Render(GpuFrame frame, int width, int height, int outputWidth, int outputHeight)
    {
        GeometryUploads = TextureUploads = PaletteUploads = PaletteUploadBytes = 0;
        DeltaUploads = MorphUploads = MorphUploadBytes = 0; DeltaUploadBytes = 0;
        _resources.Prepare(frame);
        UploadUniforms(frame);
        UploadPalettes(frame);
        // Without a visible selection there is nothing to outline: draw once into the window surface
        // instead of storing color+mask offscreen and reading them back in a fullscreen pass.
        var direct = !ForceComposite && SupportsDirect && frame.CanRenderDirect(width, height, outputWidth, outputHeight);
        LastFrameDirect = direct;
        var bg = GpuGeometry.Rgba(frame.Background);
        var clearColor = stackalloc float[4] { bg.X, bg.Y, bg.Z, 1 };
        var clearDepth = 1f;
        if (direct)
        {
            glBindFramebuffer(GL_FRAMEBUFFER, 0); glViewport(0, 0, width, height);
            glClearBufferfv(GL_COLOR, 0, clearColor);
        }
        else
        {
            EnsureTargets(width, height);
            glBindFramebuffer(GL_FRAMEBUFFER, _fbo); glViewport(0, 0, width, height);
            var buffers = stackalloc uint[2] { GL_COLOR_ATTACHMENT0, GL_COLOR_ATTACHMENT1 };
            glDrawBuffers(2, buffers);
            var clearMask = stackalloc float[4];
            glClearBufferfv(GL_COLOR, 0, clearColor); glClearBufferfv(GL_COLOR, 1, clearMask);
        }
        glDepthMask(1); glClearBufferfv(GL_DEPTH, 0, &clearDepth);
        glEnable(GL_DEPTH_TEST); glDepthFunc(GL_LESS); glDisable(GL_BLEND);
        glFrontFace(GL_CCW); glCullFace(GL_BACK);
        glActiveTexture(GL_TEXTURE0);
        var items = frame.Geometry.Items;
        uint boundTexture = uint.MaxValue, boundDeltas = uint.MaxValue, boundProgram = 0;
        var cullState = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var program = item.Joints is null ? (direct ? _program : _maskProgram) : (direct ? _skinnedProgram : _skinnedMaskProgram);
            if (program != boundProgram) { glUseProgram(program); boundProgram = program; }
            var mesh = Mesh(item.Geometry);
            glBindVertexArray(mesh.Vao);
            if (mesh.Deltas != boundDeltas)
            {
                glActiveTexture(GL_TEXTURE2); glBindTexture(GL_TEXTURE_2D, mesh.Deltas); glActiveTexture(GL_TEXTURE0);
                boundDeltas = mesh.Deltas;
            }
            glBindBufferRange(GL_UNIFORM_BUFFER, 0, _uniform, (nint)index * _uniformStride, sizeof(GpuUniforms));
            if (item.Joints is not null)
                glBindBufferRange(GL_UNIFORM_BUFFER, 1, _palette, _paletteOffsets[index], GlesShaders.PaletteBytes);
            var texture = Texture(GpuResources.TextureOf(item));
            if (texture != boundTexture) { glBindTexture(GL_TEXTURE_2D, texture); boundTexture = texture; }
            var cull = item.Shape.Material.DoubleSided ? 0 : 1;
            if (cull != cullState) { if (cull == 0) glDisable(GL_CULL_FACE); else glEnable(GL_CULL_FACE); cullState = cull; }
            glDrawArrays(GL_TRIANGLES, 0, item.Geometry.VertexCount);
        }
        // Depth is never read back: let tile-based GPUs skip writing it to memory.
        var depthAttachment = direct ? (uint)GL_DEPTH : GL_DEPTH_ATTACHMENT;
        glInvalidateFramebuffer(GL_FRAMEBUFFER, 1, &depthAttachment);
        if (!direct)
        {
            glBindFramebuffer(GL_FRAMEBUFFER, 0);
            // The fullscreen pass overwrites every pixel, so the previous window contents need not be loaded.
            var window = stackalloc uint[2] { GL_COLOR, GL_DEPTH };
            glInvalidateFramebuffer(GL_FRAMEBUFFER, 2, window);
            glViewport(0, 0, outputWidth, outputHeight); glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE);
            glUseProgram(_screenProgram); glUniform1i(_outlineLocation, frame.OutlineRadius);
            glBindVertexArray(_vao);
            glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, _color);
            glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, _mask);
            glDrawArrays(GL_TRIANGLES, 0, 3);
            glActiveTexture(GL_TEXTURE0);
        }
        Check();
    }
    /// <summary>Reads the window surface (the presented output) as packed 0xAARRGGBB rows, top row first.</summary>
    internal uint[] ReadPixels(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        fixed (byte* p = rgba) glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Check();
        var result = new uint[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            { var i = ((height - 1 - y) * width + x) * 4; result[y * width + x] = 0xFF000000u | ((uint)rgba[i] << 16) | ((uint)rgba[i + 1] << 8) | rgba[i + 2]; }
        return result;
    }
    // Every item's uniforms in one upload per frame, each at an aligned offset bound per draw.
    private void UploadUniforms(GpuFrame frame)
    {
        var items = frame.Geometry.Items;
        if (items.Count == 0) return;
        var size = items.Count * _uniformStride;
        if (_uniformData.Length < size) _uniformData = new byte[Math.Max(size, _uniformData.Length * 2)];
        var viewProjection = frame.Geometry.ViewProjection;
        fixed (byte* data = _uniformData)
        {
            for (var i = 0; i < items.Count; i++)
            {
                *(GpuUniforms*)(data + i * _uniformStride) = GpuUniforms.Create(frame, items[i], viewProjection);
                if (items[i].Geometry.MorphVertexCount != 0) { MorphUploads++; MorphUploadBytes += 80; }
            }
            glBindBuffer(GL_UNIFORM_BUFFER, _uniform);
            glBufferData(GL_UNIFORM_BUFFER, size, data, GL_STREAM_DRAW);
        }
    }
    // Every skinned item's joint palette in one upload per frame. Slots are packed by their real size; each
    // bound range covers the whole declared block, so the buffer carries one block of tail padding.
    private void UploadPalettes(GpuFrame frame)
    {
        var items = frame.Geometry.Items;
        if (_paletteOffsets.Length < items.Count) _paletteOffsets = new int[Math.Max(items.Count, _paletteOffsets.Length * 2)];
        var used = 0;
        for (var i = 0; i < items.Count; i++)
            if (items[i].Joints is { } joints) { _paletteOffsets[i] = used; used = checked(used + Align(joints.Length * sizeof(System.Numerics.Matrix4x4))); }
        if (used == 0) return;
        var size = checked(used + GlesShaders.PaletteBytes);
        if (_paletteData.Length < size) _paletteData = new byte[Math.Max(size, _paletteData.Length * 2)];
        fixed (byte* data = _paletteData)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Joints is not { } joints) continue;
                var bytes = joints.Length * sizeof(System.Numerics.Matrix4x4);
                fixed (System.Numerics.Matrix4x4* source = joints) Buffer.MemoryCopy(source, data + _paletteOffsets[i], bytes, bytes);
                PaletteUploads++; PaletteUploadBytes += bytes;
            }
            glBindBuffer(GL_UNIFORM_BUFFER, _palette);
            glBufferData(GL_UNIFORM_BUFFER, size, data, GL_STREAM_DRAW);
        }
    }
    private int Align(int bytes) => (bytes + _uniformAlignment - 1) / _uniformAlignment * _uniformAlignment;
    private (uint Buffer, uint Vao, uint Deltas) Mesh(GpuGeometry geometry)
    {
        if (_resources.Meshes.TryGetValue(geometry, out var mesh)) return mesh;
        // Checked before allocating, so a rejected mesh leaves no half-built cache entry.
        if (geometry.MorphTextureWidth > _maxTextureSize || geometry.MorphTextureHeight > _maxTextureSize)
            throw new InvalidOperationException($"Morph deltas exceed this device's OpenGL ES texture limit of {_maxTextureSize}x{_maxTextureSize}.");
        glGenVertexArrays(1, out var vao); glGenBuffers(1, out var buffer); _resources.Meshes.Add(geometry, (buffer, vao, 0));
        glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, buffer);
        var vertices = geometry.Expand();
        fixed (float* p = vertices) glBufferData(GL_ARRAY_BUFFER, vertices.Length * sizeof(float), p, GL_STATIC_DRAW);
        // Skinning appends joint indices, weights and the unexpanded source vertex index.
        var attributes = geometry.IsSkinned ? 7u : 4u;
        for (uint a = 0; a < attributes; a++) { glEnableVertexAttribArray(a); glVertexAttribPointer(a, 4, GL_FLOAT, 0, geometry.VertexStrideFloats * sizeof(float), (nint)(a * 16)); }
        GeometryUploads++;
        mesh = (buffer, vao, MorphTexture(geometry));
        _resources.Meshes[geometry] = mesh;
        return mesh;
    }
    private uint MorphTexture(GpuGeometry geometry)
    {
        if (geometry.MorphVertexCount == 0) return 0;
        var width = geometry.MorphTextureWidth; var height = geometry.MorphTextureHeight;
        var deltas = geometry.ExpandMorphs();
        // Pad the final row so the native upload never reads beyond the source array.
        Array.Resize(ref deltas, checked(width * height * 4));
        glGenTextures(1, out var texture); glActiveTexture(GL_TEXTURE2);
        try
        {
            glBindTexture(GL_TEXTURE_2D, texture); Parameters(GL_NEAREST, GL_CLAMP_TO_EDGE);
            fixed (float* p = deltas) glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA32F, width, height, 0, GL_RGBA, GL_FLOAT, p);
            Check();
            DeltaUploads++; DeltaUploadBytes += (long)deltas.Length * sizeof(float);
            return texture;
        }
        catch { glDeleteTextures(1, ref texture); throw; }
        finally { glActiveTexture(GL_TEXTURE0); }
    }
    private uint Texture(Texture2D texture)
    {
        if (_resources.Textures.TryGetValue(texture, out var id)) return id;
        if (texture.Width > _maxTextureSize || texture.Height > _maxTextureSize)
            throw new InvalidOperationException($"Texture {texture.Width}x{texture.Height} exceeds this device's OpenGL ES limit of {_maxTextureSize}x{_maxTextureSize}.");
        glGenTextures(1, out id); _resources.Textures.Add(texture, id); glBindTexture(GL_TEXTURE_2D, id);
        Parameters(texture.Filter == TextureFilter.Nearest ? GL_NEAREST : GL_LINEAR, texture.AddressMode == TextureAddressMode.Repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE);
        fixed (byte* p = GpuResources.Rgba8(texture))
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, texture.Width, texture.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        TextureUploads++; return id;
    }
    // Offscreen targets exist only while a selection is outlined (or a capped render size is upscaled).
    private void EnsureTargets(int width, int height)
    {
        if (width == _targetWidth && height == _targetHeight && _fbo != 0) return;
        ReleaseTargets(); _targetWidth = width; _targetHeight = height;
        glGenFramebuffers(1, out _fbo); glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        _color = Target(GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, GL_COLOR_ATTACHMENT0, GL_LINEAR);
        // R8 is a required color-renderable format in OpenGL ES 3.0; the mask needs one channel.
        _mask = Target(GL_R8, GL_RED, GL_UNSIGNED_BYTE, GL_COLOR_ATTACHMENT1, GL_NEAREST);
        _depth = Target(GL_DEPTH_COMPONENT24, GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, GL_DEPTH_ATTACHMENT, GL_NEAREST);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) throw new InvalidOperationException("GPU framebuffer is incomplete.");
    }
    private uint Target(int format, uint pixelFormat, uint type, uint attachment, int filter)
    {
        glGenTextures(1, out var id); glBindTexture(GL_TEXTURE_2D, id); Parameters(filter, GL_CLAMP_TO_EDGE);
        glTexImage2D(GL_TEXTURE_2D, 0, format, _targetWidth, _targetHeight, 0, pixelFormat, type, null);
        glFramebufferTexture2D(GL_FRAMEBUFFER, attachment, GL_TEXTURE_2D, id, 0); return id;
    }
    private static void Parameters(int filter, int wrap)
    { glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, wrap); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, wrap); }
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
    { glDeleteTextures(1, ref _color); glDeleteTextures(1, ref _mask); glDeleteTextures(1, ref _depth); glDeleteFramebuffers(1, ref _fbo); _color = _mask = _depth = _fbo = 0; _targetWidth = _targetHeight = 0; }
    public void Dispose()
    {
        _resources.Clear(); ReleaseTargets();
        glDeleteBuffers(1, ref _uniform); glDeleteBuffers(1, ref _palette); glDeleteVertexArrays(1, ref _vao);
        glDeleteProgram(_program); glDeleteProgram(_maskProgram); glDeleteProgram(_skinnedProgram); glDeleteProgram(_skinnedMaskProgram); glDeleteProgram(_screenProgram);
        _uniform = _palette = _vao = _program = _maskProgram = _skinnedProgram = _skinnedMaskProgram = _screenProgram = 0;
    }
}
#endif

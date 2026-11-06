// Android (and the desktop GLES tests, which link this file): never compiled into Apple builds.
#if ANDROID || SIMPLE3D_GLES_TESTS
using System.Runtime.InteropServices;
namespace Simple3D.Maui;

internal static unsafe class Gles
{
    internal const int GL_RENDERER = 0x1F01;
    internal const int GL_FRAMEBUFFER = 0x8D40;
    internal const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    internal const int GL_COLOR_ATTACHMENT1 = 0x8CE1;
    internal const int GL_COLOR = 0x1800;
    internal const int GL_DEPTH = 0x1801;
    internal const int GL_DEPTH_TEST = 0x0B71;
    internal const int GL_LESS = 0x0201;
    internal const int GL_BLEND = 0x0BE2;
    internal const int GL_CCW = 0x0901;
    internal const int GL_BACK = 0x0405;
    internal const int GL_ARRAY_BUFFER = 0x8892;
    internal const int GL_STATIC_DRAW = 0x88E4;
    internal const int GL_FLOAT = 0x1406;
    internal const int GL_UNIFORM_BUFFER = 0x8A11;
    internal const int GL_STREAM_DRAW = 0x88E0;
    internal const int GL_TEXTURE0 = 0x84C0;
    internal const int GL_TEXTURE1 = 0x84C1;
    internal const int GL_TEXTURE2 = 0x84C2;
    internal const int GL_TEXTURE3 = 0x84C3;
    internal const int GL_TEXTURE_2D = 0x0DE1;
    internal const int GL_CULL_FACE = 0x0B44;
    internal const int GL_TRIANGLES = 0x0004;
    internal const int GL_RGBA = 0x1908;
    internal const int GL_UNSIGNED_BYTE = 0x1401;
    internal const int GL_NEAREST = 0x2600;
    internal const int GL_LINEAR = 0x2601;
    internal const int GL_REPEAT = 0x2901;
    internal const int GL_CLAMP_TO_EDGE = 0x812F;
    internal const int GL_RGBA8 = 0x8058;
    internal const int GL_RGBA32F = 0x8814;
    internal const int GL_DEPTH_COMPONENT24 = 0x81A6;
    internal const int GL_DEPTH_COMPONENT = 0x1902;
    internal const int GL_UNSIGNED_INT = 0x1405;
    internal const int GL_DEPTH_ATTACHMENT = 0x8D00;
    internal const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    internal const int GL_TEXTURE_MIN_FILTER = 0x2801;
    internal const int GL_TEXTURE_MAG_FILTER = 0x2800;
    internal const int GL_TEXTURE_WRAP_S = 0x2802;
    internal const int GL_TEXTURE_WRAP_T = 0x2803;
    internal const int GL_VERTEX_SHADER = 0x8B31;
    internal const int GL_FRAGMENT_SHADER = 0x8B30;
    internal const int GL_LINK_STATUS = 0x8B82;
    internal const int GL_COMPILE_STATUS = 0x8B81;
    internal const int GL_MAX_TEXTURE_SIZE = 0x0D33;
    internal const int GL_UNIFORM_BUFFER_OFFSET_ALIGNMENT = 0x8A34;
    internal const int GL_R8 = 0x8229;
    internal const int GL_RED = 0x1903;
    internal const int GL_DEPTH_BITS = 0x0D56;

    // Android's library; the desktop (Mesa) GLES tests load the versioned Linux name.
#if ANDROID
    private const string Library = "libGLESv2.so";
#else
    private const string Library = "libGLESv2.so.2";
#endif
    [DllImport(Library)] internal static extern uint glCreateShader(uint type);
    [DllImport(Library)] internal static extern void glShaderSource(uint shader, int count, string[] source, int[]? lengths);
    [DllImport(Library)] internal static extern void glCompileShader(uint shader);
    [DllImport(Library)] internal static extern void glGetShaderiv(uint shader, uint name, out int value);
    [DllImport(Library)] internal static extern void glGetShaderInfoLog(uint shader, int length, out int written, System.Text.StringBuilder log);
    [DllImport(Library)] internal static extern void glDeleteShader(uint shader);
    [DllImport(Library)] internal static extern uint glCreateProgram();
    [DllImport(Library)] internal static extern void glAttachShader(uint program, uint shader);
    [DllImport(Library)] internal static extern void glLinkProgram(uint program);
    [DllImport(Library)] internal static extern void glGetProgramiv(uint program, uint name, out int value);
    [DllImport(Library)] internal static extern void glGetProgramInfoLog(uint program, int length, out int written, System.Text.StringBuilder log);
    [DllImport(Library)] internal static extern void glDeleteProgram(uint program);
    [DllImport(Library)] internal static extern void glUseProgram(uint program);
    [DllImport(Library)] internal static extern int glGetUniformLocation(uint program, string name);
    [DllImport(Library)] internal static extern uint glGetUniformBlockIndex(uint program, string name);
    [DllImport(Library)] internal static extern void glUniformBlockBinding(uint program, uint index, uint binding);
    [DllImport(Library)] internal static extern void glUniform1i(int location, int value);
    [DllImport(Library)] internal static extern void glGenBuffers(int count, out uint buffer);
    [DllImport(Library)] internal static extern void glDeleteBuffers(int count, ref uint buffer);
    [DllImport(Library)] internal static extern void glBindBuffer(uint target, uint buffer);
    [DllImport(Library)] internal static extern void glBindBufferRange(uint target, uint index, uint buffer, nint offset, nint size);
    [DllImport(Library)] internal static extern void glGetIntegerv(uint name, out int value);
    [DllImport(Library)] internal static extern void glBufferData(uint target, nint size, void* data, uint usage);
    [DllImport(Library)] internal static extern void glGenVertexArrays(int count, out uint array);
    [DllImport(Library)] internal static extern void glDeleteVertexArrays(int count, ref uint array);
    [DllImport(Library)] internal static extern void glBindVertexArray(uint array);
    [DllImport(Library)] internal static extern void glEnableVertexAttribArray(uint index);
    [DllImport(Library)] internal static extern void glVertexAttribPointer(uint index, int count, uint type, byte normalized, int stride, nint offset);
    [DllImport(Library)] internal static extern void glGenTextures(int count, out uint texture);
    [DllImport(Library)] internal static extern void glDeleteTextures(int count, ref uint texture);
    [DllImport(Library)] internal static extern void glBindTexture(uint target, uint texture);
    [DllImport(Library)] internal static extern void glActiveTexture(uint texture);
    [DllImport(Library)] internal static extern void glTexParameteri(uint target, uint name, int value);
    [DllImport(Library)] internal static extern void glTexImage2D(uint target, int level, int format, int width, int height, int border, uint pixelFormat, uint type, void* data);
    [DllImport(Library)] internal static extern void glTexSubImage2D(uint target, int level, int x, int y, int width, int height, uint pixelFormat, uint type, void* data);
    [DllImport(Library)] internal static extern void glGenFramebuffers(int count, out uint framebuffer);
    [DllImport(Library)] internal static extern void glDeleteFramebuffers(int count, ref uint framebuffer);
    [DllImport(Library)] internal static extern void glBindFramebuffer(uint target, uint framebuffer);
    [DllImport(Library)] internal static extern void glFramebufferTexture2D(uint target, uint attachment, uint textureTarget, uint texture, int level);
    [DllImport(Library)] internal static extern uint glCheckFramebufferStatus(uint target);
    [DllImport(Library)] internal static extern void glDrawBuffers(int count, uint* buffers);
    [DllImport(Library)] internal static extern void glViewport(int x, int y, int width, int height);
    [DllImport(Library)] internal static extern void glClearBufferfv(uint buffer, int drawBuffer, float* values);
    [DllImport(Library)] internal static extern void glInvalidateFramebuffer(uint target, int count, uint* attachments);
    [DllImport(Library)] internal static extern void glEnable(uint cap);
    [DllImport(Library)] internal static extern void glDisable(uint cap);
    [DllImport(Library)] internal static extern void glDepthFunc(uint function);
    [DllImport(Library)] internal static extern void glDepthMask(byte enabled);
    [DllImport(Library)] internal static extern void glCullFace(uint mode);
    [DllImport(Library)] internal static extern void glFrontFace(uint mode);
    [DllImport(Library)] internal static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport(Library)] internal static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, void* data);
    [DllImport(Library)] internal static extern void glFinish();
    [DllImport(Library)] internal static extern uint glGetError();
    [DllImport(Library)] internal static extern nint glGetString(uint name);
}
#endif

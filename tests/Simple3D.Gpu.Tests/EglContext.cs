using System.Runtime.InteropServices;
internal sealed class EglContext : IDisposable
{
    private nint _display, _surface, _context;
    internal EglContext(int width, int height)
    {
        NativeLibrary.SetDllImportResolver(typeof(EglContext).Assembly, (name, _, _) =>
        {
            if (name != "libGLESv2.so.2") return 0;
            if (NativeLibrary.TryLoad(name, out var handle)) return handle;
            // GLVND's libGL exports the same dispatch entry points for a current GLES context.
            return NativeLibrary.Load("libGL.so.1");
        });
        try
        {
            _display = eglGetPlatformDisplay(0x31DD, 0, null);
            if (_display == 0 || eglInitialize(_display, out _, out _) == 0) throw new Exception("EGL surfaceless initialization failed.");
            var configs = new nint[1];
            if (eglChooseConfig(_display, [0x3040, 0x0040, 0x3033, 1, 0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3038], configs, 1, out var count) == 0 || count == 0)
                throw new Exception("EGL ES3 pbuffer configuration unavailable.");
            _surface = eglCreatePbufferSurface(_display, configs[0], [0x3057, width, 0x3056, height, 0x3038]);
            _context = eglCreateContext(_display, configs[0], 0, [0x3098, 3, 0x3038]);
            if (_surface == 0 || _context == 0 || eglMakeCurrent(_display, _surface, _surface, _context) == 0) throw new Exception("EGL ES3 context failed.");
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    { if (_display == 0) return; eglMakeCurrent(_display, 0, 0, 0); if (_surface != 0) eglDestroySurface(_display, _surface); if (_context != 0) eglDestroyContext(_display, _context); eglTerminate(_display); _display = _surface = _context = 0; }
    [DllImport("libEGL.so.1")] private static extern nint eglGetPlatformDisplay(uint platform, nint display, nint[]? attributes);
    [DllImport("libEGL.so.1")] private static extern int eglInitialize(nint display, out int major, out int minor);
    [DllImport("libEGL.so.1")] private static extern int eglChooseConfig(nint display, int[] attributes, nint[] configs, int size, out int count);
    [DllImport("libEGL.so.1")] private static extern nint eglCreatePbufferSurface(nint display, nint config, int[] attributes);
    [DllImport("libEGL.so.1")] private static extern nint eglCreateContext(nint display, nint config, nint shared, int[] attributes);
    [DllImport("libEGL.so.1")] private static extern int eglMakeCurrent(nint display, nint draw, nint read, nint context);
    [DllImport("libEGL.so.1")] private static extern int eglDestroySurface(nint display, nint surface);
    [DllImport("libEGL.so.1")] private static extern int eglDestroyContext(nint display, nint context);
    [DllImport("libEGL.so.1")] private static extern int eglTerminate(nint display);
}

#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.Views;
namespace Simple3D.Maui;

/// <summary>An EGL call failed; <see cref="Recoverable"/> errors are handled by recreating the context.</summary>
internal sealed class EglException(string operation, int error) : InvalidOperationException($"{operation}: 0x{error:X}.")
{
    internal int Error { get; } = error;
    // Context loss (GPU reset, driver restart) invalidates every GL object but not the window: rebuild.
    internal bool Recoverable => Error is 0x300E /* EGL_CONTEXT_LOST */ or 0x3006 /* EGL_BAD_CONTEXT */;
}

// TextureView composes with MAUI overlays (unlike a separately composited SurfaceView).
// EGL is confined to the UI thread, including resource destruction and resize.
internal sealed class AndroidGpuView : TextureView, TextureView.ISurfaceTextureListener
{
    private const int MaximumRecoveries = 3, RecoveryResetFrames = 120;
    private readonly SceneView _owner;
    private EGLDisplay? _display;
    private EGLContext? _context;
    private EGLSurface? _surface;
    private Surface? _window;
    private SurfaceTexture? _surfaceTexture;
    private bool _queued, _released;
    private int _recoveries, _framesSinceRecovery;
    // Cached: reading Resources.DisplayMetrics creates Java peers, too costly per frame.
    private float _density;
    internal AndroidGpuView(Context context, SceneView owner) : base(context)
    {
        _owner = owner; SurfaceTextureListener = this; SetOpaque(true); _density = Density();
        _drawQueued = new Java.Lang.Runnable(() => { _queued = false; DrawFrame(); });
    }
    private float Density() => Resources?.DisplayMetrics?.Density is > 0 and var density ? density : 1;
    internal GlesRenderer? Renderer { get; private set; }
#if DEBUG || NATIVE_RENDER_PROBE
    internal int Recoveries { get; private set; }
#endif
    // Posted for every frame; one instance avoids a new Java Runnable peer per frame.
    private readonly Java.Lang.Runnable _drawQueued;
    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
    {
        _surfaceTexture = surface;
        try { CreateContext(surface); RequestRender(); }
        catch (Exception ex) { _owner.ReportHardwareFailure(ex); ReleaseContext(); }
    }
    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) { _density = Density(); RequestRender(); }
    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) { _surfaceTexture = null; ReleaseContext(); return true; }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
    private void CreateContext(SurfaceTexture surface)
    {
        _display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        if (_display is null || _display.Equals(EGL14.EglNoDisplay) || !EGL14.EglInitialize(_display, new int[2], 0, new int[2], 0))
            throw new InvalidOperationException("EGL display initialization failed.");
        // A window depth buffer lets frames without a visible selection render in one pass; without
        // one the renderer composites from offscreen targets.
        var config = ChooseConfig(_display, EGL14.EglWindowBit, depth: true) ?? ChooseConfig(_display, EGL14.EglWindowBit, depth: false)
            ?? throw new InvalidOperationException("No OpenGL ES 3 EGL configuration.");
        // Failed EGL calls return EGL_NO_* sentinels rather than null; report the real error.
        _context = EGL14.EglCreateContext(_display, config, EGL14.EglNoContext, new[] { EGL14.EglContextClientVersion, 3, EGL14.EglNone }, 0);
        if (_context is null || _context.Equals(EGL14.EglNoContext))
        { _context = null; throw new InvalidOperationException($"OpenGL ES 3 context creation failed: 0x{EGL14.EglGetError():X}."); }
        _window = new Surface(surface);
        _surface = EGL14.EglCreateWindowSurface(_display, config, _window, new[] { EGL14.EglNone }, 0);
        if (_surface is null || _surface.Equals(EGL14.EglNoSurface))
        { _surface = null; throw new InvalidOperationException($"EGL window surface creation failed: 0x{EGL14.EglGetError():X}."); }
        MakeCurrent(); Renderer = new GlesRenderer();
    }
    /// <summary>An RGBA8 OpenGL ES 3 config for the given EGL surface type, optionally with a 24-bit depth buffer.</summary>
    internal static EGLConfig? ChooseConfig(EGLDisplay display, int surfaceType, bool depth)
    {
        var configs = new EGLConfig[1]; var count = new int[1];
        var attributes = new[] { EGL14.EglRenderableType, 0x0040, EGL14.EglSurfaceType, surfaceType,
            EGL14.EglRedSize, 8, EGL14.EglGreenSize, 8, EGL14.EglBlueSize, 8, EGL14.EglAlphaSize, 8,
            EGL14.EglDepthSize, depth ? 24 : 0, EGL14.EglNone };
        return EGL14.EglChooseConfig(display, attributes, 0, configs, 0, 1, count, 0) && count[0] > 0 ? configs[0] : null;
    }
    internal void RequestRender()
    {
        if (_queued || _released || !_owner.CanRenderHardware) return;
        _queued = true;
        Post(_drawQueued);
    }
    private void DrawFrame()
    {
        if (_released || Renderer is null || !_owner.CanRenderHardware || Width < 1 || Height < 1) return;
        var presented = false;
        try
        {
            MakeCurrent();
            var size = SceneView.RenderSize(Width, Height, _owner.MaximumRenderDimension);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var frame = _owner.PrepareHardwareFrame(size.Width, size.Height, size.Width * _density / Width);
            if (frame is null) return; // Scene content skipped; the owner reported why.
            Renderer.Render(frame, size.Width, size.Height, Width, Height);
            if (!EGL14.EglSwapBuffers(_display, _surface)) throw new EglException("EGL presentation failed", EGL14.EglGetError());
            if (++_framesSinceRecovery >= RecoveryResetFrames) _recoveries = 0;
            _owner.HardwareFrameSubmitted(Renderer.Name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, size.Width, size.Height);
            presented = true;
        }
        catch (EglException lost) when (lost.Recoverable && _recoveries < MaximumRecoveries && _surfaceTexture is { } surface)
        {
            _recoveries++; _framesSinceRecovery = 0;
#if DEBUG || NATIVE_RENDER_PROBE
            Recoveries++;
#endif
            ReleaseContext();
            try { CreateContext(surface); RequestRender(); }
            catch (Exception ex) { _owner.ReportHardwareFailure(ex); ReleaseContext(); }
        }
        catch (Exception ex) { _owner.ReportHardwareFailure(ex); ReleaseContext(); }
        if (presented) _owner.RaiseFrameSubmitted();
    }
    private void MakeCurrent()
    {
        if (!EGL14.EglMakeCurrent(_display, _surface, _surface, _context)) throw new EglException("EGL context unavailable", EGL14.EglGetError());
    }
#if DEBUG || NATIVE_RENDER_PROBE
    /// <summary>Destroys the EGL context behind the renderer's back, as a driver reset would, then requests a frame.</summary>
    internal void DebugLoseContext()
    {
        if (_display is null || _context is null) return;
        EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
        EGL14.EglDestroyContext(_display, _context);
        RequestRender();
    }
#endif
    internal void Release() { _released = true; SurfaceTextureListener = null; _surfaceTexture = null; ReleaseContext(); }
    private void ReleaseContext()
    {
        if (_display is not null && !_display.Equals(EGL14.EglNoDisplay))
        {
            if (_context is not null && EGL14.EglMakeCurrent(_display, _surface, _surface, _context)) Renderer?.Dispose();
            Renderer = null;
            EGL14.EglMakeCurrent(_display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
            if (_surface is not null) EGL14.EglDestroySurface(_display, _surface);
            if (_context is not null) EGL14.EglDestroyContext(_display, _context);
            // EGLDisplay is shared with other views. Do not eglTerminate it here.
        }
        // Dispose only drops the managed peer; Release frees the native window now.
        _window?.Release(); _window?.Dispose(); _window = null; _surface = null; _context = null; _display = null;
    }
}
#endif

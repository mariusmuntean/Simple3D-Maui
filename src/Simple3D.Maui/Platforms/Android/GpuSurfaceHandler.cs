#if ANDROID
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.Views;
using Microsoft.Maui.Handlers;
namespace Simple3D.Maui;

internal sealed class GpuSurfaceHandler : ViewHandler<GpuSurface, AndroidGpuView>
{
    private static readonly CommandMapper<GpuSurface, GpuSurfaceHandler> Commands = new(ViewCommandMapper)
    { ["Render"] = (handler, _, _) => handler.PlatformView?.RequestRender() };
    internal GpuSurfaceHandler() : base(ViewMapper, Commands) { }
    protected override AndroidGpuView CreatePlatformView() => new(Context, VirtualView.Owner);
    protected override void DisconnectHandler(AndroidGpuView platformView)
    { platformView.Release(); base.DisconnectHandler(platformView); }
}

// TextureView composes with MAUI overlays (unlike a separately composited SurfaceView).
// EGL is confined to the UI thread, including resource destruction and resize.
internal sealed class AndroidGpuView : TextureView, TextureView.ISurfaceTextureListener
{
    private readonly SceneView _owner;
    private EGLDisplay? _display;
    private EGLContext? _context;
    private EGLSurface? _surface;
    private Surface? _window;
    private GlesRenderer? _renderer;
    private bool _queued, _released;
    internal AndroidGpuView(Context context, SceneView owner) : base(context)
    { _owner=owner; SurfaceTextureListener=this; SetOpaque(true); }
    public void OnSurfaceTextureAvailable(SurfaceTexture surface,int width,int height)
    {
        try
        {
            _display=EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
            if(_display is null || _display.Equals(EGL14.EglNoDisplay) || !EGL14.EglInitialize(_display,new int[2],0,new int[2],0))
                throw new InvalidOperationException("EGL display initialization failed.");
            var configs=new EGLConfig[1];var count=new int[1];
            if(!EGL14.EglChooseConfig(_display,new[]{ EGL14.EglRenderableType,0x0040,EGL14.EglSurfaceType,EGL14.EglWindowBit,
                EGL14.EglRedSize,8,EGL14.EglGreenSize,8,EGL14.EglBlueSize,8,EGL14.EglAlphaSize,8,EGL14.EglNone },0,configs,0,1,count,0)||count[0]==0)
                throw new InvalidOperationException("No OpenGL ES 3 EGL configuration.");
            _context=EGL14.EglCreateContext(_display,configs[0],EGL14.EglNoContext,new[]{EGL14.EglContextClientVersion,3,EGL14.EglNone},0);
            _window=new Surface(surface);
            _surface=EGL14.EglCreateWindowSurface(_display,configs[0],_window,new[]{EGL14.EglNone},0);
            MakeCurrent(); _renderer=new GlesRenderer(); RequestRender();
        }
        catch(Exception ex) { _owner.ReportHardwareFailure(ex); ReleaseContext(); }
    }
    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface,int width,int height)=>RequestRender();
    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) { ReleaseContext();return true; }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
    internal void RequestRender()
    {
        if(_queued||_released||!_owner.CanRenderHardware)return;
        _queued=true;
        Post(()=> { _queued=false;DrawFrame(); });
    }
    private void DrawFrame()
    {
        if(_released||_renderer is null||!_owner.CanRenderHardware||Width<1||Height<1)return;
        try
        {
            MakeCurrent();
            var size=SceneView.RenderSize(Width,Height,_owner.MaximumRenderDimension);
            var started=System.Diagnostics.Stopwatch.GetTimestamp();
            var frame=_owner.PrepareHardwareFrame(size.Width,size.Height);
            _renderer.Render(frame,size.Width,size.Height,Width,Height);
            if(!EGL14.EglSwapBuffers(_display,_surface))throw new InvalidOperationException($"EGL presentation failed: 0x{EGL14.EglGetError():X}.");
            _owner.HardwareFramePresented(_renderer.Name,System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch(Exception ex) { _owner.ReportHardwareFailure(ex);ReleaseContext(); }
    }
    private void MakeCurrent()
    {
        if(!EGL14.EglMakeCurrent(_display,_surface,_surface,_context))throw new InvalidOperationException($"EGL context unavailable: 0x{EGL14.EglGetError():X}.");
    }
    internal void Release() { _released=true;SurfaceTextureListener=null;ReleaseContext(); }
    private void ReleaseContext()
    {
        if(_display is not null && !_display.Equals(EGL14.EglNoDisplay))
        {
            if(_context is not null && EGL14.EglMakeCurrent(_display,_surface,_surface,_context))_renderer?.Dispose();
            _renderer=null;
            EGL14.EglMakeCurrent(_display,EGL14.EglNoSurface,EGL14.EglNoSurface,EGL14.EglNoContext);
            if(_surface is not null)EGL14.EglDestroySurface(_display,_surface);
            if(_context is not null)EGL14.EglDestroyContext(_display,_context);
            // EGLDisplay is shared with other views. Do not eglTerminate it here.
        }
        _window?.Dispose();_window=null;_surface=null;_context=null;_display=null;
    }
}
#endif

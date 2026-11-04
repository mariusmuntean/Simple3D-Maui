#if IOS || MACCATALYST
using CoreGraphics;
using Metal;
using MetalKit;
using Microsoft.Maui.Handlers;
namespace Simple3D.Maui;

internal sealed class GpuSurfaceHandler : ViewHandler<GpuSurface, AppleGpuView>
{
    private static readonly CommandMapper<GpuSurface, GpuSurfaceHandler> Commands = new(ViewCommandMapper)
    { ["Render"] = (handler, _, _) => handler.PlatformView?.RequestRender() };
    internal GpuSurfaceHandler() : base(ViewMapper, Commands) { }
    protected override AppleGpuView CreatePlatformView() => new(VirtualView.Owner);
    protected override void DisconnectHandler(AppleGpuView platformView)
    { platformView.Release();base.DisconnectHandler(platformView); }
}

internal sealed class AppleGpuView : MTKView
{
    private readonly SceneView _owner;
    private readonly DrawDelegate _drawDelegate;
    private MetalRenderer? _renderer;
    private bool _released;
    internal AppleGpuView(SceneView owner) : base(CGRect.Empty, MTLDevice.SystemDefault)
    {
        _owner=owner;
        Paused=true;EnableSetNeedsDisplay=true;ColorPixelFormat=MTLPixelFormat.BGRA8Unorm;
        FramebufferOnly=true;UserInteractionEnabled=false;Opaque=true;
        _drawDelegate=new DrawDelegate(this);Delegate=_drawDelegate;
        try { _renderer=new MetalRenderer(Device??throw new PlatformNotSupportedException("Metal is unavailable.")); }
        catch(Exception ex) { owner.ReportHardwareFailure(ex); }
    }
    internal void RequestRender() { if(!_released&&_owner.CanRenderHardware)SetNeedsDisplay(); }
    private void Render()
    {
        if(_released||_renderer is null||!_owner.CanRenderHardware||DrawableSize.Width<1||DrawableSize.Height<1)return;
        try
        {
            using var drawable=CurrentDrawable;
            if(drawable is null)return; // Window may be hidden/backgrounded. Retry on next invalidation.
            var size=SceneView.RenderSize((int)DrawableSize.Width,(int)DrawableSize.Height,_owner.MaximumRenderDimension);
            var started=System.Diagnostics.Stopwatch.GetTimestamp();
            var frame=_owner.PrepareHardwareFrame(size.Width,size.Height);
            _renderer.Render(frame,size.Width,size.Height,drawable.Texture,drawable,onError: error =>
                _owner.Dispatcher.Dispatch(() => { if(!_released)_owner.ReportHardwareFailure(error); }));
            _owner.HardwareFramePresented("Metal / "+Device!.Name,System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch(Exception ex) { _owner.ReportHardwareFailure(ex); }
    }
    internal void Release()
    { if(_released)return;_released=true;Delegate=null;_drawDelegate.Dispose();_renderer?.Dispose();_renderer=null;ReleaseDrawables(); }
    private sealed class DrawDelegate(AppleGpuView owner) : MTKViewDelegate
    {
        public override void Draw(MTKView view)=>owner.Render();
        public override void DrawableSizeWillChange(MTKView view,CGSize size)=>owner.RequestRender();
    }
}
#endif

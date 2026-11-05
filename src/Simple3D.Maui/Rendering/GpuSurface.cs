using Microsoft.Maui.Controls;
#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Handlers;
#if ANDROID
using PlatformGpuView = Simple3D.Maui.AndroidGpuView;
#else
using PlatformGpuView = Simple3D.Maui.AppleGpuView;
#endif
#endif
namespace Simple3D.Maui;

internal sealed class GpuSurface(SceneView owner) : View
{
    internal SceneView Owner { get; } = owner;
    internal void RequestRender() => Handler?.Invoke("Render", null);
    internal void Release()
    {
        // Leave the layout first: its handler finds this view's native view through this handler
        // (and throws once it is gone).
        (Parent as Layout)?.Children.Remove(this);
        Handler?.DisconnectHandler();
        Handler = null;
    }
}

#if ANDROID || IOS || MACCATALYST
internal sealed class GpuSurfaceHandler : ViewHandler<GpuSurface, PlatformGpuView>
{
    private static readonly CommandMapper<GpuSurface, GpuSurfaceHandler> Commands = new(ViewCommandMapper)
    { ["Render"] = (handler, _, _) => handler.PlatformView?.RequestRender() };
    internal GpuSurfaceHandler() : base(ViewMapper, Commands) { }
#if ANDROID
    protected override PlatformGpuView CreatePlatformView() => new(Context, VirtualView.Owner);
#else
    protected override PlatformGpuView CreatePlatformView() => new(VirtualView.Owner);
#endif
    protected override void DisconnectHandler(PlatformGpuView platformView)
    { platformView.Release(); base.DisconnectHandler(platformView); }
}
#endif

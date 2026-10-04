namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if DEBUG || NATIVE_RENDER_PROBE
        NativeRenderProbe.RunIfRequested();
#endif
        var page = new GalleryPage();
        var window = new Window(page) { Title = "Simple3D Gallery" };
        window.Stopped += (_, _) => page.StopAnimation();
        return window;
    }
}

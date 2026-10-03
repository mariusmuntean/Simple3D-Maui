namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if DEBUG || NATIVE_RENDER_PROBE
        NativeRenderProbe.RunIfRequested();
#endif
        return new(new GalleryPage()) { Title = "Simple3D Gallery" };
    }
}

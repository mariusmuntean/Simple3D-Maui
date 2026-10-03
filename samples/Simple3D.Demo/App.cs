namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if DEBUG
        NativeRenderProbe.RunIfRequested();
#endif
        return new(new GalleryPage()) { Title = "Simple3D Gallery" };
    }
}

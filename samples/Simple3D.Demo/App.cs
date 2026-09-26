namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new GalleryPage()) { Title = "Simple3D Gallery" };
}

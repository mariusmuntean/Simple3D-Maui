namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if DEBUG || NATIVE_RENDER_PROBE
        NativeRenderProbe.RunIfRequested();
#if IOS || MACCATALYST || ANDROID
        NativeGpuProbe.RunIfRequested();
        if (NativeGpuViewProbePage.IsRequested) return new Window(new NativeGpuViewProbePage()) { Title = "Simple3D GPU view probe" };
        if (LabelSyncProbePage.Mode is not null) return new Window(new LabelSyncProbePage()) { Title = "Simple3D label sync probe" };
#endif
#endif
        var page = new GalleryPage();
        var window = new Window(page) { Title = "Simple3D Gallery" };
        window.Stopped += (_, _) => page.StopAnimation();
        return window;
    }
}

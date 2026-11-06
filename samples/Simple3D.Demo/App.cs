namespace Simple3D.Demo;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
#if DEBUG || NATIVE_RENDER_PROBE
        NativeRenderProbe.RunIfRequested();
#if IOS || MACCATALYST || ANDROID
        if (Mirror.PoseProbePage.IsRequested) return new Window(new Mirror.PoseProbePage()) { Title = "Simple3D pose probe" };
        NativeGpuProbe.RunIfRequested();
        if (NativeGpuViewProbePage.IsRequested) return new Window(new NativeGpuViewProbePage()) { Title = "Simple3D GPU view probe" };
        if (LabelSyncProbePage.Mode is not null) return new Window(new LabelSyncProbePage()) { Title = "Simple3D label sync probe" };
#endif
#endif
#if DEBUG && (IOS || MACCATALYST || ANDROID)
        if (Mirror.MirrorPage.IsRequested) return new Window(new Mirror.MirrorPage()) { Title = "Simple3D Mirror" };
#endif
        var page = new GalleryPage();
        var navigation = new NavigationPage(page) { BackgroundColor = Theme.Canvas, BarBackgroundColor = Theme.Canvas, BarTextColor = Theme.Text };
        var window = new Window(navigation) { Title = "Simple3D Gallery" };
        window.Stopped += (_, _) => page.StopAnimation();
        return window;
    }
}

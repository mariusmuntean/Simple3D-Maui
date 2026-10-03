using Simple3D.Core;
using Simple3D.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Simple3D.Demo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().UseSkiaSharp().Build();
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var box = Shape.Box(0xFF8DA9FF).Named("Packaged box");
        var scene = new Scene().Add(box).Add(Shape.Sphere(0xFFFFB775).At(1.5f, 0, 0));
        var view = new SceneView { Scene = scene, HeightRequest = 400 };
        view.Camera.FitToScene(scene, 1.5f);
        view.Loaded += (_, _) =>
        {
            var frame = view.CaptureFrame(192, 128);
            if (!frame.Pixels.Span.ContainsAnyExcept(0xFFF4F6FAu))
                throw new InvalidOperationException("Packaged control rendered only background");
            if (!Enumerable.Range(0, 128).Any(y => Enumerable.Range(0, 192).Any(x => frame.Pick(x, y) != null)))
                throw new InvalidOperationException("Packaged control cannot pick geometry");
            var point = Enumerable.Range(0, 192 * 128).First(index => view.PickAt(index % 192, index / 192, 192, 128) is not null);
            view.SelectAt(point % 192, point / 192, 192, 128);
            view.IsInteractive = false;
            view.ClearSelection();
            if (view.SelectedShape is not null)
                throw new InvalidOperationException("Packaged control retained selection after clearing");
            Console.WriteLine("PACKAGE_MAUI_PASS: native control, pixels, picking and selection clearing");
        };
        return new Window(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children = { new Label { Text = "NuGet package consumer", FontSize = 24 }, view }
            }
        }) { Title = "Simple3D Package Check" };
    }
}

using Simple3D.Core;
using Simple3D.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Simple3D.PackageTests;

// Compile a host against packages: Core and SkiaSharp must resolve transitively.
public static class Host
{
    public static MauiApp Create() =>
        MauiApp.CreateBuilder().UseMauiApp<GalleryApp>().UseSkiaSharp().Build();
}

public sealed class GalleryApp : Application
{
    protected override Window CreateWindow(IActivationState? state)
    {
        var scene = new Scene().Add(Shape.Box().Named("box"));
        var details = new Label();
        var view = new SceneView { Scene = scene, IsInteractive = false, MaximumRenderDimension = 768 };
        view.Camera.FitToScene(scene);
        view.SelectionChanged += (_, shape) => details.Text = shape?.Name ?? "Background";
        return new Window(new ContentPage
        {
            Content = new VerticalStackLayout { Children = { view, details } }
        });
    }
}

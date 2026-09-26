using Simple3D.Core;
using Simple3D.Maui;

namespace Simple3D.Demo;

public sealed class GalleryPage : ContentPage
{
    private readonly SceneView _view = new() { HeightRequest = 440 };
    private readonly Label _caption = new() { FontSize = 15, TextColor = Color.FromArgb("#A9B8D4") };

    public GalleryPage()
    {
        BackgroundColor = Color.FromArgb("#0D1322");
        var title = new Label { Text = "Simple3D", FontSize = 34, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White };
        var subtitle = new Label { Text = "Little worlds, a few lines of C#.", FontSize = 16,
            TextColor = Color.FromArgb("#B7C6E2") };
        var scenes = new HorizontalStackLayout { Spacing = 8 };
        scenes.Add(SceneButton("Shapes", ShowShapes));
        scenes.Add(SceneButton("Stack", ShowStack));
        scenes.Add(SceneButton("Orbit", ShowOrbit));
        var tools = new HorizontalStackLayout { Spacing = 8 };
        tools.Add(SceneButton("−", () => _view.Zoom(.8f)));
        tools.Add(SceneButton("Reset", _view.ResetCamera));
        tools.Add(SceneButton("+", () => _view.Zoom(1.25f)));
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(22, 35), Spacing = 18,
            Children = { title, subtitle, scenes, new Border
            {
                BackgroundColor = Color.FromArgb("#18243B"),
                Stroke = Color.FromArgb("#314361"), StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 },
                Content = _view
            }, tools, _caption, new Label
            {
                Text = "Drag to orbit · Pinch to zoom", TextColor = Color.FromArgb("#8BA1C1"), FontSize = 13
            } }
        }};
        ShowShapes();
    }

    private static Button SceneButton(string text, Action action)
    {
        var button = new Button { Text = text, BackgroundColor = Color.FromArgb("#30466C"),
            TextColor = Colors.White, CornerRadius = 13, Padding = new Thickness(15, 9) };
        button.Clicked += (_, _) => action();
        return button;
    }
    private void ShowShapes()
    {
        _view.Scene = new Scene()
            .Add(Shape.Box(0xFF8DA9FF).At(-1.1f, 0, 0).Rotated(.15f, .3f, 0))
            .Add(Shape.Sphere(0xFFFFB775).At(0, 0, 0))
            .Add(Shape.Cylinder(0xFF72DBC5).At(1.1f, 0, 0));
        _caption.Text = "Shapes · Box, sphere and cylinder, each with its own color and transform.";
    }
    private void ShowStack()
    {
        _view.Scene = new Scene()
            .Add(Shape.Box(0xFF53699B).At(0, -.95f, 0).Scaled(2.6f, .25f, 1.8f))
            .Add(Shape.Cylinder(0xFF77D6C1).At(0, -.35f, 0).Scaled(.9f, 1, .9f))
            .Add(Shape.Sphere(0xFFFFC386).At(0, .45f, 0).Scaled(1.05f))
            .Add(Shape.Pyramid(0xFFDC98D9).At(0, 1.2f, 0).Scaled(.6f));
        _caption.Text = "Stack · Compose a scene by chaining Add, At, Scaled and Rotated.";
    }
    private void ShowOrbit()
    {
        var scene = new Scene().Add(Shape.Sphere(0xFFFFC16D).Scaled(.75f));
        for (var i = 0; i < 8; i++)
        {
            var angle = 2 * MathF.PI * i / 8;
            scene.Add(Shape.Box(i % 2 == 0 ? 0xFF94B2FF : 0xFF7EDBCB)
                .At(MathF.Cos(angle) * 1.35f, MathF.Sin(angle * 2) * .22f, MathF.Sin(angle) * 1.35f)
                .Rotated(0, angle, 0).Scaled(.32f));
        }
        _view.Scene = scene;
        _caption.Text = "Orbit · Repeated shapes share cached mesh data.";
    }
}

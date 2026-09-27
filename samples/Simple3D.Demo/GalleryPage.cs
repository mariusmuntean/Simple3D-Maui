using System.Diagnostics;
using Microsoft.Maui.Dispatching;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;

namespace Simple3D.Demo;

public sealed class GalleryPage : ContentPage
{
    private readonly SceneView _view = new() { HeightRequest = 440, SceneBackgroundColor = Color.FromArgb("#18243B") };
    private readonly Label _caption = new() { FontSize = 15, TextColor = Color.FromArgb("#A9B8D4") };
    private readonly Label _selection = new() { FontSize = 14, TextColor = Color.FromArgb("#8EE1CD") };
    private readonly IReadOnlyList<DemoScene> _scenes = DemoScenes.All;
    private readonly Button _animationButton;
    private DemoScene? _current;
    private IDispatcherTimer? _animationTimer;
    private Shape? _animatedShape;
    private long _lastAnimationTick;

    public GalleryPage()
    {
        BackgroundColor = Color.FromArgb("#0D1322");
        var title = new Label { Text = "Simple3D", FontSize = 34, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White };
        var subtitle = new Label { Text = "Little worlds, a few lines of C#.", FontSize = 16,
            TextColor = Color.FromArgb("#B7C6E2") };
        var scenes = new HorizontalStackLayout { Spacing = 8 };
        foreach (var sample in _scenes)
            scenes.Add(SceneButton(sample.Name, () => Show(sample)));
        var tools = new HorizontalStackLayout { Spacing = 8 };
        tools.Add(SceneButton("−", () => _view.Zoom(.8f)));
        tools.Add(SceneButton("Fit", () => _view.Camera.FitToScene(_view.Scene, (float)Math.Max(.1, _view.Width / Math.Max(1, _view.Height)))));
        tools.Add(SceneButton("Reset", () => { if (_current is not null) Show(_current); }));
        tools.Add(SceneButton("+", () => _view.Zoom(1.25f)));
        _animationButton = SceneButton("Animate", ToggleAnimation);
        tools.Add(_animationButton);
        _view.SelectionChanged += (_, shape) => _selection.Text = shape is null ? "Tap an object to inspect it" : $"Selected: {shape.Name ?? "unnamed shape"}";
        var sceneScroller = new ScrollView { Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = scenes };
        var toolScroller = new ScrollView { Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = tools };
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(22, 35), Spacing = 18,
            Children = { title, subtitle, sceneScroller, new Border
            {
                BackgroundColor = Color.FromArgb("#18243B"),
                Stroke = Color.FromArgb("#314361"), StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 },
                Content = _view
            }, toolScroller, _caption, _selection, new Label
            {
                Text = "Drag to orbit · Pinch to zoom · Tap to select · Animate a shape", TextColor = Color.FromArgb("#8BA1C1"), FontSize = 13
            } }
        }};
        var requested = Environment.GetEnvironmentVariable("SIMPLE3D_GALLERY_SCENE");
        Show(_scenes.FirstOrDefault(s => s.Name == requested) ?? _scenes[0]);
    }

    private static Button SceneButton(string text, Action action)
    {
        var button = new Button { Text = text, BackgroundColor = Color.FromArgb("#30466C"),
            TextColor = Colors.White, CornerRadius = 13, Padding = new Thickness(15, 9) };
        button.Clicked += (_, _) => action();
        return button;
    }

    private void Show(DemoScene sample)
    {
        StopAnimation();
        _current = sample;
        _view.Scene = sample.Scene;
        _view.Camera = new Camera(sample.Camera.Distance, sample.Camera.Yaw, sample.Camera.Pitch)
        {
            Target = sample.Camera.Target,
            Projection = sample.Camera.Projection,
            FieldOfView = sample.Camera.FieldOfView,
            OrthographicHeight = sample.Camera.OrthographicHeight,
            NearPlane = sample.Camera.NearPlane
        };
        _caption.Text = $"{sample.Name} · {sample.Description}";
        _selection.Text = "Tap an object to inspect it";
    }

    private void ToggleAnimation()
    {
        if (_animationTimer?.IsRunning == true) { StopAnimation(); return; }
        if (_current is null || _current.Scene.Shapes.Count == 0) return;
        _animationTimer ??= Dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _animationTimer.Tick -= AdvanceAnimation;
        _animationTimer.Tick += AdvanceAnimation;
        _animatedShape = _current.Scene.Shapes[0];
        _lastAnimationTick = Stopwatch.GetTimestamp();
        _view.MaximumRenderDimension = 768;
        _animationButton.Text = "Pause";
        _animationTimer.Start();
    }

    private void AdvanceAnimation(object? sender, EventArgs args)
    {
        if (_current is null || _animatedShape is null) { StopAnimation(); return; }
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Min(Stopwatch.GetElapsedTime(_lastAnimationTick, now).TotalSeconds, .1);
        _lastAnimationTick = now;
        var next = _animatedShape.Rotated(_animatedShape.Rotation.X,
            _animatedShape.Rotation.Y + (float)(seconds * .7), _animatedShape.Rotation.Z);
        if (!_current.Scene.Replace(_animatedShape, next)) { StopAnimation(); return; }
        _animatedShape = next;
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _animatedShape = null;
        _view.MaximumRenderDimension = DepthRenderer.MaximumDimension;
        _animationButton.Text = "Animate";
    }

    protected override void OnDisappearing()
    {
        StopAnimation();
        base.OnDisappearing();
    }
}

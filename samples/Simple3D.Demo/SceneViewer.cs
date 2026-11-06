using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;
using Shape = Simple3D.Core.Shape;

namespace Simple3D.Demo;

/// <summary>One scene, full bleed, with its controls floating over it: on phones a top bar, frame stats, camera rail,
/// selection chip and description card; in the wide layout a title block, a horizontal toolbar and inline stats.</summary>
internal sealed class SceneViewer : ContentView
{
    private readonly SceneView _view = new() { SceneBackgroundColor = Theme.SceneBackground };
    private readonly RendererSettings _renderer;
    private readonly Label _title = Theme.Label("", Theme.FontDisplay, 18, Theme.Text);
    private readonly Label _tag = Theme.Label("", Theme.FontMono, 11, Theme.Ember);
    private readonly Label _description = Theme.Label("", Theme.FontBody, 14, Theme.TextSoft);
    private readonly ActionButton _animate;
    private readonly Segmented _shading, _light;
    private readonly View _surfaceControls;
    private readonly SelectionChip _selection;
    private readonly FrameStats _stats;
    private readonly FrameClock _animationClock = new();
    private IDispatcherTimer? _statsTimer;
    private int _frames;
    private long _framesSince;
    private double _animationSeconds;

    public SceneViewer(DemoScene scene, RendererSettings renderer, bool wide = false, Action? back = null, Action? openRenderer = null)
    {
        _renderer = renderer;
        _animate = new ActionButton("Animate", ToggleAnimation, ActionStyle.Primary, Icon.Animate, wide ? 46 : 52);
        _animate.VerticalOptions = LayoutOptions.Center;
        _shading = new Segmented(["Smooth", "Flat"], 0, (i, selected) => Selected(i == 0 ? "Shading: smooth" : "Shading: flat", selected)) { AutomationId = "SurfaceShading" };
        foreach (var button in _shading.Buttons) SemanticProperties.SetHint(button, "Compare smooth vertex normals with flat triangle shading");
        _shading.Changed += index => { Current?.SurfaceComparison?.SetSmoothShading(index == 0); };
        _light = new Segmented(["Left", "Right"], 1, (i, selected) => Selected(i == 0 ? "Light: from left" : "Light: from right", selected)) { AutomationId = "SurfaceLight" };
        foreach (var button in _light.Buttons) SemanticProperties.SetHint(button, "Shine the directional light from the left or right side of the wave");
        _light.Changed += index => { Current?.SurfaceComparison?.SetLightFromLeft(index == 0); };
        _surfaceControls = SurfaceControls();
        _selection = new SelectionChip(_view, wide);
        _stats = new FrameStats(wide);
        _view.FrameSubmitted += (_, _) => _frames++;
        // The scenes' own cameras suit a landscape view; frame each scene for this view once its size is known.
        _view.SizeChanged += (_, _) => { if (_framePending) Frame(); };

        var root = new Grid { BackgroundColor = Theme.SceneBackground };
        root.Add(_view);
        root.Add(wide ? WideOverlay() : PhoneOverlay(back, openRenderer));
        Content = root;
        Show(scene);
    }

    public DemoScene? Current { get; private set; }
    public SceneView View => _view;

    private static string Selected(string description, bool selected) => selected ? $"{description}, selected" : description;

    private View PhoneOverlay(Action? back, Action? openRenderer)
    {
        var top = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        if (back is not null) top.Add(new IconButton(Icon.Back, "Back to scenes", back));
        _title.HorizontalTextAlignment = _tag.HorizontalTextAlignment = TextAlignment.Center;
        _title.LineBreakMode = _tag.LineBreakMode = LineBreakMode.TailTruncation;
        top.Add(new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, InputTransparent = true, Children = { _title, _tag } }, 1);
        if (openRenderer is not null) top.Add(new RendererChip(_renderer, openRenderer, floating: true), 2);
        var middle = new Grid { InputTransparent = true, CascadeInputTransparent = false };
        _stats.HorizontalOptions = LayoutOptions.Start;
        _stats.VerticalOptions = LayoutOptions.Start;
        var rail = Rail(horizontal: false);
        rail.HorizontalOptions = LayoutOptions.End;
        rail.VerticalOptions = LayoutOptions.Start;
        middle.Add(_stats);
        middle.Add(rail);
        var card = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        _description.VerticalOptions = LayoutOptions.Center;
        card.Add(_description);
        card.Add(_animate, 1);
        var bottom = new VerticalStackLayout
        {
            Spacing = 10, InputTransparent = true, CascadeInputTransparent = false,
            Children = { _selection, Panels.Card(new VerticalStackLayout { Spacing = 14, Children = { _surfaceControls, card } }) }
        };
        var overlay = new Grid
        {
            Padding = new Thickness(Theme.Margin, 8, Theme.Margin, 12), RowSpacing = 12, InputTransparent = true, CascadeInputTransparent = false,
            SafeAreaEdges = SafeAreaEdges.All,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        overlay.Add(top);
        overlay.Add(new FallbackBanner(_renderer), 0, 1);
        overlay.Add(middle, 0, 2);
        overlay.Add(bottom, 0, 3);
        return overlay;
    }

    private View WideOverlay()
    {
        _title.FontSize = 28;
        _tag.FontSize = 12;
        var heading = new VerticalStackLayout
        {
            Spacing = 4, MaximumWidthRequest = 460, HorizontalOptions = LayoutOptions.Start, InputTransparent = true, CascadeInputTransparent = false,
            Children = { _tag, _title, _description, _surfaceControls }
        };
        _surfaceControls.Margin = new Thickness(0, 10, 0, 0);
        var tools = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Children = { Rail(horizontal: true), _animate } };
        var top = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, InputTransparent = true, CascadeInputTransparent = false };
        top.Add(heading);
        top.Add(tools, 1);
        var bottom = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, InputTransparent = true, CascadeInputTransparent = false };
        _selection.HorizontalOptions = LayoutOptions.Start;
        _stats.HorizontalOptions = LayoutOptions.End;
        _stats.VerticalOptions = LayoutOptions.End;
        bottom.Add(_selection);
        bottom.Add(_stats, 1);
        var overlay = new Grid
        {
            Padding = new Thickness(24, 22, 24, 24), RowSpacing = 12, InputTransparent = true, CascadeInputTransparent = false,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        var banner = new FallbackBanner(_renderer) { MaximumWidthRequest = 560, HorizontalOptions = LayoutOptions.Start };
        overlay.Add(top);
        overlay.Add(banner, 0, 1);
        overlay.Add(bottom, 0, 3);
        return overlay;
    }

    private View SurfaceControls()
    {
        View Group(string label, Segmented control) => new VerticalStackLayout
        {
            Spacing = 6, Children = { Theme.Label(label, Theme.FontLabel, 12, Theme.TextMuted), control }
        };
        var grid = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        grid.Add(Group("Shading", _shading));
        grid.Add(Group("Light from", _light), 1);
        return grid;
    }

    private View Rail(bool horizontal)
    {
        var zoomIn = new IconButton(Icon.ZoomIn, "Zoom in", () => _view.Zoom(1.25f), floating: false);
        var zoomOut = new IconButton(Icon.ZoomOut, "Zoom out", () => _view.Zoom(.8f), floating: false);
        var fit = new IconButton(Icon.Fit, "Fit scene to view", Fit, floating: false);
        var reset = new IconButton(Icon.Reset, "Reset scene", Reset, floating: false);
        StackBase stack = horizontal
            ? new HorizontalStackLayout { Spacing = 2, Children = { zoomOut, zoomIn, fit, reset } }
            : new VerticalStackLayout
            {
                Spacing = 4,
                Children = { zoomIn, zoomOut, new BoxView { HeightRequest = 1, Margin = new Thickness(8, 2), Color = Theme.Line }, fit, reset }
            };
        return Panels.Overlay(stack, horizontal ? 24 : 26, horizontal ? 3 : 4);
    }

    private bool _framePending;

    private void Fit() => _view.Camera.FitToScene(_view.Scene, (float)Math.Max(.1, _view.Width / Math.Max(1, _view.Height)));

    private void Frame()
    {
        _framePending = _view.Width <= 0 || _view.Height <= 0;
        if (!_framePending) Fit();
    }

    public void Reset()
    {
        if (Current is not null) Show(Current);
    }

    public void Show(DemoScene sample)
    {
        StopAnimation();
        Current = sample;
        sample.SurfaceComparison?.Reset();
        UpdateSurfaceControls();
        _animationSeconds = 0;
        sample.Animate(0);
        _view.Scene = sample.Scene;
        _view.Camera = new Camera(sample.Camera.Distance, sample.Camera.Yaw, sample.Camera.Pitch)
        {
            Target = sample.Camera.Target,
            Projection = sample.Camera.Projection,
            FieldOfView = sample.Camera.FieldOfView,
            OrthographicHeight = sample.Camera.OrthographicHeight,
            NearPlane = sample.Camera.NearPlane
        };
        _view.ClearSelection();
        Frame();
        _title.Text = sample.Name;
        _tag.Text = SceneCatalog.LongTag(sample.Name);
        _description.Text = sample.Description;
    }

#if DEBUG
    internal void ShowState(string state, Action openRenderer)
    {
        switch (state)
        {
            case "renderer": openRenderer(); break;
            case "fallback": _view.ReportHardwareFailure(new InvalidOperationException("Simulated GPU loss for a screenshot")); break;
            case "stats": _stats.Expand(true); break;
            case "selected": _view.SelectAt(_view.Width * .62, _view.Height * .58, _view.Width, _view.Height); break;
        }
    }
#endif

    private void UpdateSurfaceControls()
    {
        var comparison = Current?.SurfaceComparison;
        _surfaceControls.IsVisible = comparison is not null;
        _shading.Select(comparison?.IsSmooth == false ? 1 : 0, notify: false);
        _light.Select(comparison?.IsLightFromLeft == true ? 0 : 1, notify: false);
    }

    private void ToggleAnimation()
    {
        if (_animationClock.IsRunning) { StopAnimation(); return; }
        if (Current is null) return;
        // Once per display refresh, at the screen's real rate.
        _animationClock.Tick -= AdvanceAnimation;
        _animationClock.Tick += AdvanceAnimation;
        _view.MaximumRenderDimension = 768;
        _animate.Update("Pause", ActionStyle.Active, Icon.Pause);
        _animationClock.Start();
    }

    private void AdvanceAnimation(TimeSpan elapsed, TimeSpan delta)
    {
        if (Current is null) { StopAnimation(); return; }
        // A long pause (backgrounding, a slow frame) must not make the animation jump.
        _animationSeconds += Math.Min(delta.TotalSeconds, .1);
        Current.Animate((float)_animationSeconds);
    }

    public void StopAnimation()
    {
        _animationClock.Stop();
        _view.MaximumRenderDimension = DepthRenderer.MaximumDimension;
        _animate.Update("Animate", ActionStyle.Primary, Icon.Animate);
    }

    /// <summary>On screen: count frames for the stats and follow the renderer choice.</summary>
    public void Start()
    {
        _renderer.Attach(_view);
        _statsTimer ??= Dispatcher.CreateTimer();
        _statsTimer.Interval = TimeSpan.FromMilliseconds(500);
        _statsTimer.Tick -= UpdateStats;
        _statsTimer.Tick += UpdateStats;
        _frames = 0;
        _framesSince = Stopwatch.GetTimestamp();
        _statsTimer.Start();
    }

    public void Stop()
    {
        _statsTimer?.Stop();
        StopAnimation();
        _renderer.Detach(_view);
    }

    private void UpdateStats(object? sender, EventArgs args)
    {
        var now = Stopwatch.GetTimestamp();
        var fps = _frames / Math.Max(.001, Stopwatch.GetElapsedTime(_framesSince, now).TotalSeconds);
        _frames = 0;
        _framesSince = now;
        _stats.Update(fps, _view);
    }
}

/// <summary>"Selected" + the picked shape's name and a clear button, or a dashed hint while nothing is selected.</summary>
internal sealed class SelectionChip : Grid
{
    private readonly Label _name = Theme.Label("", Theme.FontLabel, 14, Theme.Text);
    private readonly Border _selected, _hint;

    public SelectionChip(SceneView view, bool wide)
    {
        HorizontalOptions = LayoutOptions.Start;
        _name.LineBreakMode = LineBreakMode.TailTruncation;
        _name.VerticalOptions = LayoutOptions.Center;
        var label = Theme.Label("Selected", Theme.FontLabel, 12, Theme.EmberLight);
        label.VerticalOptions = LayoutOptions.Center;
        var clear = new IconButton(Icon.Close, "Clear selection", view.ClearSelection, floating: false, iconSize: 14, background: Theme.Press);
        var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        row.Add(label);
        row.Add(_name, 1);
        row.Add(clear, 2);
        _selected = new Border
        {
            HeightRequest = 48, Padding = new Thickness(16, 0, 2, 0), BackgroundColor = Theme.EmberTint, Stroke = Theme.Ember, StrokeThickness = 1,
            StrokeShape = Theme.Rounded(24), Content = row
        };
        var touch = !(OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
        var text = !wide || touch ? (wide ? "Tap a part to select it · drag to orbit · pinch to zoom" : "Drag to orbit · tap a part to select it")
            : "Click a part to select it · drag to orbit · scroll to zoom";
        var hintIcon = Icons.Create(touch ? Icon.Hand : Icon.Pointer, Theme.TextMuted, 18);
        hintIcon.VerticalOptions = LayoutOptions.Center;
        var hintLabel = Theme.Label(text, Theme.FontBody, 13, Theme.TextSoft);
        hintLabel.VerticalOptions = LayoutOptions.Center;
        _hint = new Border
        {
            MinimumHeightRequest = Theme.Touch, Padding = new Thickness(16, 10), BackgroundColor = wide ? Theme.Overlay : Colors.Transparent,
            Stroke = wide ? Theme.Line : Theme.LineStrong, StrokeThickness = 1, StrokeDashArray = wide ? null : [4, 3], StrokeShape = Theme.Rounded(22),
            InputTransparent = true, Content = new HorizontalStackLayout { Spacing = 10, Children = { hintIcon, hintLabel } }
        };
        Add(_hint);
        Add(_selected);
        view.SelectionChanged += (_, shape) => Update(shape);
        Update(null);
    }

    private void Update(Shape? shape)
    {
        _selected.IsVisible = shape is not null;
        _hint.IsVisible = shape is null;
        _name.Text = shape is null ? "" : shape.Name ?? "unnamed shape";
    }
}

/// <summary>Frame statistics. Frames are drawn on demand, so a still scene reads "idle". On phones a pill that expands to the
/// four-line panel; in the wide layout one inline row.</summary>
internal sealed class FrameStats : ContentView
{
    private readonly Label _fps, _frame = Line(), _backend = Line(), _triangles = Line();
    private readonly Label _pill = Theme.Label("idle", Theme.FontMono, 12, Theme.TextSoft);
    private readonly View? _collapsed, _expanded;
    private readonly Button? _expand;

    public FrameStats(bool wide)
    {
        AutomationId = "FrameStats";
        _fps = Theme.Label("idle", Theme.FontMonoMedium, wide ? 12 : 18, Theme.EmberLight);
        if (wide)
        {
            InputTransparent = true;
            _backend.LineBreakMode = LineBreakMode.TailTruncation;
            Content = Panels.Overlay(new HorizontalStackLayout { Spacing = 14, Children = { _fps, _backend, _triangles } }, Theme.RadiusSmall, new Thickness(14, 10));
            return;
        }
        var pulse = Icons.Create(Icon.FrameStats, Theme.TextMuted, 14);
        pulse.VerticalOptions = _pill.VerticalOptions = LayoutOptions.Center;
        var pill = Panels.Overlay(new HorizontalStackLayout { Spacing = 6, Children = { pulse, _pill } }, 22, new Thickness(14, 0));
        pill.HeightRequest = Theme.Touch;
        var collapsed = new Tappable(pill, null, () => Expand(true));
        _expand = collapsed.Button;
        _collapsed = collapsed;
        var collapse = new IconButton(Icon.Collapse, "Hide frame stats", () => Expand(false), floating: false, iconSize: 16, color: Theme.TextMuted)
        {
            Margin = new Thickness(0, -12, -12, -12), HorizontalOptions = LayoutOptions.End
        };
        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        _fps.VerticalOptions = LayoutOptions.Center;
        head.Add(_fps);
        head.Add(collapse, 1);
        var panel = Panels.Card(new VerticalStackLayout { Spacing = 4, Children = { head, _frame, _backend, _triangles } }, 16, new Thickness(14, 12));
        panel.WidthRequest = 196;
        _expanded = panel;
        Expand(false);
    }

    internal void Expand(bool expanded)
    {
        Content = expanded ? _expanded : _collapsed;
        UpdateDescription();
    }

    public void Update(double fps, SceneView view)
    {
        var rate = fps < .5 ? "idle" : $"{fps:0} fps";
        _fps.Text = _pill.Text = rate;
        _frame.Text = view.LastFrameWidth > 0 ? $"{view.LastFrameMilliseconds:0.0} ms CPU · {view.LastFrameWidth}×{view.LastFrameHeight}" : "no frame yet";
        _backend.Text = view.ActiveRenderingBackend switch
        {
            // The API only ("Metal", "OpenGL ES 3"); the renderer sheet has the full device name.
            RenderingBackend.Hardware => view.RenderingDiagnostic?.Split(" / ")[0] ?? "Hardware",
            var backend => backend.ToString()
        };
        // Animated scenes may swap shapes, so recount (cheap at 2 Hz).
        _triangles.Text = $"{CountTriangles(view.Scene):N0} triangles";
        UpdateDescription();
    }

    private void UpdateDescription()
    {
        if (_expand is not null) SemanticProperties.SetDescription(_expand, $"Frame stats: {_pill.Text}. Show details");
    }

    private static long CountTriangles(Scene scene)
    {
        long count = 0;
        foreach (var shape in scene.Shapes) Add(shape);
        return count;
        void Add(Shape shape) { count += shape.TriangleCount; foreach (var child in shape.Children) Add(child); }
    }

    private static Label Line()
    {
        var label = Theme.Label("", Theme.FontMono, 12, Theme.TextSoft);
        label.LineBreakMode = LineBreakMode.TailTruncation;
        return label;
    }
}

using Simple3D.Maui;
using Simple3D.Shared;

namespace Simple3D.Demo;

/// <summary>The scene catalogue. Phones show a grid of cards, each opening the scene's viewer; wide windows (tablets, Mac)
/// show a sidebar with the scenes and the renderer next to the viewer.</summary>
public sealed class GalleryPage : ContentPage
{
    // Each opened page gets its own scene: a scene view stays subscribed to its scene, so sharing one would keep closed pages alive.
    private static readonly (string Name, Func<DemoScene> Create)[] Scenes = [.. DemoScenes.Factories.Select(create => (create().Name, create))];
    private readonly RendererSettings _renderer = new();
    private readonly RendererSheet _sheet;
    private readonly Grid _root = new();
    private bool? _wide;
    private bool _visible;
    private SceneViewer? _wideViewer;
    private SceneViewerPage? _viewerPage;
    private EventHandler? _sidebarStatus;
    private readonly List<(string Scene, Border Row, Label Name)> _sidebarRows = [];

    public GalleryPage()
    {
        BackgroundColor = Theme.Canvas;
        Background = Theme.Glow(Theme.Canvas, 0, .9);
        SafeAreaEdges = SafeAreaEdges.None;
        NavigationPage.SetHasNavigationBar(this, false);
        _sheet = new RendererSheet(_renderer);
        Navigate = page => Navigation.PushAsync(page);
        Content = _root;
        Arrange(wide: false);
    }

    /// <summary>Opens a scene page; tests replace it to capture the page instead of navigating.</summary>
    internal Func<Page, Task> Navigate { get; set; }
    /// <summary>The viewer on screen: the last opened scene page, or the one beside the sidebar.</summary>
    internal SceneViewer? Viewer => _wide == true ? _wideViewer : _viewerPage?.Viewer;

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        Arrange(width >= Theme.WideBreakpoint);
        OpenRequested();
    }

    // Automated checks open a scene or Mirror directly: SIMPLE3D_GALLERY_SCENE, once the layout is known.
    private void OpenRequested()
    {
        if (!_visible || Environment.GetEnvironmentVariable("SIMPLE3D_GALLERY_SCENE") is not { } requested) return;
        Environment.SetEnvironmentVariable("SIMPLE3D_GALLERY_SCENE", null);
        if (requested == "Mirror") OpenMirror();
        else if (Scenes.Any(s => s.Name == requested)) Dispatcher.Dispatch(() => Open(requested));
    }

    private void Arrange(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        var current = _wideViewer?.Current?.Name;
        _wideViewer?.Stop();
        _wideViewer = null;
        if (_sidebarStatus is not null) _renderer.Changed -= _sidebarStatus;
        _sidebarStatus = null;
        _sidebarRows.Clear();
        _root.Clear();
        _root.Add(wide ? Wide(current) : Phone());
        _root.Add(_sheet);
        if (_visible) _wideViewer?.Start();
    }

    public async void Open(string scene)
    {
        var sample = Scenes.Single(s => s.Name == scene).Create();
        if (_wideViewer is { } viewer) { viewer.Show(sample); HighlightSidebar(); return; }
        _viewerPage = new SceneViewerPage(sample, _renderer);
        await Navigate(_viewerPage);
    }

    private void OpenMirror()
    {
#if IOS || MACCATALYST || ANDROID
        // Mirror needs the camera, so it opens as its own full-screen page instead of a gallery scene.
        Navigation.PushModalAsync(new Mirror.MirrorPage(_renderer.Mode));
#endif
    }

    private static bool HasMirror =>
#if IOS || MACCATALYST || ANDROID
        true;
#else
        false;
#endif

    private View Phone()
    {
        var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 0), ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        header.Add(AppMark(40));
        header.Add(Brand(20, 13), 1);
        header.Add(new RendererChip(_renderer, _sheet.Show, floating: false), 2);
        var scenes = Theme.Label("Scenes", Theme.FontDisplay, 28, Theme.Text);
        scenes.Margin = new Thickness(4, 0);
        SemanticProperties.SetHeadingLevel(scenes, SemanticHeadingLevel.Level1);
        var grid = new Grid { ColumnSpacing = Theme.Gap, RowSpacing = Theme.Gap, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        var cards = Scenes.Select(scene => Card(scene.Name, SceneCatalog.ShortTag(scene.Name), SceneImage(scene.Name), () => Open(scene.Name))).ToList();
        if (HasMirror)
        {
            var mirror = Card("Mirror", SceneCatalog.MirrorTag, MirrorImage(), OpenMirror);
            SemanticProperties.SetHint(mirror.Button, MirrorHint);
            cards.Add(mirror);
        }
        for (var i = 0; i < cards.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(cards[i], i % 2, i / 2);
        }
        return new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Content = new VerticalStackLayout
            {
                Spacing = 18, Padding = new Thickness(Theme.Margin, 12, Theme.Margin, 24), MaximumWidthRequest = 720,
                Children = { header, scenes, grid }
            }
        };
    }

    private const string MirrorHint = "Opens a camera scene in which a 3D frog copies your movement";

    private static Tappable Card(string name, string tag, View image, Action open)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 2, Padding = new Thickness(12, 10, 12, 12),
            Children = { Theme.Label(name, Theme.FontLabel, 15, Theme.Text), Theme.Label(tag, Theme.FontMono, 11, Theme.Ember) }
        };
        var card = new Tappable(new Border
        {
            BackgroundColor = Theme.Surface, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(Theme.RadiusCard),
            Content = new VerticalStackLayout { Children = { image, text } }
        }, name, open);
        SemanticProperties.SetHint(card.Button, tag);
        return card;
    }

    private static View SceneImage(string scene) => new Image
    {
        Source = SceneCatalog.Thumbnail(scene), Aspect = Aspect.AspectFill, HeightRequest = 118, BackgroundColor = Theme.SceneBackground
    };

    private static View MirrorImage()
    {
        var grid = new Grid
        {
            HeightRequest = 118, Background = Theme.Glow(Theme.SceneBackground, .6, .8), ColumnSpacing = 6, RowSpacing = 6,
            HorizontalOptions = LayoutOptions.Fill,
            ColumnDefinitions = { new(GridLength.Star), new(44), new(44), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Star), new(44), new(44), new(GridLength.Star) }
        };
        var models = Simple3D.Shared.Mirror.MirrorModels.All;
        for (var i = 0; i < Math.Min(4, models.Count); i++)
            grid.Add(Avatar(models[i].File, 44, 12), 1 + i % 2, 1 + i / 2);
        return grid;
    }

    private static View Avatar(string file, double size, double radius) => new Border
    {
        WidthRequest = size, HeightRequest = size, StrokeThickness = 0, StrokeShape = Theme.Rounded(radius),
        Content = new Image { Aspect = Aspect.AspectFill, Source = SceneCatalog.Character(file) }
    };

    private static View AppMark(double size)
    {
        var art = size * .7;
        var cube = new Grid
        {
            WidthRequest = art, HeightRequest = art, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Icons.Shape("M64 22 100 43 64 64 28 43z", 128, art).WithFill(Theme.CubeTop),
                Icons.Shape("M28 43 64 64v41L28 84z", 128, art).WithFill(Theme.CubeLeft),
                Icons.Shape("M100 43 64 64v41l36-21z", 128, art).WithFill(Theme.CubeRight)
            }
        };
        var mark = new Border
        {
            WidthRequest = size, HeightRequest = size, StrokeThickness = 0, StrokeShape = Theme.Rounded(size / 4), BackgroundColor = Theme.Raised,
            InputTransparent = true, VerticalOptions = LayoutOptions.Center, Content = cube
        };
        AutomationProperties.SetIsInAccessibleTree(mark, false);
        return mark;
    }

    private static View Brand(double title, double subtitle) => new VerticalStackLayout
    {
        VerticalOptions = LayoutOptions.Center,
        Children = { Theme.Label("Simple3D", Theme.FontDisplay, title, Theme.Text), Theme.Label("Interactive 3D for .NET MAUI", Theme.FontBody, subtitle, Theme.TextMuted) }
    };

    private View Wide(string? current)
    {
        _wideViewer = new SceneViewer((Scenes.FirstOrDefault(s => s.Name == current).Create ?? Scenes[0].Create)(), _renderer, wide: true);
        var brand = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 0), ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        brand.Add(AppMark(40));
        brand.Add(Brand(18, 12), 1);
        var list = new VerticalStackLayout { Spacing = 4, Children = { Caption("SCENES", new Thickness(8, 0, 8, 6)) } };
        foreach (var (name, _) in Scenes) list.Add(SidebarRow(name));
        var top = new VerticalStackLayout { Spacing = 18, Children = { brand, list } };
        if (HasMirror) top.Add(MirrorRow());
        RenderingMode[] modes = [RenderingMode.Auto, RenderingMode.Hardware, RenderingMode.Software];
        var renderer = new Segmented(modes.Select(m => m.ToString()).ToList(), Array.IndexOf(modes, _renderer.Mode),
            (i, selected) => selected ? $"{modes[i]} renderer, selected" : $"{modes[i]} renderer", strong: true);
        renderer.Changed += index => _renderer.Select(modes[index]);
        var dot = new RendererDot();
        var status = Theme.Label("", Theme.FontMono, 12, Theme.TextSoft);
        status.LineBreakMode = LineBreakMode.TailTruncation;
        // Held in a field: the renderer settings only hold their subscribers weakly.
        _sidebarStatus = (_, _) =>
        {
            renderer.Select(Array.IndexOf(modes, _renderer.Mode), notify: false);
            dot.Update(_renderer);
            status.Text = _renderer.Backend is { } backend ? $"{backend} · {_renderer.Diagnostic}" : _renderer.Mode.ToString();
        };
        _renderer.Changed += _sidebarStatus;
        _sidebarStatus(null, EventArgs.Empty);
        var statusRow = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 0), ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        statusRow.Add(dot);
        statusRow.Add(status, 1);
        var bottom = new VerticalStackLayout { Spacing = 10, Children = { Caption("RENDERER", new Thickness(4, 0)), renderer, statusRow } };
        var sidebarContent = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 18 };
        sidebarContent.Add(new ScrollView { Content = top });
        sidebarContent.Add(bottom, 0, 1);
        var sidebar = new Border
        {
            WidthRequest = 320, Padding = new Thickness(16, 20), BackgroundColor = Theme.Surface, Stroke = Theme.Line, StrokeThickness = 1,
            StrokeShape = Theme.Rounded(Theme.RadiusPanel), Content = sidebarContent
        };
        SemanticProperties.SetDescription(sidebar, "Gallery");
        var main = new Border
        {
            StrokeThickness = 1, Stroke = Theme.Line, StrokeShape = Theme.Rounded(Theme.RadiusPanel), BackgroundColor = Theme.SceneBackground,
            Content = _wideViewer
        };
        var layout = new Grid
        {
            Padding = Theme.Margin, ColumnSpacing = Theme.Margin, SafeAreaEdges = SafeAreaEdges.All,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }
        };
        layout.Add(sidebar);
        layout.Add(main, 1);
        HighlightSidebar();
        return new Grid { Background = Theme.Glow(Theme.Canvas, .25, .8), Children = { layout } };
    }

    private static Label Caption(string text, Thickness margin)
    {
        var label = Theme.Label(text, Theme.FontLabel, 12, Theme.TextMuted);
        label.CharacterSpacing = 1;
        label.Margin = margin;
        return label;
    }

    private View SidebarRow(string scene)
    {
        var image = new Border
        {
            WidthRequest = 40, HeightRequest = 40, StrokeThickness = 0, StrokeShape = Theme.Rounded(10), BackgroundColor = Theme.SceneBackground,
            Content = new Image { Source = SceneCatalog.Thumbnail(scene), Aspect = Aspect.AspectFill }
        };
        var name = Theme.Label(scene, Theme.FontLabel, 14, Theme.TextSoft);
        name.VerticalOptions = LayoutOptions.Center;
        var content = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        content.Add(image);
        content.Add(name, 1);
        var row = new Border { MinimumHeightRequest = 52, Padding = new Thickness(8, 6), StrokeThickness = 1, StrokeShape = Theme.Rounded(Theme.RadiusSmall), Content = content };
        var item = new Tappable(row, scene, () => Open(scene));
        SemanticProperties.SetHint(item.Button, SceneCatalog.ShortTag(scene));
        _sidebarRows.Add((scene, row, name));
        return item;
    }

    private void HighlightSidebar()
    {
        foreach (var (scene, row, name) in _sidebarRows)
        {
            var current = scene == _wideViewer?.Current?.Name;
            row.BackgroundColor = current ? Theme.EmberTint : Colors.Transparent;
            row.Stroke = current ? Theme.Ember : Colors.Transparent;
            name.TextColor = current ? Theme.Text : Theme.TextSoft;
        }
    }

    private View MirrorRow()
    {
        var models = Simple3D.Shared.Mirror.MirrorModels.All;
        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { Theme.Label("Mirror", Theme.FontLabel, 14, Theme.Text), Theme.Label("A character copies your moves", Theme.FontBody, 12, Theme.TextSoft) }
        };
        var arrow = Icons.Create(Icon.Open, Theme.EmberLight, 20, center: true);
        var content = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(Avatar(models[0].File, 44, 12));
        content.Add(text, 1);
        content.Add(arrow, 2);
        var row = new Tappable(new Border
        {
            Padding = 12, Stroke = Theme.EmberTint, StrokeThickness = 1, StrokeShape = Theme.Rounded(16), Content = content,
            Background = new LinearGradientBrush([new GradientStop(Theme.EmberTint, 0), new GradientStop(Theme.Surface, .9f)], new Point(0, 0), new Point(1, 1))
        }, "Mirror", OpenMirror);
        SemanticProperties.SetHint(row.Button, MirrorHint);
        return row;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _wideViewer?.Start();
        if (Width > 0) OpenRequested();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _wideViewer?.Stop();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_sheet.IsOpen) return base.OnBackButtonPressed();
        _sheet.Hide();
        return true;
    }

    /// <summary>Stops any running scene animation, for example when the app goes to the background.</summary>
    internal void StopAnimation()
    {
        _wideViewer?.StopAnimation();
        _viewerPage?.Viewer.StopAnimation();
    }
}

/// <summary>A scene on its own page (phones): the viewer plus the renderer sheet its chip opens.</summary>
internal sealed class SceneViewerPage : ContentPage
{
    private readonly RendererSheet _sheet;
    public SceneViewer Viewer { get; }

    public SceneViewerPage(DemoScene scene, RendererSettings renderer)
    {
        BackgroundColor = Theme.SceneBackground;
        SafeAreaEdges = SafeAreaEdges.None;
        NavigationPage.SetHasNavigationBar(this, false);
        _sheet = new RendererSheet(renderer);
        Viewer = new SceneViewer(scene, renderer, back: () => Navigation.PopAsync(), openRenderer: _sheet.Show);
        renderer.Attach(Viewer.View);
        Content = new Grid { Children = { Viewer, _sheet } };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Viewer.Start();
#if DEBUG
        // Screenshots of each state: SIMPLE3D_GALLERY_SHOW=renderer|fallback|stats|selected.
        var show = Environment.GetEnvironmentVariable("SIMPLE3D_GALLERY_SHOW");
        Environment.SetEnvironmentVariable("SIMPLE3D_GALLERY_SHOW", null);
        if (show is not null) Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), () => Viewer.ShowState(show, _sheet.Show));
#endif
    }

    protected override void OnDisappearing()
    {
        Viewer.Stop();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_sheet.IsOpen) return base.OnBackButtonPressed();
        _sheet.Hide();
        return true;
    }
}

using Microsoft.Maui.Controls.Shapes;
using Simple3D.Maui;

namespace Simple3D.Demo;

/// <summary>The renderer the person picked, applied to whichever scene view is on screen, and what that view draws with.</summary>
internal sealed class RendererSettings
{
    private SceneView? _view;
    public RenderingMode Mode { get; private set; } = RenderingMode.Auto;
    /// <summary>Raised when the mode, the attached view or its backend changes.</summary>
    public event Action? Changed;

    public SceneView? View => _view;
    public RenderingBackend? Backend => _view?.ActiveRenderingBackend;
    public string? Diagnostic => _view?.RenderingDiagnostic;
    /// <summary>Auto gave up on the GPU for this view and draws in software.</summary>
    public bool IsFallback => Mode == RenderingMode.Auto && Backend == RenderingBackend.Software;

    public void Attach(SceneView view)
    {
        if (ReferenceEquals(view, _view)) return;
        Detach();
        _view = view;
        view.RenderingMode = Mode;
        view.RenderingBackendChanged += OnBackendChanged;
        Changed?.Invoke();
    }

    public void Detach(SceneView? view = null)
    {
        if (_view is null || view is not null && !ReferenceEquals(view, _view)) return;
        _view.RenderingBackendChanged -= OnBackendChanged;
        _view = null;
        Changed?.Invoke();
    }

    /// <summary>Picks a mode. Picking the current mode again retries the GPU: the view only re-initializes on a change of
    /// mode, so it passes through Software first.</summary>
    public void Select(RenderingMode mode)
    {
        Mode = mode;
        if (_view is { } view)
        {
            if (view.RenderingMode == mode && mode != RenderingMode.Software) view.RenderingMode = RenderingMode.Software;
            view.RenderingMode = mode;
        }
        Changed?.Invoke();
    }

    public void Retry() => Select(Mode);

    private void OnBackendChanged(object? sender, EventArgs e) => Changed?.Invoke();

    /// <summary>What the chip shows: the attached view's backend, or what the mode will use before any view draws.</summary>
    public (string Text, bool Software, bool Starting) Chip => Backend switch
    {
        RenderingBackend.Software => ("CPU", true, false),
        RenderingBackend.Hardware => ("GPU", false, false),
        RenderingBackend.None when _view is not null => ("Starting", false, true),
        _ => Mode == RenderingMode.Software ? ("CPU", true, false) : ("GPU", false, false)
    };

    public string Description => Chip switch
    {
        { Starting: true } => $"Renderer: {Mode}, starting",
        { Software: true } => $"Renderer: {Mode}, drawing in software",
        _ => $"Renderer: {Mode}, drawing on the GPU"
    };
}

/// <summary>The 44 pt pill showing GPU, CPU or Starting; tapping it opens the renderer sheet.</summary>
internal sealed class RendererChip : Grid
{
    private readonly RendererSettings _settings;
    private readonly Border _face = new() { StrokeShape = Theme.Rounded(22), StrokeThickness = 1, Padding = new Thickness(14, 0), InputTransparent = true };
    private readonly Border _dot = new() { WidthRequest = 8, HeightRequest = 8, StrokeShape = new Ellipse(), VerticalOptions = LayoutOptions.Center };
    private readonly Label _text = new() { FontFamily = Theme.FontLabel, FontSize = 13, VerticalOptions = LayoutOptions.Center };
    public Button Button { get; }

    public RendererChip(RendererSettings settings, Action open, bool floating)
    {
        _settings = settings;
        HeightRequest = Theme.Touch;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Center;
        _face.BackgroundColor = floating ? Theme.Overlay : Theme.Surface;
        _face.Content = new HorizontalStackLayout { Spacing = 8, Children = { _dot, _text } };
        AutomationProperties.SetIsInAccessibleTree(_face, false);
        Button = Tap.Over(_face, null, open);
        SemanticProperties.SetHint(Button, "Opens the renderer settings");
        Add(_face);
        Add(Button);
        settings.Changed += Update;
        Update();
    }

    private void Update()
    {
        var (text, software, starting) = _settings.Chip;
        _text.Text = text;
        _text.TextColor = software ? Theme.IceText : starting ? Theme.TextSoft : Theme.Text;
        _face.Stroke = software ? Theme.IceBorder : starting ? Theme.LineStrong : Theme.Line;
        _face.StrokeDashArray = starting ? [4, 3] : null;
        _dot.BackgroundColor = starting ? Colors.Transparent : software ? Theme.Ice : Theme.Ember;
        _dot.Stroke = starting ? Theme.TextMuted : null;
        _dot.StrokeThickness = starting ? 1.5 : 0;
        _dot.Shadow = software || starting ? null! : Theme.EmberGlow(10, 1);
        SemanticProperties.SetDescription(Button, _settings.Description);
    }
}

/// <summary>The renderer choice: a bottom sheet over a scrim with the three modes and what is drawing now.</summary>
internal sealed class RendererSheet : Grid
{
    private readonly RendererSettings _settings;
    private readonly Border _sheet;
    private readonly BoxView _scrim = new() { Color = Theme.Scrim };
    private readonly List<(RenderingMode Mode, Icon Icon, Border Row, Border Tile, Border Radio, Button Button)> _options = [];
    private readonly Border _nowDot = new() { WidthRequest = 8, HeightRequest = 8, StrokeShape = new Ellipse(), StrokeThickness = 0, VerticalOptions = LayoutOptions.Center };
    private readonly Label _nowBackend = Theme.Label("", Theme.FontLabel, 15, Theme.Text);
    private readonly Label _nowDetail = Theme.Label("", Theme.FontMono, 12, Theme.TextSoft);

    public RendererSheet(RendererSettings settings)
    {
        _settings = settings;
        IsVisible = false;
        _nowDetail.AutomationId = "RenderingBackend";
        var close = new IconButton(Icon.Close, "Close", Hide, floating: false, background: Theme.Raised);
        var title = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Theme.Label("Renderer", Theme.FontDisplay, 22, Theme.Text),
                Theme.Label("How scenes are drawn. Picking a mode again retries the GPU.", Theme.FontBody, 14, Theme.TextMuted)
            }
        };
        var header = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        header.Add(title);
        header.Add(close, 1);
        var options = new VerticalStackLayout { Spacing = 8, AutomationId = "RenderingMode" };
        SemanticProperties.SetDescription(options, "Rendering mode");
        options.Add(Option(RenderingMode.Auto, Icon.Auto, "GPU when it works, software when it doesn't.", isDefault: true));
        options.Add(Option(RenderingMode.Hardware, Icon.Hardware, "GPU only: Metal or OpenGL ES. Blank if the GPU fails."));
        options.Add(Option(RenderingMode.Software, Icon.Software, "Drawn on the CPU with SkiaSharp. Works everywhere."));
        var now = new Border
        {
            Padding = new Thickness(16, 14), BackgroundColor = Theme.Canvas, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(16),
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    Theme.Label("DRAWING NOW", Theme.FontLabel, 12, Theme.TextMuted),
                    new HorizontalStackLayout { Spacing = 8, Children = { _nowDot, _nowBackend } },
                    _nowDetail
                }
            }
        };
        _sheet = new Border
        {
            VerticalOptions = LayoutOptions.End, MaximumWidthRequest = 560, BackgroundColor = Theme.Surface, Stroke = Theme.Line, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(Theme.RadiusSheet, Theme.RadiusSheet, 0, 0) },
            Padding = new Thickness(20, 10, 20, 0),
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new BoxView { WidthRequest = 36, HeightRequest = 5, CornerRadius = 3, Color = Theme.Line, HorizontalOptions = LayoutOptions.Center },
                    header, options, now
                }
            }
        };
        // Runs under the home indicator, with room for it below the content.
        _sheet.Padding = new Thickness(20, 10, 20, 34);
        var dismiss = Tap.Over(_scrim, null, Hide);
        SemanticProperties.SetDescription(dismiss, "Close renderer sheet");
        Add(_scrim);
        Add(dismiss);
        Add(_sheet);
        settings.Changed += Update;
        Update();
    }

    public bool IsOpen => IsVisible;

    public async void Show()
    {
        if (IsVisible) return;
        Update();
        IsVisible = true;
        _scrim.Opacity = 0;
        _sheet.TranslationY = 600;
        await Task.WhenAll(_scrim.FadeToAsync(1, 180), _sheet.TranslateToAsync(0, 0, 240, Easing.CubicOut));
    }

    public async void Hide()
    {
        if (!IsVisible) return;
        await Task.WhenAll(_scrim.FadeToAsync(0, 160), _sheet.TranslateToAsync(0, Math.Max(400, _sheet.Height), 200, Easing.CubicIn));
        IsVisible = false;
    }

    private View Option(RenderingMode mode, Icon icon, string explanation, bool isDefault = false)
    {
        var name = new HorizontalStackLayout { Spacing = 8, Children = { Theme.Label(mode.ToString(), Theme.FontLabel, 16, Theme.Text) } };
        if (isDefault)
        {
            var tag = Theme.Label("Default", Theme.FontLabel, 12, Theme.Ember);
            tag.VerticalOptions = LayoutOptions.Center;
            name.Add(tag);
        }
        var tile = new Border { WidthRequest = 40, HeightRequest = 40, StrokeThickness = 0, StrokeShape = Theme.Rounded(12), VerticalOptions = LayoutOptions.Center };
        var radio = new Border
        {
            WidthRequest = 22, HeightRequest = 22, StrokeShape = new Ellipse(), VerticalOptions = LayoutOptions.Center,
            Content = new Ellipse { WidthRequest = 12, HeightRequest = 12, Fill = Theme.Ember, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
        var grid = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        grid.Add(tile);
        grid.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { name, Theme.Label(explanation, Theme.FontBody, 13, Theme.TextMuted) } }, 1);
        grid.Add(radio, 2);
        var row = new Border { Padding = new Thickness(16, 14), BackgroundColor = Theme.Raised, StrokeThickness = 1.5, StrokeShape = Theme.Rounded(16), InputTransparent = true, Content = grid };
        AutomationProperties.SetIsInAccessibleTree(row, false);
        var button = Tap.Over(row, null, () => _settings.Select(mode));
        button.AutomationId = $"RenderingMode{mode}";
        SemanticProperties.SetHint(button, explanation);
        _options.Add((mode, icon, row, tile, radio, button));
        return new Grid { Children = { row, button } };
    }

    private void Update()
    {
        foreach (var (mode, icon, row, tile, radio, button) in _options)
        {
            var selected = mode == _settings.Mode;
            row.Stroke = selected ? Theme.Ember : Theme.Line;
            row.Shadow = selected ? Theme.EmberGlow(24, .25f) : null!;
            tile.BackgroundColor = selected ? Theme.EmberTint : Theme.Line;
            var glyph = Icons.Create(icon, selected ? Theme.Ember : Theme.Text, 20);
            glyph.HorizontalOptions = glyph.VerticalOptions = LayoutOptions.Center;
            tile.Content = glyph;
            radio.Stroke = selected ? Theme.Ember : null;
            radio.StrokeThickness = selected ? 2 : 0;
            radio.BackgroundColor = selected ? Colors.Transparent : Theme.Text;
            radio.Content!.IsVisible = selected;
            SemanticProperties.SetDescription(button, selected ? $"{mode} renderer, selected" : $"{mode} renderer");
        }
        var (_, software, starting) = _settings.Chip;
        _nowBackend.Text = _settings.Backend?.ToString() ?? _settings.Mode switch
        {
            RenderingMode.Software => "Software",
            _ => "Hardware"
        };
        _nowDetail.Text = _settings.Diagnostic ?? "Open a scene to start drawing.";
        _nowDot.BackgroundColor = starting ? Theme.TextMuted : software ? Theme.Ice : Theme.Ember;
        _nowDot.Shadow = software || starting ? null! : Theme.EmberGlow(10, 1);
    }
}

/// <summary>Shown when Auto has fallen back to software: why, and a way back to the GPU.</summary>
internal sealed class FallbackBanner : Border
{
    private readonly RendererSettings _settings;
    private readonly Label _detail = Theme.Label("", Theme.FontMono, 12, Theme.IceTextSoft);
    private string? _dismissed;

    public FallbackBanner(RendererSettings settings)
    {
        _settings = settings;
        Padding = new Thickness(16, 14);
        BackgroundColor = Theme.IcePanel;
        Stroke = Theme.IceBorder;
        StrokeThickness = 1;
        StrokeShape = Theme.Rounded(Theme.RadiusCard);
        var icon = Icons.Create(Icon.Fallback, Theme.Ice, 22);
        icon.VerticalOptions = LayoutOptions.Start;
        var text = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Theme.Label("Drawing in software", Theme.FontLabel, 15, Theme.IceText),
                Theme.Label("The GPU stopped, so Auto switched to the CPU. Everything still works.", Theme.FontBody, 13, Theme.IceTextSoft),
                _detail
            }
        };
        var top = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        top.Add(icon);
        top.Add(text, 1);
        var dismiss = new ActionButton("Dismiss", () => { _dismissed = _settings.Diagnostic; Update(); }, ActionStyle.Text, height: 44, textColor: Theme.IceText);
        var retry = new ActionButton("Try GPU again", settings.Retry, ActionStyle.Primary, height: 44);
        var actions = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { dismiss, retry } };
        Content = new VerticalStackLayout { Spacing = 12, Children = { top, actions } };
        settings.Changed += Update;
        Update();
    }

    private void Update()
    {
        _detail.Text = _settings.Diagnostic;
        IsVisible = _settings.IsFallback && _settings.Diagnostic != _dismissed;
    }
}

using Microsoft.Maui.Controls.Shapes;

namespace Simple3D.Demo;

/// <summary>Custom content under a transparent button: the button takes the taps, reads its text or description to screen
/// readers and dims the content while pressed. Tests click it through <see cref="IButtonController"/>.</summary>
internal class Tappable : Grid
{
    public Button Button { get; }

    public Tappable(View face, string? text, Action action)
    {
        face.InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(face, false);
        Button = new Button
        {
            Text = text, TextColor = Colors.Transparent, BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
            CornerRadius = 0, MinimumHeightRequest = 0, MinimumWidthRequest = 0, FontSize = 1
        };
        Button.Pressed += (_, _) => face.Opacity = .6;
        Button.Released += (_, _) => face.Opacity = 1;
        Button.Clicked += (_, _) => action();
        Add(face);
        Add(Button);
    }
}

/// <summary>An icon-only round button, at least 44 pt, with a required screen-reader description.</summary>
internal sealed class IconButton : Tappable
{
    private readonly Border _face;

    public IconButton(Icon icon, string description, Action action, bool floating = true, double iconSize = 20, Color? color = null, Color? background = null)
        : this(new Border
        {
            StrokeShape = new Ellipse(), StrokeThickness = floating ? 1 : 0, Stroke = Theme.Line,
            BackgroundColor = background ?? (floating ? Theme.Overlay : Colors.Transparent)
        }, description, action)
    {
        WidthRequest = HeightRequest = Theme.Touch;
        SetIcon(icon, color ?? Theme.Text, iconSize);
    }

    private IconButton(Border face, string description, Action action) : base(face, null, action)
    {
        _face = face;
        SemanticProperties.SetDescription(Button, description);
    }

    public void SetIcon(Icon icon, Color color, double size = 20) => _face.Content = Icons.Create(icon, color, size, center: true);
}

internal enum ActionStyle { Primary, Active, Secondary, Text }

/// <summary>A pill button with an optional leading icon. <see cref="Tappable.Button"/> carries the text for screen readers
/// and tests.</summary>
internal sealed class ActionButton : Tappable
{
    private readonly Border _face;
    private readonly Label _label = new() { FontFamily = Theme.FontLabel, VerticalOptions = LayoutOptions.Center };
    private readonly Color? _textColor;
    private const double GlowRoom = 16;

    public ActionButton(string text, Action action, ActionStyle style = ActionStyle.Primary, Icon? icon = null, double height = 52, Color? textColor = null)
        : this(new Border { StrokeShape = Theme.Rounded(height / 2), Padding = new Thickness(20, 0) }, text, action)
    {
        // Room for the glow inside this view: Android does not draw a shadow outside its parent.
        Padding = GlowRoom;
        Margin = -GlowRoom;
        HeightRequest = Math.Max(Theme.Touch, height) + 2 * GlowRoom;
        _label.FontSize = height >= 52 ? 15 : 14;
        _textColor = textColor;
        Update(text, style, icon);
    }

    private ActionButton(Border face, string text, Action action) : base(face, text, action) => _face = face;

    public void Update(string text, ActionStyle style, Icon? icon)
    {
        Button.Text = _label.Text = text;
        var (background, stroke, foreground) = style switch
        {
            ActionStyle.Primary => (Theme.PrimaryGradient, null, Theme.OnEmber),
            ActionStyle.Active => (new SolidColorBrush(Theme.EmberTint), Theme.Ember, Theme.EmberLight),
            ActionStyle.Secondary => (new SolidColorBrush(Theme.Surface), Theme.Line, Theme.Text),
            _ => ((Brush?)null, (Color?)null, _textColor ?? Theme.Text)
        };
        _face.Background = background;
        _face.Stroke = stroke;
        _face.StrokeThickness = stroke is null ? 0 : 1;
        _face.Shadow = style switch
        {
            ActionStyle.Primary => Theme.EmberGlow(),
            ActionStyle.Active => Theme.EmberGlow(20, .3f),
            _ => null!
        };
        _label.TextColor = foreground;
        var content = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        if (icon is { } shown) content.Add(Icons.Create(shown, foreground, 16, center: true));
        content.Add(_label);
        _face.Content = content;
    }
}

/// <summary>Two or three mutually exclusive options in an inset track; the chosen one is ember-tinted, or ember-filled when
/// <c>strong</c>.</summary>
internal sealed class Segmented : Border
{
    private readonly List<Button> _buttons = [];
    private readonly Func<int, bool, string> _describe;
    private readonly bool _strong;
    public int Selected { get; private set; }
    public IReadOnlyList<Button> Buttons => _buttons;
    public event Action<int>? Changed;

    /// <param name="describe">Screen-reader description of option <c>i</c>, selected or not.</param>
    public Segmented(IReadOnlyList<string> options, int selected, Func<int, bool, string> describe, bool strong = false)
    {
        _describe = describe;
        _strong = strong;
        Padding = 3;
        BackgroundColor = Theme.Canvas;
        Stroke = Theme.Line;
        StrokeThickness = 1;
        StrokeShape = Theme.Rounded(Theme.RadiusSmall);
        var grid = new Grid();
        for (var i = 0; i < options.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var index = i;
            var button = new Button
            {
                Text = options[i], FontFamily = Theme.FontLabel, FontSize = 13, CornerRadius = 11, BorderWidth = 0,
                HeightRequest = Theme.Touch, MinimumHeightRequest = Theme.Touch, Padding = new Thickness(8, 0)
            };
            button.Clicked += (_, _) => Select(index, notify: true);
            _buttons.Add(button);
            grid.Add(button, i);
        }
        Content = grid;
        Select(selected, notify: false);
    }

    public void Select(int index, bool notify)
    {
        Selected = index;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var chosen = i == index;
            _buttons[i].Background = !chosen ? new SolidColorBrush(Colors.Transparent) : _strong ? Theme.PrimaryGradient : new SolidColorBrush(Theme.EmberTint);
            _buttons[i].TextColor = !chosen ? Theme.TextSoft : _strong ? Theme.OnEmber : Theme.EmberLight;
            SemanticProperties.SetDescription(_buttons[i], _describe(i, chosen));
        }
        if (notify) Changed?.Invoke(index);
    }
}

/// <summary>Floating panels and pills over 3D content.</summary>
internal static class Panels
{
    public static Border Overlay(View content, double radius, Thickness padding) => new()
    {
        BackgroundColor = Theme.Overlay, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(radius),
        Padding = padding, Content = content
    };

    /// <summary>A panel drawn behind its content rather than around it, so the glow of a button inside is not clipped.</summary>
    public static View Card(View content, double radius = Theme.RadiusPanel, Thickness? padding = null) => new Grid
    {
        Children =
        {
            new Border { BackgroundColor = Theme.OverlayStrong, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(radius) },
            new ContentView { Padding = padding ?? new Thickness(16), Content = content }
        }
    };
}

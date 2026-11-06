using Microsoft.Maui.Controls.Shapes;

namespace Simple3D.Demo;

/// <summary>A transparent button over custom content: it takes the taps, reads its text or description to screen readers,
/// and dims the content while pressed. Tests click it through <see cref="IButtonController"/>.</summary>
internal static class Tap
{
    public static Button Over(View content, string? text, Action action)
    {
        var button = new Button
        {
            Text = text, TextColor = Colors.Transparent, BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
            CornerRadius = 0, MinimumHeightRequest = 0, MinimumWidthRequest = 0, FontSize = 1
        };
        button.Pressed += (_, _) => content.Opacity = .6;
        button.Released += (_, _) => content.Opacity = 1;
        button.Clicked += (_, _) => action();
        return button;
    }
}

/// <summary>An icon-only round button, at least 44 pt, with a required screen-reader description.</summary>
internal sealed class IconButton : Grid
{
    private readonly Grid _face;
    public Button Button { get; }

    public IconButton(Icon icon, string description, Action action, bool floating = true, double size = Theme.Touch,
        double iconSize = 20, Color? color = null, Color? background = null)
    {
        WidthRequest = HeightRequest = size;
        _face = new Grid();
        var face = new Border
        {
            StrokeShape = new Ellipse(), StrokeThickness = floating ? 1 : 0, Stroke = Theme.Line,
            BackgroundColor = background ?? (floating ? Theme.Overlay : Colors.Transparent), InputTransparent = true
        };
        AutomationProperties.SetIsInAccessibleTree(face, false);
        _face.Add(face);
        SetIcon(icon, color ?? Theme.Text, iconSize);
        Button = Tap.Over(_face, null, action);
        SemanticProperties.SetDescription(Button, description);
        Add(_face);
        Add(Button);
    }

    public void SetIcon(Icon icon, Color color, double size = 20)
    {
        while (_face.Count > 1) _face.RemoveAt(1);
        var view = Icons.Create(icon, color, size);
        view.HorizontalOptions = view.VerticalOptions = LayoutOptions.Center;
        _face.Add(view);
    }
}

internal enum ActionStyle { Primary, Active, Secondary, Text }

/// <summary>A pill button with an optional leading icon. <see cref="Button"/> carries the text for screen readers and tests.</summary>
internal sealed class ActionButton : Grid
{
    private readonly Border _face;
    private readonly HorizontalStackLayout _content = new() { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
    private readonly Label _label = new() { FontFamily = Theme.FontLabel, VerticalOptions = LayoutOptions.Center };
    private Icon? _icon;
    private ActionStyle _style;
    private readonly Color? _textColor;
    public Button Button { get; }

    public ActionButton(string text, Action action, ActionStyle style = ActionStyle.Primary, Icon? icon = null, double height = 52, Color? textColor = null)
    {
        HeightRequest = Math.Max(Theme.Touch, height);
        _face = new Border { StrokeShape = Theme.Rounded(height / 2), InputTransparent = true, Padding = new Thickness(20, 0), Content = _content };
        AutomationProperties.SetIsInAccessibleTree(_face, false);
        _label.FontSize = height >= 52 ? 15 : 14;
        _textColor = textColor;
        Button = Tap.Over(_face, text, action);
        Add(_face);
        Add(Button);
        Update(text, style, icon);
    }

    public string Text => Button.Text;

    public void Update(string text, ActionStyle style, Icon? icon)
    {
        Button.Text = _label.Text = text;
        _style = style;
        _icon = icon;
        var (background, stroke, foreground) = style switch
        {
            ActionStyle.Primary => ((Brush?)Theme.PrimaryGradient, (Color?)null, Theme.OnEmber),
            ActionStyle.Active => (new SolidColorBrush(Theme.EmberTint), Theme.Ember, Theme.EmberLight),
            ActionStyle.Secondary => (new SolidColorBrush(Theme.Surface), Theme.Line, Theme.Text),
            _ => (null, null, _textColor ?? Theme.Text)
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
        _content.Clear();
        if (icon is { } shown) _content.Add(Icons.Create(shown, foreground, 16, 2));
        _content.Add(_label);
    }

    public void SetText(string text) => Update(text, _style, _icon);
}

/// <summary>Two or three mutually exclusive options in an inset track; the chosen one is ember-tinted.</summary>
internal sealed class Segmented : Border
{
    private readonly List<Button> _buttons = [];
    private readonly Func<int, bool, string> _describe;
    public int Selected { get; private set; }
    public IReadOnlyList<Button> Buttons => _buttons;
    public event Action<int>? Changed;

    /// <param name="describe">Screen-reader description of option <c>i</c>, selected or not.</param>
    public Segmented(IReadOnlyList<string> options, int selected, Func<int, bool, string> describe, bool strong = false)
    {
        _describe = describe;
        Padding = 3;
        BackgroundColor = Theme.Canvas;
        Stroke = Theme.Line;
        StrokeThickness = 1;
        StrokeShape = Theme.Rounded(Theme.RadiusSmall);
        var grid = new Grid { ColumnSpacing = 0 };
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
        _strong = strong;
        Content = grid;
        Select(selected, notify: false);
    }

    private readonly bool _strong;

    public void Select(int index, bool notify)
    {
        Selected = index;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var chosen = i == index;
            var button = _buttons[i];
            button.Background = chosen ? (_strong ? Theme.PrimaryGradient : new SolidColorBrush(Theme.EmberTint)) : new SolidColorBrush(Colors.Transparent);
            button.TextColor = chosen ? (_strong ? Theme.OnEmber : Theme.EmberLight) : Theme.TextSoft;
            SemanticProperties.SetDescription(button, _describe(i, chosen));
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

    public static Border Card(View content, double radius = Theme.RadiusPanel, double padding = 16) => new()
    {
        BackgroundColor = Theme.OverlayStrong, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(radius),
        Padding = padding, Content = content
    };
}

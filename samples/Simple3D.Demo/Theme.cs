using Microsoft.Maui.Controls.Shapes;

namespace Simple3D.Demo;

/// <summary>The gallery's design tokens (docs/design/2026-11-redesign): warm, dark surfaces, ember for the GPU and the main
/// action, ice only for the software fallback. Every color, font, radius and spacing in the app comes from here.</summary>
internal static class Theme
{
    // Colors.
    public static readonly Color Canvas = Color.FromArgb("#0A0908");
    public static readonly Color Surface = Color.FromArgb("#151210");
    public static readonly Color Raised = Color.FromArgb("#1E1A17");
    public static readonly Color Line = Color.FromArgb("#2E2723");
    public static readonly Color LineStrong = Color.FromArgb("#6A5D54");
    public static readonly Color Text = Color.FromArgb("#F7F1EC");
    public static readonly Color TextSoft = Color.FromArgb("#D6CBC2");
    public static readonly Color TextMuted = Color.FromArgb("#A89C93");
    public static readonly Color Ember = Color.FromArgb("#FF8A3D");
    public static readonly Color EmberLight = Color.FromArgb("#FFC08F");
    public static readonly Color EmberTint = Color.FromArgb("#3A1C0A");
    public static readonly Color OnEmber = Color.FromArgb("#2B1203");
    public static readonly Color Ice = Color.FromArgb("#7CB7FF");
    public static readonly Color IcePanel = Color.FromArgb("#0E1A2A");
    public static readonly Color IceBorder = Color.FromArgb("#24405E");
    public static readonly Color IceText = Color.FromArgb("#D6E8FF");
    public static readonly Color IceTextSoft = Color.FromArgb("#B9D2EE");
    public static readonly Color Overlay = Color.FromRgba(21, 18, 16, 217);
    public static readonly Color OverlayStrong = Color.FromRgba(21, 18, 16, 235);
    public static readonly Color Scrim = Color.FromRgba(5, 4, 3, 184);
    public static readonly Color Press = Color.FromRgba(255, 255, 255, 20);
    /// <summary>Behind 3D content: the scene view's own background, so the view blends into the glow around it.</summary>
    public static readonly Color SceneBackground = Color.FromArgb("#140C07");
    public static readonly Color GlowCenter = Color.FromArgb("#3A1A08");
    public static readonly Color HeaderGlow = Color.FromArgb("#2E1609");

    // App icon cube faces.
    public static readonly Color CubeTop = EmberLight;
    public static readonly Color CubeLeft = Ember;
    public static readonly Color CubeRight = Color.FromArgb("#C2541A");

    // Fonts, registered in MauiProgram.
    public const string FontDisplay = "SpaceGroteskSemiBold";
    public const string FontBody = "InterRegular";
    public const string FontLabel = "InterSemiBold";
    public const string FontMono = "JetBrainsMonoRegular";
    public const string FontMonoMedium = "JetBrainsMonoMedium";

    // Radii and spacing (4 pt grid).
    public const double RadiusSmall = 14, RadiusCard = 20, RadiusPanel = 24, RadiusSheet = 28;
    public const double Margin = 16, Gap = 10, GroupGap = 20;
    /// <summary>The smallest tappable size.</summary>
    public const double Touch = 44;
    /// <summary>Above this window width the gallery shows a sidebar next to the viewer.</summary>
    public const double WideBreakpoint = 840;

    public static Brush PrimaryGradient => new LinearGradientBrush(
        [new GradientStop(Color.FromArgb("#FFA45E"), 0), new GradientStop(Color.FromArgb("#FF7A2A"), 1)], new Point(0, 0), new Point(0, 1));

    /// <summary>The radial glow behind 3D content and at the top of pages.</summary>
    public static Brush Glow(Color edge, double centerY = .45, double radius = .7) =>
        new RadialGradientBrush([new GradientStop(GlowCenter, 0), new GradientStop(edge, 1)], new Point(.5, centerY), radius);

    public static Shadow EmberGlow(double radius = 24, float opacity = .45f) =>
        new() { Brush = Ember, Radius = (float)radius, Opacity = opacity, Offset = new Point(0, 0) };

    public static RoundRectangle Rounded(double radius) => new() { CornerRadius = radius };

    public static Label Label(string text, string font, double size, Color color) => new()
    {
        Text = text, FontFamily = font, FontSize = size, TextColor = color, LineBreakMode = LineBreakMode.WordWrap
    };
}

using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Simple3D.Demo;

internal enum Icon
{
    Back, Close, ZoomIn, ZoomOut, Fit, Reset, Animate, Pause, FrameStats, Auto, Hardware, Software, Camera, CameraOff,
    SwitchCamera, Body, WaistUp, Face, Open, Private, Fallback, Settings, Hand, Pointer, Collapse, FindPerson
}

/// <summary>Line icons on a 24 pt grid with a 2 pt round stroke (docs/design/2026-11-redesign/source/System.dc.html).</summary>
internal static class Icons
{
    // Path data copied from the design sources; rectangles and circles converted to paths.
    private static readonly Dictionary<Icon, string> Strokes = new()
    {
        [Icon.Back] = "m15 18-6-6 6-6",
        [Icon.Close] = "M6 6l12 12M18 6 6 18",
        [Icon.ZoomIn] = "M12 5v14M5 12h14",
        [Icon.ZoomOut] = "M5 12h14",
        [Icon.Fit] = "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5",
        [Icon.Reset] = "M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5",
        [Icon.FrameStats] = "M3 12h4l3-8 4 16 3-8h4",
        [Icon.Auto] = "M13 2 4 14h7l-1 8 9-12h-7z",
        [Icon.Hardware] = Rect(5, 5, 14, 14, 3) + Rect(9, 9, 6, 6, 0) + "M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3",
        [Icon.Software] = Rect(3, 4, 18, 12, 2) + "M8 20h8M12 16v4",
        [Icon.Camera] = "M4 8h3l2-3h6l2 3h3v11H4z" + Circle(12, 13, 3.5),
        [Icon.CameraOff] = "M4 8h3l2-3h6l2 3h3v11H4z" + Circle(12, 13, 3.5) + "M3 3l18 18",
        [Icon.SwitchCamera] = "M20 7h-9a5 5 0 0 0 0 10h1M17 4l3 3-3 3M4 17h9a5 5 0 0 0 0-10h-1M7 20l-3-3 3-3",
        [Icon.Body] = Circle(12, 4, 2) + "M8 9h8M12 9v6M12 15l-3 7M12 15l3 7M8 9l-2 5M16 9l2 5",
        [Icon.WaistUp] = Circle(12, 6, 3) + "M5 21v-5a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v5",
        [Icon.Face] = Circle(12, 12, 9) + "M9 10h.01M15 10h.01M8.5 14.5a4.5 4.5 0 0 0 7 0",
        [Icon.Open] = "M5 12h14M13 6l6 6-6 6",
        [Icon.Private] = Rect(5, 11, 14, 10, 2) + "M8 11V7a4 4 0 0 1 8 0v4",
        [Icon.Fallback] = Circle(12, 12, 9) + "M12 8v5M12 16h.01",
        [Icon.Settings] = Circle(12, 12, 3) + "M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 " +
            "1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1" +
            "a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1" +
            "a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 " +
            "1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z",
        [Icon.Hand] = "M9 11V5a2 2 0 0 1 4 0v6M13 10a2 2 0 0 1 4 0v3a6 6 0 0 1-6 6h-1a5 5 0 0 1-4-2l-2.5-3.5a1.6 1.6 0 0 1 2.4-2L9 14",
        [Icon.Pointer] = "M3 3l7 18 2.5-7.5L20 11z",
        [Icon.Collapse] = "m6 15 6-6 6 6",
        [Icon.FindPerson] = "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" + Circle(12, 9, 2) + "M9 17v-2a3 3 0 0 1 6 0v2",
    };

    private static readonly Dictionary<Icon, string> Fills = new()
    {
        [Icon.Animate] = "M7 5v14l12-7z",
        [Icon.Pause] = Rect(6, 5, 4, 14, 1) + Rect(14, 5, 4, 14, 1),
    };

    private static readonly Dictionary<string, Geometry> Parsed = [];

    /// <summary>A <paramref name="size"/> pt square showing the icon in <paramref name="color"/>.</summary>
    public static View Create(Icon icon, Color color, double size = 20, double stroke = 2, bool center = false)
    {
        var filled = Fills.TryGetValue(icon, out var data);
        var path = Shape(data ?? Strokes[icon], 24, size);
        if (filled) path.Fill = color;
        else
        {
            path.Stroke = color;
            path.StrokeThickness = stroke * size / 24;
            path.StrokeLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        var box = new Grid { WidthRequest = size, HeightRequest = size, InputTransparent = true, Children = { path } };
        if (center) box.HorizontalOptions = box.VerticalOptions = LayoutOptions.Center;
        AutomationProperties.SetIsInAccessibleTree(box, false);
        return box;
    }

    /// <summary>Path data drawn in a <paramref name="viewBox"/> square, scaled to <paramref name="size"/> pt.</summary>
    public static Path Shape(string data, double viewBox, double size) => new()
    {
        Data = Geometry(data, (float)(size / viewBox)), Aspect = Stretch.None, WidthRequest = size, HeightRequest = size, InputTransparent = true
    };

    // MAUI draws path data at its own coordinates, so scale the data itself (arcs become curves on the way).
    private static Geometry Geometry(string data, float scale)
    {
        var key = FormattableString.Invariant($"{scale}|{data}");
        if (Parsed.TryGetValue(key, out var geometry)) return geometry;
        using var path = SKPath.ParseSvgPathData(data) ?? throw new FormatException($"Invalid icon path: {data}");
        path.Transform(SKMatrix.CreateScale(scale, scale));
        return Parsed[key] = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(path.ToSvgPathData())!;
    }

    private static string Rect(double x, double y, double w, double h, double r) => r == 0
        ? FormattableString.Invariant($"M{x} {y} H{x + w} V{y + h} H{x} Z ")
        : FormattableString.Invariant($"M{x + r} {y} H{x + w - r} A{r} {r} 0 0 1 {x + w} {y + r} V{y + h - r} A{r} {r} 0 0 1 {x + w - r} {y + h} H{x + r} A{r} {r} 0 0 1 {x} {y + h - r} V{y + r} A{r} {r} 0 0 1 {x + r} {y} Z ");

    private static string Circle(double cx, double cy, double r) =>
        FormattableString.Invariant($" M{cx - r} {cy} A{r} {r} 0 1 0 {cx + r} {cy} A{r} {r} 0 1 0 {cx - r} {cy} Z ");
}

internal static class ShapeExtensions
{
    public static Path WithFill(this Path path, Color fill)
    {
        path.Fill = fill;
        return path;
    }
}

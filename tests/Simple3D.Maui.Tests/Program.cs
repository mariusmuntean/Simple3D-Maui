using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Simple3D.Core;
using Simple3D.Maui;

var tests = new (string Name, Action Run)[]
{
    ("pinch applies every incremental scale update", () => {
        var actual = CreateView();
        var expected = CreateView();
        var pinch = (IPinchGestureController)actual.GestureRecognizers.OfType<PinchGestureRecognizer>().Single();
        pinch.SendPinchStarted(actual, new Point(.5, .5));
        foreach (var factor in new[] { 1.1, 1.1, 1.1 }) {
            pinch.SendPinch(actual, factor, new Point(.5, .5));
            expected.Zoom((float)factor);
        }
        pinch.SendPinchEnded(actual);
        AssertSameDrawing(actual, expected);
    }),
    ("invalid pinch update does not poison the next update", () => {
        var actual = CreateView();
        var expected = CreateView();
        var pinch = (IPinchGestureController)actual.GestureRecognizers.OfType<PinchGestureRecognizer>().Single();
        pinch.SendPinchStarted(actual, new Point(.5, .5));
        pinch.SendPinch(actual, double.PositiveInfinity, new Point(.5, .5));
        pinch.SendPinch(actual, 1.2, new Point(.5, .5));
        expected.Zoom(1.2f);
        AssertSameDrawing(actual, expected);
    }),
    ("reset camera restores the original drawing", () => {
        var actual = CreateView();
        actual.Orbit(.5f, -.2f);
        actual.Zoom(2);
        actual.ResetCamera();
        AssertSameDrawing(actual, CreateView());
    }),
    ("draw releases native path resources", () => {
        var canvas = Record(CreateView());
        Assert(canvas.Resources.Count > 0, "No paths drawn");
        Assert(canvas.Resources.All(resource => resource.Disposed), "Path resources were not disposed");
    })
};
var failed = 0;
foreach (var (name, run) in tests) {
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static SceneView CreateView() => new() { Scene = new Scene().Add(Shape.Box()) };
static RecordingCanvas Record(SceneView view)
{
    var canvas = DispatchProxy.Create<ICanvas, RecordingCanvas>();
    view.Drawable.Draw(canvas, new RectF(0, 0, 300, 300));
    return (RecordingCanvas)canvas;
}
static void AssertSameDrawing(SceneView actual, SceneView expected)
{
    var a = Record(actual).Points;
    var b = Record(expected).Points;
    Assert(a.Count > 0 && a.Count == b.Count, "Different or empty drawings");
    for (var i = 0; i < a.Count; i++)
        Assert(MathF.Abs(a[i].X - b[i].X) < .0001f && MathF.Abs(a[i].Y - b[i].Y) < .0001f,
            $"Drawing differs at vertex {i}: {a[i]} vs {b[i]}");
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

public class RecordingCanvas : DispatchProxy
{
    public List<PointF> Points { get; } = [];
    public List<PathResource> Resources { get; } = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(ICanvas.FillPath)) {
            var path = (PathF)args![0]!;
            Points.AddRange(path.Points);
            var resource = new PathResource();
            path.PlatformPath = resource;
            Resources.Add(resource);
        }
        return method?.ReturnType == typeof(void) ? null :
            method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
public sealed class PathResource : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

using System.Numerics;
using Simple3D.Core;

var tests = new (string Name, Action Run)[]
{
    ("built-in meshes have triangles", () => {
        foreach (var shape in new[] { Shape.Box(), Shape.Sphere(), Shape.Cylinder(), Shape.Pyramid() })
            Assert(shape.TriangleCount > 0, "empty mesh");
        Assert(Shape.Box().TriangleCount == 12, "box should have 12 faces");
    }),
    ("empty or invalid viewport renders nothing", () => {
        var scene = new Scene().Add(Shape.Box());
        Assert(SceneRenderer.Render(scene, new Camera(), 0, 100).Count == 0, "zero width");
        Assert(SceneRenderer.Render(scene, new Camera(), 100, float.NaN).Count == 0, "NaN height");
        Assert(SceneRenderer.Render(new Scene(), new Camera(), 100, 100).Count == 0, "empty scene");
    }),
    ("visible box projects into viewport", () => {
        var faces = SceneRenderer.Render(new Scene().Add(Shape.Box()), new Camera(), 300, 300);
        Assert(faces.Count >= 2 && faces.Count <= 6, $"box face count {faces.Count}");
        Assert(faces.All(f => float.IsFinite(f.A.X) && f.A.X is >= 0 and <= 300), "invalid projection");
    }),
    ("far geometry renders first", () => {
        var scene = new Scene().Add(Shape.Box(0xFFFF0000).At(0, 0, -2)).Add(Shape.Box(0xFF0000FF).At(0, 0, 2));
        var faces = SceneRenderer.Render(scene, new Camera(distance: 10), 300, 300);
        Assert(faces.Count > 0, "faces missing");
        for (var i = 1; i < faces.Count; i++) Assert(faces[i - 1].Depth >= faces[i].Depth, "depth order");
    }),
    ("camera clamps and rejects invalid zoom", () => {
        var camera = new Camera();
        camera.Orbit(0, 100); Assert(camera.Pitch < MathF.PI / 2, "pitch limit");
        camera.Zoom(0); Assert(float.IsFinite(camera.Distance) && camera.Distance > 0, "invalid zoom");
        camera.Zoom(float.NaN); Assert(float.IsFinite(camera.Distance), "NaN zoom");
    }),
    ("camera-facing exclusion", () => {
        var scene = new Scene().Add(Shape.Box().At(0, 0, 10));
        Assert(SceneRenderer.Render(scene, new Camera(distance: 5, yaw: 0, pitch: 0), 200, 200).Count == 0, "behind camera");
    }),
    ("transform and light are finite", () => {
        var shape = Shape.Pyramid().At(1, 2, 0).Rotated(0, 0.5f, 0).Scaled(2);
        var faces = SceneRenderer.Render(new Scene().Add(shape), new Camera(), 400, 300);
        Assert(faces.Count > 0 && faces.All(f => float.IsFinite(f.B.Y) && f.Color != 0), "transform");
    })
};
var failed = 0;
foreach (var (name, run) in tests) {
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

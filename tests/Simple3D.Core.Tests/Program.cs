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
    }),
    ("immutable transformations retain the shared unit mesh", () => {
        var original = Shape.Sphere();
        var translated = original.At(2, 0, 0);
        Assert(original.Position == Vector3.Zero && translated.Position.X == 2, "mutable shape");
        Assert(original.TriangleCount == translated.TriangleCount, "mesh changed");
    }),
    ("invalid dimensions are rejected", () => {
        try { Shape.Box().Scaled(-1); throw new Exception("accepted negative size"); }
        catch (ArgumentOutOfRangeException) { }
    }),
    ("viewport reprojects after resize", () => {
        var scene = new Scene().Add(Shape.Sphere());
        var small = SceneRenderer.Render(scene, new Camera(), 200, 200);
        var large = SceneRenderer.Render(scene, new Camera(), 400, 400);
        Assert(small.Count == large.Count && large[0].A.X > small[0].A.X, "resize projection");
    }),
    ("near plane clips one vertex into two visible triangles", () => {
        var shape = new Shape([new Triangle3(new(-.1f, -.1f, 4.9f), new(.1f, -.1f, 4.9f),
            new(0, .1f, 5))], 0xFF7799CC, Vector3.Zero, Vector3.Zero, Vector3.One);
        var faces = SceneRenderer.Render(new Scene().Add(shape), new Camera(5, 0, 0), 300, 300);
        Assert(faces.Count == 2, $"clipped face count {faces.Count}");
        Assert(faces.All(Finite), "invalid clipped projection");
    }),
    ("near plane clips two vertices into one visible triangle", () => {
        var shape = new Shape([new Triangle3(new(-.1f, -.1f, 4.9f), new(.1f, -.1f, 5),
            new(0, .1f, 5))], 0xFF7799CC, Vector3.Zero, Vector3.Zero, Vector3.One);
        var faces = SceneRenderer.Render(new Scene().Add(shape), new Camera(5, 0, 0), 300, 300);
        Assert(faces.Count == 1, $"clipped face count {faces.Count}");
        Assert(faces.All(Finite), "invalid clipped projection");
    }),
    ("unit meshes have outward, nondegenerate triangles", () => {
        foreach (var shape in new[] { Shape.Box(), Shape.Sphere(), Shape.Cylinder(), Shape.Pyramid() })
            foreach (var face in shape.Mesh) {
                var normal = Vector3.Cross(face.B - face.A, face.C - face.A);
                Assert(normal.LengthSquared() > 1e-10f, "degenerate mesh triangle");
                Assert(Vector3.Dot(normal, (face.A + face.B + face.C) / 3) > 0, "inward mesh triangle");
            }
    }),
    ("seeded camera and shape scenes render finite, deterministic faces", () => {
        var random = new Random(71239);
        for (var trial = 0; trial < 120; trial++) {
            var scene = new Scene();
            for (var i = 0; i < 5; i++)
                scene.Add((i % 2 == 0 ? Shape.Box() : Shape.Sphere())
                    .At((float)(random.NextDouble() * 6 - 3), (float)(random.NextDouble() * 6 - 3),
                        (float)(random.NextDouble() * 6 - 3))
                    .Rotated((float)random.NextDouble(), (float)random.NextDouble(), 0)
                    .Scaled((float)(random.NextDouble() * 1.8 + .2)));
            var camera = new Camera((float)(random.NextDouble() * 7 + 1.2),
                (float)(random.NextDouble() * 6 - 3), (float)(random.NextDouble() * 2 - 1));
            var first = SceneRenderer.Render(scene, camera, 360, 240);
            var second = SceneRenderer.Render(scene, camera, 360, 240);
            Assert(first.SequenceEqual(second), $"nondeterministic scene {trial}");
            Assert(first.All(Finite), $"nonfinite scene {trial}");
            for (var i = 1; i < first.Count; i++)
                Assert(first[i - 1].Depth >= first[i].Depth, $"unsorted scene {trial}");
        }
    })
};
var failed = 0;
foreach (var (name, run) in tests) {
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
if (failed == 0)
{
    var scene = new Scene();
    for (var i = 0; i < 12; i++) scene.Add(Shape.Sphere().At((i % 4 - 1.5f) * .8f, (i / 4 - 1) * .8f, 0));
    for (var i = 0; i < 30; i++) SceneRenderer.Render(scene, new Camera(), 400, 400);
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 300; i++) SceneRenderer.Render(scene, new Camera(), 400, 400);
    Console.WriteLine($"Render baseline (12 spheres, {300} frames): {timer.Elapsed.TotalMilliseconds / 300:F2} ms/frame on this runner");
}
return failed == 0 ? 0 : 1;
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static bool Finite(DrawTriangle face) => float.IsFinite(face.A.X) && float.IsFinite(face.A.Y) &&
    float.IsFinite(face.B.X) && float.IsFinite(face.B.Y) && float.IsFinite(face.C.X) &&
    float.IsFinite(face.C.Y) && float.IsFinite(face.Depth);

using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared;

if (args.Length > 0 && args[0] == "--render-images")
    return RenderImages.Run(args[1..]);

var tests = new (string Name, Action Run)[]
{
    ("indexed meshes validate and defensively copy", () => {
        var vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(0,1,0) };
        var mesh = new Mesh(vertices, new[] {0,1,2}); vertices[0] = Vector3.Zero;
        Assert(mesh.Vertices[0].X == -1, "vertices changed");
        try { _ = new Mesh(vertices, new[] {0,1,9}); throw new Exception("bad index accepted"); } catch (ArgumentException) { }
        try { _ = new Mesh(vertices, new[] {0,0,1}); throw new Exception("degenerate accepted"); } catch (ArgumentException) { }
    }),
    ("scene events and immutable groups", () => {
        var child = Shape.Box().Named("child"); var group = Shape.Group(child).At(4,0,0);
        var scene = new Scene(); var changes = 0; scene.Changed += (_,_) => changes++;
        scene.Add(group); Assert(scene.GetBounds()!.Value.Min.X == 3.5f, "group transform");
        Assert(scene.Replace(group, child), "replace"); Assert(scene.Remove(child), "remove");
        scene.Clear(); Assert(changes == 3, "event count");
    }),
    ("replacement notifications identify old and new nodes once", () => {
        var previous = Shape.Box(); var next = previous.At(1, 0, 0);
        var scene = new Scene().Add(previous); var count = 0;
        scene.Changed += (_, args) => {
            var replacement = args as ShapeReplacementEventArgs;
            Assert(replacement is not null && ReferenceEquals(replacement.OldShape, previous) &&
                ReferenceEquals(replacement.NewShape, next), "replacement notification lost node identities");
            Assert(ReferenceEquals(scene.Shapes[0], next), "notification happened before replacement");
            count++;
        };
        Assert(scene.Replace(previous, next), "replacement failed");
        Assert(!scene.Replace(previous, next) && !scene.Replace(next, next), "no-op replacement succeeded");
        Assert(count == 1, "replacement notified more than once");
    }),
    ("depth visibility picking and retained frames", () => {
        var near = Shape.Box(0xFFFF0000).Named("near").At(0,0,1);
        var far = Shape.Box(0xFF0000FF).Named("far");
        var renderer = new DepthRenderer(); var scene = new Scene().Add(near).Add(far);
        var frame = renderer.Render(scene, new Camera(5,0,0), 100,100);
        Assert(ReferenceEquals(frame.Pick(50,50), near), "occluded pick");
        renderer.Render(new Scene(),new Camera(),10,10);
        Assert(ReferenceEquals(frame.Pick(50,50), near), "frame was overwritten");
    }),
    ("camera fit and projected world labels", () => {
        var scene = new Scene().Add(Shape.Box().At(20,3,0)).AddLabel(new WorldLabel("A",new(20,3,0)));
        var camera = new Camera(5,0,0); camera.FitToScene(scene,2);
        Assert(camera.Target == new Vector3(20,3,0), "fit target");
        var frame = new DepthRenderer().Render(scene,camera,200,100);
        Assert(frame.Labels.Count == 1 && MathF.Abs(frame.Labels[0].Position.X-100)<.01f,"label projection");
        camera.Projection = CameraProjection.Orthographic;
        Assert(new DepthRenderer().Render(scene,camera,200,100).Pick(100,50) != null,"orthographic fit");
    }),
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
        Assert(ReferenceEquals(original.Mesh, translated.Mesh), "mesh copied");
    }),
    ("fluent transforms replace values and preserve unrelated attributes", () => {
        var original = Shape.Box(0xFF123456).At(1, 2, 3).Rotated(.1f, .2f, .3f).Scaled(2);
        var changed = original.At(4, 5, 6).Scaled(3).Scaled(2, 3, 4);
        Assert(original.Position == new Vector3(1, 2, 3) && original.Size == new Vector3(2), "original changed");
        Assert(changed.Position == new Vector3(4, 5, 6) && changed.Size == new Vector3(2, 3, 4), "transforms accumulated");
        Assert(changed.Rotation == original.Rotation && changed.Color == original.Color, "unrelated attributes changed");
        Assert(ReferenceEquals(original.Mesh, changed.Mesh), "mesh copied");
    }),
    ("invalid dimensions are rejected", () => {
        try { Shape.Box().Scaled(-1); throw new Exception("accepted negative size"); }
        catch (ArgumentOutOfRangeException) { }
    }),
    ("invalid transform reports the offending argument", () => {
        var shape = Shape.Box();
        AssertOutOfRange(() => shape.At(float.NaN, 0, 0), "x");
        AssertOutOfRange(() => shape.At(0, 0, float.PositiveInfinity), "z");
        AssertOutOfRange(() => shape.Rotated(0, float.NaN, 0), "y");
        AssertOutOfRange(() => shape.Scaled(0), "size");
        AssertOutOfRange(() => shape.Scaled(1, -1, 1), "y");
        AssertOutOfRange(() => shape.Scaled(1, 1, float.NaN), "z");
    }),
    ("transparent colors are rejected by opaque shape factories", () => {
        AssertOutOfRange(() => Shape.Box(0x00FFFFFF), "color");
        AssertOutOfRange(() => Shape.Sphere(0x80FFFFFF), "color");
        AssertOutOfRange(() => Shape.Cylinder(0x80FFFFFF), "color");
        AssertOutOfRange(() => Shape.Pyramid(0x80FFFFFF), "color");
    }),
    ("scene shape collection cannot bypass Add validation", () => {
        var scene = new Scene().Add(Shape.Box());
        Assert(scene.Shapes is not List<Shape>, "mutable list exposed");
        Assert(scene.Shapes is IList<Shape>, "collection does not support read-only list access");
        try { ((IList<Shape>)scene.Shapes).Add(null!); throw new Exception("accepted mutation"); }
        catch (NotSupportedException) { }
        Assert(scene.Shapes.Count == 1, "scene changed through read-only view");
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
    ("near plane discards subpixel slivers instead of drawing degenerate paths", () => {
        var triangle = new Triangle3(new(-.0001f, -.0001f, 4.95f),
            new(0, .0001f, 5.2f), new(.0001f, -.0001f, 4.95f));
        var shape = new Shape([triangle], 0xFF7799CC, Vector3.Zero, Vector3.Zero, Vector3.One);
        var faces = SceneRenderer.Render(new Scene().Add(shape), new Camera(5, 0, 0), 300, 300);
        Assert(faces.Count == 0, $"clipped sliver emitted {faces.Count} paths");
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
tests = tests.Concat(CoreRegressionTests.Cases).ToArray();
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
    Console.WriteLine($"Legacy baseline (12 spheres, 400x400, {300} frames): {timer.Elapsed.TotalMilliseconds / 300:F2} ms/frame on this runner");
    var depthRenderer = new DepthRenderer();
    var camera = new Camera();
    for (var i = 0; i < 20; i++) depthRenderer.Render(scene, camera, 400, 300);
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    for (var i = 0; i < 100; i++) depthRenderer.Render(scene, camera, 400, 300);
    Console.WriteLine($"Depth renderer (12 spheres, {scene.Shapes.Sum(shape => shape.TriangleCount)} triangles, 400x300, 100 frames): {timer.Elapsed.TotalMilliseconds / 100:F2} ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 100:N0} bytes/frame on this runner");
    var animated = DemoScenes.Surface();
    for (var i = 0; i < 10; i++) { animated.Animate(i / 60f); depthRenderer.Render(animated.Scene, animated.Camera, 768, 576); }
    allocated = GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    for (var i = 0; i < 60; i++) { animated.Animate(i / 60f); depthRenderer.Render(animated.Scene, animated.Camera, 768, 576); }
    Console.WriteLine($"Depth renderer (animated mesh scene, 768x576, 60 frames): {timer.Elapsed.TotalMilliseconds / 60:F2} ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 60:N0} bytes/frame on this runner");
    var target = new RenderTarget(768, 576);
    for (var i = 0; i < 10; i++) { animated.Animate(i / 60f); depthRenderer.RenderInto(animated.Scene, animated.Camera, target); }
    allocated = GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    for (var i = 0; i < 60; i++) { animated.Animate(i / 60f); depthRenderer.RenderInto(animated.Scene, animated.Camera, target); }
    Console.WriteLine($"Reusable target (same animated mesh scene, 768x576, 60 frames): {timer.Elapsed.TotalMilliseconds / 60:F2} ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 60:N0} bytes/frame on this runner");
    foreach (var name in new[] { "Robot Arm", "Orbit", "Wind", "Conveyor Inspection", "Solar Tracker", "Packet Routing", "Drone Survey", "Battery Storage", "Gantry Crane" })
    {
        var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == name);
        var sceneTarget = new RenderTarget(768, 576);
        for (var i = 0; i < 10; i++) { sample.Animate(i / 60f); depthRenderer.RenderInto(sample.Scene, sample.Camera, sceneTarget); }
        allocated = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (var i = 0; i < 60; i++) { sample.Animate(i / 60f); depthRenderer.RenderInto(sample.Scene, sample.Camera, sceneTarget); }
        Console.WriteLine($"{name} animated scene (768x576, 60 frames): {timer.Elapsed.TotalMilliseconds / 60:F2} ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 60:N0} bytes/frame on this runner");
    }
}
return failed == 0 ? 0 : 1;
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static bool Finite(DrawTriangle face) => float.IsFinite(face.A.X) && float.IsFinite(face.A.Y) &&
    float.IsFinite(face.B.X) && float.IsFinite(face.B.Y) && float.IsFinite(face.C.X) &&
    float.IsFinite(face.C.Y) && float.IsFinite(face.Depth);

static void AssertOutOfRange(Action action, string parameter)
{
    try { action(); }
    catch (ArgumentOutOfRangeException error) {
        Assert(error.ParamName == parameter, $"Expected {parameter}, got {error.ParamName}");
        return;
    }
    throw new Exception($"Expected {parameter} to be rejected");
}

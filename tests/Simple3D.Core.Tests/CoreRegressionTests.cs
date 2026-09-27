using System.Numerics;
using Simple3D.Core;

internal static class CoreRegressionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("camera notifications describe effective atomic changes", CameraNotifications),
        ("malformed meshes and zero length geometry reject early", InvalidGeometry),
        ("indexed instances share geometry without expanded storage", IndexedStorage),
        ("hierarchical transforms materials ties and label semantics", SceneSemantics),
        ("intersecting triangles select nearest visible surface", Intersections),
        ("seeded depth and picking agree with independent ray oracle", RayOracle),
        ("raster clipping extremes resizing and empty frames remain bounded", ExtremeFrames),
        ("group depth and node budget are bounded", HierarchyLimits),
        ("bounds and fit reject excessive repeated mesh triangles", BoundsBudget),
        ("raster sample work is bounded", RasterWorkBudget),
        ("legacy renderer rejects unsupported scene features", LegacyFeatureGuard)
    ];

    private static void CameraNotifications()
    {
        var camera = new Camera(5, 0, 0);
        var changes = 0;
        camera.Changed += (_, _) => changes++;
        camera.Orbit(0, 0);
        camera.Zoom(1);
        camera.Zoom(float.NaN);
        camera.Target = Vector3.Zero;
        Check(changes == 0, "no-op notified");
        camera.Orbit(.1f, .2f);
        camera.Zoom(2);
        camera.Target = Vector3.One;
        camera.Projection = CameraProjection.Orthographic;
        camera.FieldOfView = 1;
        camera.OrthographicHeight = 3;
        camera.NearPlane = .1f;
        Check(changes == 7, "mutation notifications");
        camera.FitToScene(new Scene().Add(Shape.Box().At(4, 0, 0)));
        Check(changes == 8, "fit must notify once");
        camera.FitToScene(new Scene());
        Check(changes == 8, "empty fit notified");
    }

    private static void InvalidGeometry()
    {
        Throws(() => new Mesh([Vector3.Zero, Vector3.One, new(float.NaN, 0, 0)], [0, 1, 2]));
        Throws(() => new Mesh([Vector3.Zero, Vector3.One, Vector3.UnitX], [0, 1]));
        Throws(() => new Mesh([Vector3.Zero, Vector3.One, Vector3.UnitX], [-1, 1, 2]));
        Throws(() => Shape.Line(Vector3.Zero, Vector3.Zero));
        Throws(() => Shape.Arrow(Vector3.Zero, Vector3.Zero));
        Check(Shape.Line(Vector3.Zero, Vector3.UnitY).TriangleCount == 64, "line topology");
        Check(Shape.Arrow(Vector3.Zero, Vector3.UnitX).TriangleCount == 96, "arrow topology");
    }

    private static void IndexedStorage()
    {
        var indices = new[] { 0, 1, 2, 2, 1, 3 };
        var mesh = new Mesh([Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new(1, 1, 0)], indices);
        indices[0] = 9;
        var first = Shape.FromMesh(mesh);
        var second = first.At(1, 2, 3);
        Check(mesh.Vertices.Count == 4 && mesh.Indices[0] == 0, "indexed copy");
        Check(ReferenceEquals(first.Geometry, second.Geometry), "geometry copied");
        Check(first.Mesh is not Triangle3[], "indexed mesh expanded into triangles");
    }

    private static void SceneSemantics()
    {
        var child = Shape.Box().At(1, 0, 0);
        var group = Shape.Group(child).Scaled(2).Rotated(0, 0, MathF.PI / 2).At(3, 0, 0);
        var bounds = new Scene().Add(group).GetBounds()!.Value;
        Check(Vector3.Distance(bounds.Center, new(3, 2, 0)) < 1e-5f, "group transform order");
        var triangle = Triangle(new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0), 0xFF123456);
        var scene = new Scene().Add(triangle).Add(triangle.WithMaterial(new Material(0xFFABCDEF, false)));
        scene.AddLabel(new("behind", new(0, 0, 6))).AddLabel(new("visible", Vector3.Zero));
        var renderer = new DepthRenderer();
        var camera = new Camera(5, 0, 0);
        var frame = renderer.Render(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), triangle), "equal depth tie");
        Check(frame.Pixels.Span[50 * 100 + 50] == 0xFF123456, "unlit packed color");
        Check(frame.Labels.Count == 1 && frame.Labels[0].Label.Text == "visible", "label near clipping");
        var reversed = Shape.FromMesh(new Mesh(triangle.Geometry!.Vertices, [2, 1, 0]), new Material(doubleSided: false));
        Check(renderer.Render(new Scene().Add(reversed), camera, 100, 100).Pick(50, 50) is null, "backface culling");
        Check(renderer.Render(new Scene().Add(reversed.WithMaterial(new Material())), camera, 100, 100).Pick(50, 50) is not null, "double sided");
    }

    private static void Intersections()
    {
        var red = Triangle(new(-1, -1, 1), new(1, -1, -1), new(0, 1, 0), 0xFFFF0000);
        var blue = Triangle(new(-1, -1, -1), new(1, -1, 1), new(0, 1, 0), 0xFF0000FF);
        var camera = new Camera(5, 0, 0) { Projection = CameraProjection.Orthographic, OrthographicHeight = 3 };
        var renderer = new DepthRenderer();
        var frame = renderer.Render(new Scene().Add(red).Add(blue), camera, 90, 90);
        Check(ReferenceEquals(frame.Pick(30, 60), red), "left crossing");
        Check(ReferenceEquals(frame.Pick(60, 60), blue), "right crossing");
        var reversed = renderer.Render(new Scene().Add(blue).Add(red), camera, 90, 90);
        Check(ReferenceEquals(reversed.Pick(30, 60), red), "order-dependent left crossing");
        Check(ReferenceEquals(reversed.Pick(60, 60), blue), "order-dependent right crossing");
    }

    private static void RayOracle()
    {
        const int width = 32, height = 24;
        var random = new Random(28741);
        var renderer = new DepthRenderer();
        for (var trial = 0; trial < 60; trial++)
        {
            var camera = new Camera(3, 0, 0) { Projection = trial % 2 == 0 ? CameraProjection.Perspective : CameraProjection.Orthographic };
            var scene = new Scene();
            for (var i = 0; i < 8; i++)
            {
                Vector3 Point() => new((float)random.NextDouble() * 4 - 2, (float)random.NextDouble() * 4 - 2, (float)random.NextDouble() * 4);
                scene.Add(Triangle(Point(), Point(), Point(), 0xFF123456));
            }
            var frame = renderer.Render(scene, camera, width, height);
            var repeat = renderer.Render(scene, camera, width, height);
            Check(frame.Pixels.Span.SequenceEqual(repeat.Pixels.Span), "nondeterministic pixels");
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var scale = height / (2 * MathF.Tan(camera.FieldOfView / 2));
                    var origin = new Vector3(0, 0, 3);
                    var direction = new Vector3((x + .5f - width / 2f) / scale, -(y + .5f - height / 2f) / scale, -1);
                    if (camera.Projection == CameraProjection.Orthographic)
                    {
                        origin += new Vector3((x + .5f - width / 2f) * camera.OrthographicHeight / height,
                            -(y + .5f - height / 2f) * camera.OrthographicHeight / height, 0);
                        direction = -Vector3.UnitZ;
                    }
                    Shape? nearest = null;
                    var depth = float.PositiveInfinity;
                    foreach (var shape in scene.Shapes)
                    {
                        var triangle = shape.Mesh[0];
                        var edge1 = triangle.B - triangle.A;
                        var edge2 = triangle.C - triangle.A;
                        var p = Vector3.Cross(direction, edge2);
                        var determinant = Vector3.Dot(edge1, p);
                        if (MathF.Abs(determinant) < 1e-7f) continue;
                        var relative = origin - triangle.A;
                        var u = Vector3.Dot(relative, p) / determinant;
                        var q = Vector3.Cross(relative, edge1);
                        var v = Vector3.Dot(direction, q) / determinant;
                        var distance = Vector3.Dot(edge2, q) / determinant;
                        if (u >= 0 && v >= 0 && u + v <= 1 && distance >= camera.NearPlane && distance < depth)
                        { nearest = shape; depth = distance; }
                    }
                    Check(ReferenceEquals(frame.Pick(x, y), nearest), $"ray mismatch trial {trial}, pixel {x},{y}");
                }
        }
    }

    private static void ExtremeFrames()
    {
        var renderer = new DepthRenderer();
        var scene = new Scene().Add(Shape.Box());
        foreach (var camera in new[] { new Camera(float.MaxValue), new Camera(float.Epsilon), new Camera() { NearPlane = float.Epsilon }, new Camera() { OrthographicHeight = float.Epsilon, Projection = CameraProjection.Orthographic } })
        {
            var frame = renderer.Render(scene, camera, 16, 12);
            Check(frame.Pixels.Length == 192, "invalid frame dimensions");
        }
        var tinyOrtho = new Camera(5, 0, 0) { OrthographicHeight = float.Epsilon, Projection = CameraProjection.Orthographic };
        Check(renderer.Render(scene, tinyOrtho, 16, 12).Pick(8, 6) is not null, "tiny orthographic height lost visible box");
        var empty = renderer.Render(new Scene(), new Camera(), 1, 1);
        Check(empty.Pick(0, 0) is null && empty.Pick(-1, 0) is null, "empty pick");
        Check(empty.Pixels.Span[0] == 0xFFF4F6FA, "background");
        Throws(() => renderer.Render(scene, new Camera(), 0, 20));
        Throws(() => renderer.Render(scene, new Camera(), 2049, 20));
        var overflow = new Scene().Add(Shape.Group(Shape.Box().Scaled(float.MaxValue)).Scaled(float.MaxValue));
        renderer.Render(overflow, new Camera(), 16, 12);
        Throws(() => overflow.GetBounds());
    }

    private static void HierarchyLimits()
    {
        var group = Shape.Box();
        for (var i = 1; i < 64; i++) group = Shape.Group(group);
        Throws(() => Shape.Group(group));
        var shared = Shape.Group();
        for (var i = 0; i < 20; i++) shared = Shape.Group(shared, shared);
        Throws(() => new DepthRenderer().Render(new Scene().Add(shared), new Camera(), 1, 1));
        var triangles = Shape.Sphere();
        for (var i = 0; i < 12; i++) triangles = Shape.Group(triangles, triangles);
        Throws(() => new DepthRenderer().Render(new Scene().Add(triangles), new Camera(), 1, 1));
    }

    private static void BoundsBudget()
    {
        var shared = Shape.Sphere();
        while (shared.TriangleCount <= 1_000_000 && CountTriangles(shared) <= 1_000_000)
            shared = Shape.Group(shared, shared);
        var scene = new Scene().Add(shared);
        var camera = new Camera();
        var original = (camera.Target, camera.Distance, camera.OrthographicHeight);
        Throws(() => scene.GetBounds());
        Throws(() => camera.FitToScene(scene));
        Check((camera.Target, camera.Distance, camera.OrthographicHeight) == original, "failed fit changed camera");

        static long CountTriangles(Shape shape) => shape.TriangleCount + shape.Children.Sum(CountTriangles);
    }

    private static void RasterWorkBudget()
    {
        var large = Triangle(new(-20, -20, 0), new(20, -20, 0), new(0, 20, 0), 0xFF123456);
        var scene = new Scene();
        for (var i = 0; i < 20; i++) scene.Add(large);
        Throws(() => new DepthRenderer().Render(scene, new Camera(5, 0, 0), 1024, 1024));
    }

    private static void LegacyFeatureGuard()
    {
        var rendererScene = new Scene().Add(Shape.Box());
        var camera = new Camera();
        camera.Target = Vector3.One;
        Unsupported(() => SceneRenderer.Render(rendererScene, camera, 32, 32));
        camera.Target = Vector3.Zero;
        camera.Projection = CameraProjection.Orthographic;
        Unsupported(() => SceneRenderer.Render(rendererScene, camera, 32, 32));
        camera.Projection = CameraProjection.Perspective;
        camera.NearPlane = .2f;
        Unsupported(() => SceneRenderer.Render(rendererScene, camera, 32, 32));
        camera.NearPlane = .05f;
        Unsupported(() => SceneRenderer.Render(new Scene().Add(Shape.Group(Shape.Box())), camera, 32, 32));
        Unsupported(() => SceneRenderer.Render(new Scene().Add(Shape.Box().WithMaterial(new Material(lit: false))), camera, 32, 32));
        Check(SceneRenderer.Render(rendererScene, camera, 32, 32).Count > 0, "legacy primitive regression");
    }

    private static void Unsupported(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Expected unsupported feature diagnostic.");
    }

    private static Shape Triangle(Vector3 a, Vector3 b, Vector3 c, uint color) =>
        Shape.FromMesh(new Mesh([a, b, c], [0, 1, 2]), new Material(color, lit: false));

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected an argument exception.");
    }
}

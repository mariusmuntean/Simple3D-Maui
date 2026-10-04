using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared;

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
        ("reusable target matches owned frames across updates", ReusableRenderTarget),
        ("failed render validation clears reusable picking and labels", ReusableValidationFailure),
        ("group traversal avoids per-node iterator allocation", GroupTraversalAllocation),
        ("repeated small meshes stay within reusable render allocation budget", SmallMeshRenderAllocation),
        ("reusable traversal keeps many small instances within allocation budget", ManyInstancesRenderAllocation),
        ("branching groups preserve transform and equal-depth order", BranchingGroupTraversal),
        ("legacy renderer rejects unsupported scene features", LegacyFeatureGuard),
        ("sample scenes use depth rendering and fit the camera", SampleScenes),
        ("each sample animation moves and resets its scene", SampleAnimations),
        ("equipment output arrow keeps its tail fixed", EquipmentAnimation),
        ("telemetry bars animate within scale and change color", TelemetryAnimation),
        ("engineering scenes move their subject while anchors stay fixed", EngineeringAnimations),
        ("conveyor scan moves parcels through a fixed gate and updates status", ConveyorInspectionAnimation),
        ("solar tracker follows a moving sun above a fixed mount", SolarTrackerAnimation),
        ("network packets traverse fixed links between named racks", PacketRoutingAnimation),
        ("survey drone moves and spins propellers above a fixed pad", DroneSurveyAnimation),
        ("survey animation reuses its static geometry", DroneSurveyAllocation),
        ("battery charge remains inside its gauges while status colors change", BatteryStorageAnimation),
        ("gantry cable remains attached to its moving load", GantryCraneAnimation),
        ("energy and handling animations reuse geometry within their allocation budget", EnergyHandlingAllocation),
        ("solar animation reuses its panel cells", SolarTrackerAllocation),
        ("workflow movers reuse static parcel and packet parts", WorkflowMovingPartsAllocation)
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
        var renderer = new DepthRenderer();
        renderer.Render(new Scene(), camera, 1, 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Throws(() => renderer.Render(scene, camera, 2048, 2048));
        var rejectedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(rejectedBytes < 8_000_000,
            $"rejected scene allocated full output buffers ({rejectedBytes:N0} bytes)");

        static long CountTriangles(Shape shape) => shape.TriangleCount + shape.Children.Sum(CountTriangles);
    }

    private static void RasterWorkBudget()
    {
        var large = Triangle(new(-20, -20, 0), new(20, -20, 0), new(0, 20, 0), 0xFF123456);
        var scene = new Scene();
        for (var i = 0; i < 20; i++) scene.Add(large);
        try { new DepthRenderer().Render(scene, new Camera(5, 0, 0), 1024, 1024); }
        catch (RasterBudgetExceededException)
        {
            var renderer = new DepthRenderer();
            var target = new RenderTarget(1024, 1024);
            renderer.RenderInto(new Scene().Add(Shape.Box()), new Camera(5, 0, 0), target);
            Check(target.RasterSamples > 0, "target omitted successful raster work");
            try { renderer.RenderInto(scene, new Camera(5, 0, 0), target); }
            catch (RasterBudgetExceededException)
            {
                Check(target.Pick(512, 512) is null, "failed target render kept stale picking");
                Check(target.RasterSamples == 0, "failed target kept stale raster work");
                renderer.RenderInto(new Scene(), new Camera(), target);
                Check(target.Pick(512, 512) is null, "target did not recover after failure");
                Check(target.RasterSamples == 0, "empty scene reported raster work");
                return;
            }
        }
        throw new InvalidOperationException("Expected a specific raster budget diagnostic.");
    }

    private static void BranchingGroupTraversal()
    {
        var first = Triangle(new(-.5f, -.5f, 0), new(.5f, -.5f, 0), new(0, .5f, 0), 0xFFFF0000);
        var side = first.WithMaterial(new Material(0xFF00FF00, false));
        var tied = first.WithMaterial(new Material(0xFF0000FF, false));
        var a = first.Scaled(.5f).At(.25f, 0, 0);
        var b = side.Scaled(.25f).At(-.75f, .5f, 0);
        var c = tied.At(1, 0, 0);
        var group = Shape.Group(Shape.Group(a, b).Scaled(2).At(.5f, 0, 0), c).Scaled(2).At(-2, 0, 0);

        // Hand-derived transforms: local translation is scaled by each parent,
        // but a parent's own translation is applied after its scale.
        var expectedA = first.Scaled(2);
        var expectedB = side.At(-4, 2, 0);
        var expectedC = tied.Scaled(2);
        var expectedPicks = new Dictionary<Shape, Shape> { [expectedA] = a, [expectedB] = b, [expectedC] = c };
        var renderer = new DepthRenderer();
        var camera = new Camera(5, 0, 0) { Projection = CameraProjection.Orthographic, OrthographicHeight = 12 };
        var actual = renderer.Render(new Scene().Add(group), camera, 96, 96);
        var expected = renderer.Render(new Scene().Add(expectedA).Add(expectedB).Add(expectedC), camera, 96, 96);
        Check(actual.Pixels.Span.SequenceEqual(expected.Pixels.Span), "branching group changed transformed pixels");
        var visible = new HashSet<Shape>();
        for (var y = 0; y < 96; y++)
            for (var x = 0; x < 96; x++)
            {
                var pick = expected.Pick(x, y);
                var expectedPick = pick is null ? null : expectedPicks[pick];
                Check(ReferenceEquals(actual.Pick(x, y), expectedPick), "branching group changed pick order");
                if (expectedPick is not null) visible.Add(expectedPick);
            }
        Check(visible.SetEquals([a, b]), "test must cover both branches and hide the later equal-depth leaf");
    }

    private static void GroupTraversalAllocation()
    {
        var flat = new Scene();
        var grouped = new Scene();
        for (var i = 0; i < 128; i++)
        {
            var shape = Triangle(new(-.1f, -.1f, 0), new(.1f, -.1f, 0), new(0, .1f, 0), 0xFF80B2FF)
                .At((i % 16 - 8) * .2f, (i / 16 - 4) * .2f, 0);
            flat.Add(shape);
            grouped.Add(Shape.Group(Shape.Group(Shape.Group(shape))));
        }
        var camera = new Camera(5, 0, 0);
        var renderer = new DepthRenderer();
        var a = new RenderTarget(32, 32);
        var b = new RenderTarget(32, 32);
        for (var i = 0; i < 10; i++)
        {
            renderer.RenderInto(flat, camera, a);
            renderer.RenderInto(grouped, camera, b);
        }
        Check(a.Pixels.Span.SequenceEqual(b.Pixels.Span), "identity groups changed pixels");
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
                Check(ReferenceEquals(a.Pick(x, y), b.Pick(x, y)), "groups changed traversal or picking order");
        var flatBytes = Measure(flat, a);
        var groupedBytes = Measure(grouped, b);
        Check(groupedBytes <= flatBytes + 4096, $"group wrappers allocated {groupedBytes - flatBytes} extra bytes per frame");

        long Measure(Scene scene, RenderTarget target)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++) renderer.RenderInto(scene, camera, target);
            return (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        }
    }

    private static void ManyInstancesRenderAllocation()
    {
        var mesh = new Mesh([new(-.1f, -.1f, 0), new(.1f, -.1f, 0), new(0, .1f, 0)], [0, 1, 2]);
        var scene = new Scene();
        for (var i = 0; i < 128; i++)
            scene.Add(Shape.FromMesh(mesh).At((i % 16 - 8) * .2f, (i / 16 - 4) * .2f, 0));
        var camera = new Camera(5, 0, 0);
        var renderer = new DepthRenderer();
        var target = new RenderTarget(32, 32);
        for (var i = 0; i < 20; i++) renderer.RenderInto(scene, camera, target);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 80; i++) renderer.RenderInto(scene, camera, target);
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 80;
        Check(bytes <= 4096, $"128 small instances allocated {bytes} bytes per reusable render; budget is 4096");
        Console.WriteLine($"128 small instances: {bytes:N0} managed bytes per reusable render on this runner");
        var retainedPick = target.Pick(16, 16);
        Check(retainedPick is not null, "fixture has no center pick");
        var snapshot = renderer.Render(scene, camera, 32, 32);
        var pixels = snapshot.Pixels.ToArray();
        renderer.RenderInto(new Scene().Add(Shape.Box()), camera, new RenderTarget(16, 16));
        Check(ReferenceEquals(target.Pick(16, 16), retainedPick), "another target changed retained picking");
        Check(ReferenceEquals(snapshot.Pick(16, 16), retainedPick) && snapshot.Pixels.Span.SequenceEqual(pixels),
            "scratch traversal changed owned snapshot");
        var emptyGroup = Shape.Group();
        var excessive = new Scene().Add(Shape.Box()).Add(Shape.Group(Enumerable.Repeat(emptyGroup, Scene.MaximumNodes).ToArray()));
        Throws(() => renderer.RenderInto(excessive, camera, target));
        Check(target.Pick(16, 16) is null, "failed preparation retained picking");
        renderer.RenderInto(scene, camera, target);
        Check(target.Pixels.Span.SequenceEqual(pixels) && ReferenceEquals(target.Pick(16, 16), retainedPick),
            "failed preparation left stale nodes in the next render");
    }

    private static void SmallMeshRenderAllocation()
    {
        var sample = DemoScenes.PatternedSurface();
        var renderer = new DepthRenderer();
        var target = new RenderTarget(64, 64);
        for (var i = 0; i < 20; i++) renderer.RenderInto(sample.Scene, sample.Camera, target);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 80; i++) renderer.RenderInto(sample.Scene, sample.Camera, target);
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 80;
        Check(bytes <= 8192, $"small meshes allocated {bytes} bytes per reusable render; budget is 8192");
    }

    private static void ReusableValidationFailure()
    {
        var shape = Shape.Box();
        var scene = new Scene().Add(shape).AddLabel(new WorldLabel("Box", Vector3.Zero));
        var camera = new Camera(5, 0, 0);
        var renderer = new DepthRenderer();
        var target = new RenderTarget(32, 32);
        Action[] invalidRenders =
        [
            () => renderer.RenderInto(null!, camera, target),
            () => renderer.RenderInto(scene, null!, target),
            () => renderer.RenderInto(scene, camera, target, background: 0x00FFFFFF)
        ];
        foreach (var render in invalidRenders)
        {
            renderer.RenderInto(scene, camera, target);
            Check(ReferenceEquals(target.Pick(16, 16), shape) && target.Labels.Count == 1, "valid target was not populated");
            Throws(render);
            Check(target.Pick(16, 16) is null && target.Labels.Count == 0, "failed validation left stale scene information");
            renderer.RenderInto(scene, camera, target);
            Check(ReferenceEquals(target.Pick(16, 16), shape) && target.Labels.Count == 1, "target did not recover");
        }
    }

    private static void ReusableRenderTarget()
    {
        var renderer = new DepthRenderer();
        var camera = new Camera(5, .3f, .2f);
        var near = Shape.Box(0xFF80B2FF).Named("near").At(0, 0, .6f);
        var far = Shape.Sphere(0xFFFFA66F).Named("far").At(.4f, 0, -.5f);
        var scene = new Scene().Add(near).Add(far).AddLabel(new("origin", Vector3.Zero));
        var target = new RenderTarget(160, 120);
        var storage = target.PixelBuffer;
        var first = renderer.Render(scene, camera, 160, 120);
        renderer.RenderInto(scene, camera, target);
        Compare(first);

        var originalPixels = first.Pixels.ToArray();
        scene.Replace(near, near.At(-.8f, .3f, .8f));
        camera.Orbit(.2f, -.1f);
        var second = renderer.Render(scene, camera, 160, 120);
        renderer.RenderInto(scene, camera, target);
        Compare(second);
        Check(ReferenceEquals(storage, target.PixelBuffer), "target allocated new pixels for an update");
        Check(first.Pixels.Span.SequenceEqual(originalPixels), "owned frame changed after target update");
        Check(!first.Pixels.Span.SequenceEqual(second.Pixels.Span), "scene update did not alter frame");

        void Compare(RenderFrame owned)
        {
            Check(target.Pixels.Span.SequenceEqual(owned.Pixels.Span), "target pixels differ from owned render");
            Check(target.Labels.Count == owned.Labels.Count, "target labels differ from owned render");
            for (var y = 0; y < 120; y += 5)
                for (var x = 0; x < 160; x += 5)
                    Check(ReferenceEquals(target.Pick(x, y), owned.Pick(x, y)), "target picking differs");
        }
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

    private static void SampleScenes()
    {
        var names = DemoScenes.RegressionScenes.Select(sample => sample.Name).ToArray();
        foreach (var name in new[] { "Equipment", "Packing", "Surface", "Assembly", "Molecule", "Telemetry", "City", "Robot Arm", "Orbit", "Wind", "Conveyor Inspection", "Solar Tracker", "Packet Routing", "Drone Survey", "Patterned Surface", "Battery Storage", "Gantry Crane" })
            Check(names.Count(candidate => candidate == name) == 1, $"missing or duplicate {name} example");
        foreach (var sample in DemoScenes.RegressionScenes)
        {
            Check(sample.Scene.GetBounds() is not null, "sample has no geometry");
            Check(sample.Scene.Labels.Count > 0, "sample has no labels");
            var frame = new DepthRenderer().Render(sample.Scene, sample.Camera, 240, 180, 0xFF18243B);
            Check(frame.Pixels.Span.ToArray().Count(p => p != 0xFF18243B) > 500, $"{sample.Name} drawing too small");
            var hasPickableShape = false;
            for (var y = 0; y < frame.Height && !hasPickableShape; y++)
                for (var x = 0; x < frame.Width; x++)
                    if (frame.Pick(x, y) is not null) { hasPickableShape = true; break; }
            Check(hasPickableShape, $"{sample.Name} has no pickable geometry");
        }
    }

    private static void SampleAnimations()
    {
        var renderer = new DepthRenderer();
        foreach (var sample in DemoScenes.RegressionScenes)
        {
            var initial = renderer.Render(sample.Scene, sample.Camera, 320, 240).Pixels.ToArray();
            sample.Animate(.75f);
            var animated = renderer.Render(sample.Scene, sample.Camera, 320, 240).Pixels.ToArray();
            Check(initial.Zip(animated).Count(pair => pair.First != pair.Second) > 20,
                $"{sample.Name} animation is not visible");
            sample.Animate(0);
            var restored = renderer.Render(sample.Scene, sample.Camera, 320, 240).Pixels.Span;
            Check(restored.SequenceEqual(initial), $"{sample.Name} animation did not reset");
        }
    }

    private static void TelemetryAnimation()
    {
        var sample = DemoScenes.Telemetry();
        var smallest = float.PositiveInfinity;
        var largest = 0f;
        var colors = new HashSet<uint>();
        for (var step = 0; step <= 120; step++)
        {
            sample.Animate(step / 20f);
            var bars = sample.Scene.Flatten().Select(node => node.Shape)
                .Where(shape => shape.Name?.StartsWith("Sample ", StringComparison.Ordinal) == true).ToArray();
            Check(bars.Length == 5, "animation lost a bar");
            foreach (var bar in bars)
            {
                smallest = MathF.Min(smallest, bar.Size.Y);
                largest = MathF.Max(largest, bar.Size.Y);
                colors.Add(bar.Color);
                Check(bar.Size.Y >= .35f && bar.Size.Y <= 2.5f, "bar left the chart scale");
                Check(MathF.Abs(bar.Position.Y - bar.Size.Y / 2) < .0001f, "bar left the baseline");
                Check(bar.Name == $"Sample {Array.IndexOf(bars, bar) + 1}: {bar.Size.Y:0.00}",
                    "bar inspection does not show its current value");
            }
            if (step == 20)
            {
                var frame = new DepthRenderer().Render(sample.Scene, sample.Camera, 320, 240);
                var picked = false;
                for (var y = 0; y < frame.Height && !picked; y++)
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var shape = frame.Pick(x, y);
                        if (shape?.Name?.StartsWith("Sample ", StringComparison.Ordinal) != true) continue;
                        Check(bars.Contains(shape), "picked bar is not the current animated shape");
                        picked = true;
                        break;
                    }
                Check(picked, "animated bars are not pickable");
            }
        }
        Check(largest - smallest > 1.5f, "bar motion has too little range");
        Check(colors.Count > 10, "bar colors do not follow their heights");
        sample.Animate(0);
    }

    private static void EquipmentAnimation()
    {
        var sample = DemoScenes.Equipment();
        sample.Animate(.75f);
        var arrow = sample.Scene.Shapes.Single(shape => shape.Name == "Output axis");
        Check(arrow.Position == new Vector3(.55f, -.2f, 0),
            "output arrow must be positioned at its fixed tail");
        Check(arrow.Geometry!.Vertices.Contains(Vector3.Zero), "output arrow tail moved");
    }

    private static void EngineeringAnimations()
    {
        foreach (var (name, anchor, moving) in new[]
        {
            ("Robot Arm", "Base", "Gripper"),
            ("Orbit", "Star", "Planet"),
            ("Wind", "Tower", "Blade 1")
        })
        {
            var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == name);
            if (name == "Orbit")
                Check(sample.Scene.Shapes.Single(shape => shape.Name == "Orbit path").Children
                    .All(segment => segment.Name == "Orbit path"), "picked orbit segment has no useful name");
            var anchorBefore = Position(anchor);
            var movingBefore = Position(moving);
            var geometryBefore = sample.Scene.Flatten().Select(node => node.Shape.Mesh).ToArray();
            sample.Animate(.85f);
            Check(sample.Scene.Flatten().Select(node => node.Shape.Mesh).Zip(geometryBefore)
                .All(pair => ReferenceEquals(pair.First, pair.Second)), $"{name} rebuilt geometry during animation");
            Check(Vector3.Distance(Position(anchor), anchorBefore) < .0001f,
                $"{name} animation moved its fixed anchor");
            Check(Vector3.Distance(Position(moving), movingBefore) > .05f,
                $"{name} animation did not move {moving}");
            sample.Animate(0);
            Check(Vector3.Distance(Position(moving), movingBefore) < .0001f,
                $"{name} animation did not reset {moving}");

            for (var i = 0; i < 10; i++) sample.Animate(i / 60f);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 120; i++) sample.Animate(i / 60f);
            var perFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 120;
            Check(perFrame < 2_000, $"{name} rebuilt static parts: {perFrame} bytes/frame");

            Vector3 Position(string shapeName)
            {
                var node = sample.Scene.Flatten().Single(node => node.Shape.Name == shapeName);
                return Vector3.Transform(Vector3.Zero, node.Transform);
            }
        }
    }

    private static void ConveyorInspectionAnimation()
    {
        var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == "Conveyor Inspection");
        var gate = sample.Scene.Flatten().Single(node => node.Shape.Name == "Scanner head");
        var gateBefore = gate.Transform;
        var parcelBefore = sample.Scene.Flatten().Single(node => node.Shape.Name == "Parcel 1: pending");
        var start = Vector3.Transform(Vector3.Zero, parcelBefore.Transform);
        sample.Animate(2f);
        var parcelAfter = sample.Scene.Flatten().Single(node => node.Shape.Name == "Parcel 1: passed");
        var end = Vector3.Transform(Vector3.Zero, parcelAfter.Transform);
        Check(end.X > start.X + 1f, "parcel did not pass through scan gate");
        Check(parcelAfter.Shape.Color != parcelBefore.Shape.Color, "scan status did not change color");
        Check(sample.Scene.Flatten().Single(node => node.Shape.Name == "Scanner head").Transform == gateBefore,
            "scan gate moved during inspection");
        sample.Animate(0);
        var restored = sample.Scene.Flatten().Single(node => node.Shape.Name == "Parcel 1: pending");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, restored.Transform), start) < .0001f,
            "parcel did not reset to the start of the belt");
    }

    private static void SolarTrackerAnimation()
    {
        var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == "Solar Tracker");
        var mount = sample.Scene.Flatten().Single(node => node.Shape.Name == "Tracker mount");
        var panel = sample.Scene.Flatten().Single(node => node.Shape.Name == "Solar panel");
        var sun = sample.Scene.Flatten().Single(node => node.Shape.Name == "Sun");
        var mountBefore = mount.Transform;
        var panelUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, panel.Transform));
        var sunBefore = Vector3.Transform(Vector3.Zero, sun.Transform);
        sample.Animate(.85f);
        var panelAfter = sample.Scene.Flatten().Single(node => node.Shape.Name == "Solar panel");
        var sunAfter = sample.Scene.Flatten().Single(node => node.Shape.Name == "Sun");
        Check(Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, panelAfter.Transform)), panelUp) > .1f,
            "solar panel did not tilt toward the sun");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, sunAfter.Transform), sunBefore) > .5f,
            "sun did not move along its path");
        Check(sample.Scene.Flatten().Single(node => node.Shape.Name == "Tracker mount").Transform == mountBefore,
            "solar tracker moved its fixed mount");
        foreach (var time in new[] { .85f, 4.35f })
        {
            sample.Animate(time);
            var tilted = sample.Scene.Flatten().Single(node => node.Shape.Name == "Solar panel");
            var currentSun = sample.Scene.Flatten().Single(node => node.Shape.Name == "Sun");
            var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, tilted.Transform));
            var direction = Vector3.Normalize(Vector3.Transform(Vector3.Zero, currentSun.Transform) -
                Vector3.Transform(Vector3.Zero, tilted.Transform));
            Check(Vector3.Dot(normal, direction) > Vector3.Dot(panelUp, direction) + .05f,
                "solar panel tilts away from the sun instead of improving alignment");
        }
        sample.Animate(0);
        var restored = sample.Scene.Flatten().Single(node => node.Shape.Name == "Solar panel");
        Check(Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, restored.Transform)), panelUp) < .0001f,
            "solar panel did not reset");
    }

    private static void PacketRoutingAnimation()
    {
        var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == "Packet Routing");
        var sourceLink = sample.Scene.Flatten().Single(node => node.Shape.Name == "Source link");
        var destinationLink = sample.Scene.Flatten().Single(node => node.Shape.Name == "Destination link");
        var packet = sample.Scene.Flatten().Single(node => node.Shape.Name == "Packet 1");
        var sourceBefore = sourceLink.Transform;
        var destinationBefore = destinationLink.Transform;
        var start = Vector3.Transform(Vector3.Zero, packet.Transform);
        var sourceRack = sample.Scene.Flatten().Single(node => node.Shape.Name == "Source rack");
        var rackCenter = Vector3.Transform(Vector3.Zero, sourceRack.Transform);
        Check(MathF.Abs(start.Y - (rackCenter.Y + sourceRack.Shape.Size.Y / 2)) < .1f,
            "packet route floats above the source rack");
        sample.Animate(.85f);
        var moved = sample.Scene.Flatten().Single(node => node.Shape.Name == "Packet 1");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, moved.Transform), start) > .5f,
            "network packet did not traverse a link");
        Check(sample.Scene.Flatten().Single(node => node.Shape.Name == "Source link").Transform == sourceBefore &&
              sample.Scene.Flatten().Single(node => node.Shape.Name == "Destination link").Transform == destinationBefore,
            "routing links moved during animation");
        sample.Animate(0);
        var restored = sample.Scene.Flatten().Single(node => node.Shape.Name == "Packet 1");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, restored.Transform), start) < .0001f,
            "network packet did not reset to source");
    }

    private static void DroneSurveyAnimation()
    {
        var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == "Drone Survey");
        var pad = sample.Scene.Flatten().Single(node => node.Shape.Name == "Landing pad");
        var body = sample.Scene.Flatten().Single(node => node.Shape.Name == "Drone body");
        var blade = sample.Scene.Flatten().Single(node => node.Shape.Name == "Propeller 1");
        var padBefore = pad.Transform;
        var bodyBefore = Vector3.Transform(Vector3.Zero, body.Transform);
        var bladeAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, blade.Transform));
        sample.Animate(.85f);
        var bodyAfter = sample.Scene.Flatten().Single(node => node.Shape.Name == "Drone body");
        var bladeAfter = sample.Scene.Flatten().Single(node => node.Shape.Name == "Propeller 1");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, bodyAfter.Transform), bodyBefore) > .2f,
            "survey drone did not move over the pad");
        Check(Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, bladeAfter.Transform)), bladeAxis) > .2f,
            "survey propeller did not spin");
        Check(sample.Scene.Flatten().Single(node => node.Shape.Name == "Landing pad").Transform == padBefore,
            "landing pad moved with the drone");
        sample.Animate(0);
        var restored = sample.Scene.Flatten().Single(node => node.Shape.Name == "Drone body");
        Check(Vector3.Distance(Vector3.Transform(Vector3.Zero, restored.Transform), bodyBefore) < .0001f,
            "survey drone did not reset");
    }

    private static void DroneSurveyAllocation()
    {
        var sample = DemoScenes.DroneSurvey();
        for (var i = 0; i < 10; i++) sample.Animate(i / 60f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++) sample.Animate(i / 60f);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
        Check(perFrame < 12_000, $"survey animation rebuilt static geometry: {perFrame} bytes/frame");
    }

    private static void BatteryStorageAnimation()
    {
        var sample = DemoScenes.RegressionScenes.SingleOrDefault(scene => scene.Name == "Battery Storage")
            ?? throw new InvalidOperationException("Battery Storage showcase is missing");
        var cabinets = sample.Scene.Shapes.Single(shape => shape.Name == "Battery cabinets");
        var colors = new HashSet<uint>();
        var heights = new List<float>();
        for (var step = 0; step <= 100; step++)
        {
            sample.Animate(step / 10f);
            var charges = sample.Scene.Flatten().Where(node => node.Shape.Name?.StartsWith("Charge ") == true).ToArray();
            Check(charges.Length == 6, "battery animation lost a charge gauge");
            foreach (var (charge, _) in charges)
            {
                Check(charge.Size.Y >= .1f && charge.Size.Y <= 1.1f, "charge escaped its gauge");
                var scale = sample.Scene.Flatten().Single(node => node.Shape.Name == $"Module {charge.Name![7..]} gauge scale").Shape;
                Check(MathF.Abs(charge.Position.Y - charge.Size.Y / 2 - (scale.Position.Y - scale.Size.Y / 2)) < .0001f,
                    "charge gauge lost its bottom anchor");
                Check(charge.Position.Y + charge.Size.Y / 2 <= scale.Position.Y + scale.Size.Y / 2,
                    "charge exceeds the gauge scale");
                colors.Add(charge.Color);
                heights.Add(charge.Size.Y);
            }
            Check(ReferenceEquals(cabinets, sample.Scene.Shapes.Single(shape => shape.Name == "Battery cabinets")),
                "battery animation rebuilt static cabinets");
        }
        Check(heights.Max() - heights.Min() > .7f && colors.Count > 20, "charge state barely changes");
    }

    private static void GantryCraneAnimation()
    {
        var sample = DemoScenes.RegressionScenes.SingleOrDefault(scene => scene.Name == "Gantry Crane")
            ?? throw new InvalidOperationException("Gantry Crane showcase is missing");
        var structure = sample.Scene.Shapes.Single(shape => shape.Name == "Gantry structure");
        var positions = new List<Vector3>();
        for (var step = 0; step <= 100; step++)
        {
            sample.Animate(step / 10f);
            var load = sample.Scene.Flatten().Single(node => node.Shape.Name == "Carried load");
            var cable = sample.Scene.Flatten().Single(node => node.Shape.Name == "Hoist cable");
            var trolley = sample.Scene.Flatten().Single(node => node.Shape.Name == "Hoist trolley");
            var bridge = sample.Scene.Flatten().Single(node => node.Shape.Name == "Moving bridge");
            var center = Vector3.Transform(Vector3.Zero, load.Transform);
            var loadTop = Vector3.Transform(Vector3.UnitY / 2, load.Transform);
            var cableBottom = Vector3.Transform(-Vector3.UnitY / 2, cable.Transform);
            var cableTop = Vector3.Transform(Vector3.UnitY / 2, cable.Transform);
            Check(Vector3.Distance(loadTop, cableBottom) < .0001f, "hoist cable detached from load");
            Check(Vector3.Distance(cableTop, Vector3.Transform(-Vector3.UnitY / 2, trolley.Transform)) < .0001f,
                "hoist cable detached from trolley");
            Check(Vector3.Distance(Vector3.Transform(Vector3.UnitY / 2, trolley.Transform),
                Vector3.Transform(-Vector3.UnitY / 2, bridge.Transform)) < .0001f, "trolley detached from bridge");
            Check(MathF.Abs(center.X) < 1.3f && center.Y is >= -.5f and <= .3f, "load left its working envelope");
            Check(ReferenceEquals(structure, sample.Scene.Shapes.Single(shape => shape.Name == "Gantry structure")),
                "gantry animation rebuilt its stationary structure");
            positions.Add(center);
        }
        Check(positions.Max(p => p.X) - positions.Min(p => p.X) > 1.5f, "trolley barely traverses");
        Check(positions.Max(p => p.Y) - positions.Min(p => p.Y) > .5f, "hoist barely lifts");
    }

    private static void EnergyHandlingAllocation()
    {
        foreach (var name in new[] { "Battery Storage", "Gantry Crane" })
        {
            var sample = DemoScenes.RegressionScenes.Single(scene => scene.Name == name);
            var meshes = sample.Scene.Flatten().Select(node => (node.Shape.Mesh, node.Shape.Name)).ToArray();
            for (var i = 0; i < 20; i++) sample.Animate(i / 60f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 120; i++) sample.Animate(i / 60f);
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 120;
            Check(bytes < 4096, $"{name} animation allocated {bytes} bytes/frame; budget is 4096");
            var after = sample.Scene.Flatten().Select(node => (node.Shape.Mesh, node.Shape.Name)).ToArray();
            Check(meshes.Length == after.Length && meshes.Zip(after).All(pair =>
                ReferenceEquals(pair.First.Mesh, pair.Second.Mesh) && pair.First.Name == pair.Second.Name),
                $"{name} animation rebuilt geometry or changed logical child order");
        }
    }

    private static void SolarTrackerAllocation()
    {
        var sample = DemoScenes.SolarTracker();
        for (var i = 0; i < 10; i++) sample.Animate(i / 60f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++) sample.Animate(i / 60f);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
        Check(perFrame < 4_000, $"solar animation rebuilt panel cells: {perFrame} bytes/frame");
    }

    private static void WorkflowMovingPartsAllocation()
    {
        foreach (var sample in new[] { DemoScenes.ConveyorInspection(), DemoScenes.PacketRouting() })
        {
            for (var i = 0; i < 10; i++) sample.Animate(i / 60f);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 120; i++) sample.Animate(i / 60f);
            var perFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 120;
            Check(perFrame < 1_200, $"{sample.Name} rebuilt static moving parts: {perFrame} bytes/frame");
        }
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

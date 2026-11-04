using System.Numerics;
using Simple3D.Core;

internal static class GpuSceneTests
{
    internal static void Run()
    {
        var box = Shape.Box();
        var scene = new Scene().Add(box);
        var camera = new Camera(5, 0, 0);
        var frame = new GpuScene();
        frame.Update(scene, camera, 100, 100);
        var geometry = frame.Items[0].Geometry;
        scene.Replace(box, box.Rotated(0, .2f, 0));
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(geometry, frame.Items[0].Geometry), "animation must reuse geometry");
        Check(geometry.Vertices.Length == box.TriangleCount * 3 * GpuGeometry.FloatsPerVertex, "vertex layout");
        var near = box.At(0, 0, 1);
        scene.Clear(); scene.Add(box).Add(near);
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "nearest pick");
        Check(frame.Pick(-1, 50) is null, "outside pick");
        camera.Projection = CameraProjection.Orthographic;
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "orthographic pick");
        scene.Remove(box);
        camera.NearPlane = 4;
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "near clipping must retain back face of double sided box");
        scene.Clear(); scene.AddLabel(new WorldLabel("center", Vector3.Zero));
        camera.NearPlane = .05f;
        frame.Update(scene, camera, 100, 100);
        Check(frame.Items.Count == 0 && frame.Labels.Count == 1, "clear geometry and project labels");
        Check(Vector2.Distance(frame.Labels[0].Position, new(50, 50)) < .001f, "center label");
        var p = Vector4.Transform(new Vector4(0, 0, 0, 1), frame.ViewProjection);
        Check(Math.Abs(p.X / p.W) < .001f && p.Z / p.W >= 0, "clip projection");
        Console.WriteLine("PASS GPU geometry reuse, visibility picking, clipping, labels and projection");
        RunSceneContentTolerance();
    }

    // Scene content the software renderer accepts must not fail GPU preparation; only real
    // scene errors use GpuSceneException, which SceneView reports without disabling hardware.
    private static void RunSceneContentTolerance()
    {
        var camera = new Camera(5, 0, 0);
        var frame = new GpuScene();
        // Determinant below float.Epsilon: Matrix4x4.Invert fails, but the shape still renders in software.
        var flat = Shape.Box().Scaled(1e-24f, 1e-24f, 1);
        var visible = Shape.Box();
        var scene = new Scene().Add(visible).Add(flat);
        Check(!Matrix4x4.Invert(Matrix4x4.CreateScale(1e-24f, 1e-24f, 1), out _), "test transform must be non-invertible");
        frame.Update(scene, camera, 100, 100);
        Check(frame.Items.Count == 2, "near-singular transform must not fail or drop the shape");
        Check(AllFinite(frame.Items[1].Normal), "near-singular normal matrix must be finite");

        // Overflowing bounds: skipped like the software renderer's non-finite triangles, not thrown.
        var overflowing = Shape.Box().At(3e38f, 0, 0).Scaled(1e38f);
        scene = new Scene().Add(visible).Add(overflowing);
        new DepthRenderer().Render(scene, camera, 64, 64);
        frame.Update(scene, camera, 100, 100);
        Check(frame.Items.Count == 1 && ReferenceEquals(frame.Items[0].Shape, visible), "overflowing shape must be skipped");
        Check(ReferenceEquals(frame.Pick(50, 50), visible), "remaining shape stays pickable");

        // Huge but finite scale: cofactor must not overflow.
        Check(AllFinite(GpuScene.NormalMatrix(Matrix4x4.CreateScale(1e30f))), "large scale normal matrix overflowed");

        // Cofactor normals match transpose(inverse) in direction, including mirrored transforms.
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var world = Matrix4x4.CreateScale(R(random) * 2, R(random) * 2, i % 2 == 0 ? -R(random) : R(random)) *
                Matrix4x4.CreateFromYawPitchRoll(R(random) * 6, R(random) * 6, R(random) * 6) * Matrix4x4.CreateTranslation(R(random), R(random), R(random));
            Check(Matrix4x4.Invert(world, out var inverse), "random transform invertible");
            var expectedTransform = Matrix4x4.Transpose(inverse);
            var actualTransform = GpuScene.NormalMatrix(world);
            var n = Vector3.Normalize(new Vector3(R(random) - .5f, R(random) - .5f, R(random) - .5f));
            var expected = Vector3.Normalize(Vector3.TransformNormal(n, expectedTransform));
            var actual = Vector3.Normalize(Vector3.TransformNormal(n, actualTransform));
            Check(Vector3.Dot(expected, actual) > .9999f, $"normal matrix direction mismatch at sample {i}");
        }

        // The budget is a scene error with its own type, not a device failure.
        scene = new Scene();
        for (var i = 0; i <= DepthRenderer.MaximumTriangles / Shape.Box().TriangleCount; i++) scene.Add(Shape.Box());
        try { frame.Update(scene, camera, 100, 100); Check(false, "over-budget scene accepted"); }
        catch (GpuSceneException) { }
        Console.WriteLine("PASS GPU scene tolerates singular and overflowing transforms; budget is a scene error");
    }
    private static float R(Random random) => .2f + (float)random.NextDouble();
    private static bool AllFinite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M21) && float.IsFinite(m.M22) &&
        float.IsFinite(m.M23) && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

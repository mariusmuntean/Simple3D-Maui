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
        Check(geometry.Expand().Length == box.TriangleCount * 3 * GpuGeometry.FloatsPerVertex && geometry.VertexCount == box.TriangleCount * 3, "vertex layout");
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
        RunReversedDepth();
        Console.WriteLine("PASS GPU geometry reuse, visibility picking, clipping, labels and projection");
        RunSceneContentTolerance();
        RunSkinning();
    }

    private static void RunSkinning()
    {
        var (skinned, skeleton) = SkinningTests.Bar();
        var rest = Shape.FromSkinnedMesh(skinned);
        var bend = SkeletonPose.Create(skeleton, [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)]);
        var bent = rest.WithPose(bend);
        var frame = new GpuScene();
        var camera = new Camera(6, 0, 0) { Target = new(-.5f, 1, 0), Projection = CameraProjection.Orthographic };
        var scene = new Scene().Add(rest);
        frame.Update(scene, camera, 120, 120);
        var geometry = frame.Items[0].Geometry;
        Check(geometry.IsSkinned && geometry.VertexStrideFloats == 24 && geometry.ByteSize == geometry.VertexCount * 24L * sizeof(float), "skinned vertex size");
        Check(ReferenceEquals(frame.Items[0].Joints, skeleton.RestPose.SkinningMatrices), "rest palette reference");
        Check(((PosedGeometry)rest.Mesh).GetType().GetField("_deformed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(rest.Mesh) is null,
            "Update must not deform the rest mesh");
        var expanded = geometry.Expand();
        Check(expanded.Length == geometry.VertexCount * 24, "skinned upload length");
        Check(expanded[0] == skinned.Mesh.Vertices[skinned.Mesh.Indices[0]].X && expanded[1] == skinned.Mesh.Vertices[skinned.Mesh.Indices[0]].Y,
            "upload uses bind positions");
        Check(expanded[16] == 0 && expanded[17] == 0 && expanded[20] == 1 && expanded[21] == 0,
            "first vertex joint and weight");
        var tipOffset = 0;
        for (var i = 0; i < skinned.Mesh.Indices.Count; i++)
            if (skinned.Mesh.Indices[i] >= 8) { tipOffset = i * 24; break; }
        Check(expanded[tipOffset + 16] == 1 && expanded[tipOffset + 20] == 1,
            "tip vertex joint and weight");
        Check(((PosedGeometry)rest.Mesh).GetType().GetField("_deformed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(rest.Mesh) is null,
            "Expand must not deform the rest mesh");

        scene.Replace(rest, bent);
        frame.Update(scene, camera, 120, 120);
        Check(ReferenceEquals(geometry, frame.Items[0].Geometry), "pose changes reuse bind geometry");
        Check(ReferenceEquals(frame.Items[0].Joints, bend.SkinningMatrices) && !ReferenceEquals(frame.Items[0].Joints, skeleton.RestPose.SkinningMatrices),
            "pose changes carry their own palette");
        Check(Vector3.Distance(Vector3.Transform(new Vector3(0, 2, 0), frame.Items[0].Joints![1]), new(-1, 1, 0)) < 1e-4f,
            "tip palette contains the bent transform");
        Check(((PosedGeometry)bent.Mesh).GetType().GetField("_deformed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(bent.Mesh) is null,
            "Update must not deform bent mesh");
        var tip = new WorldLabel("tip", new(-.9f, 1, 0));
        scene.AddLabel(tip);
        frame.Update(scene, camera, 120, 120);
        var pixel = frame.Labels[0].Position;
        Check(ReferenceEquals(frame.Pick(pixel.X, pixel.Y), bent), "posed tip must be pickable");
        var distant = SkeletonPose.Create(skeleton, [Quaternion.Identity, Quaternion.Identity], new(0, 0, -20));
        scene.Replace(bent, rest.WithPose(distant));
        frame.Update(scene, camera, 120, 120);
        var farPoint = Vector4.Transform(new Vector4(0, 1, -20, 1), frame.ViewProjection);
        Check(farPoint.Z / farPoint.W >= 0 && farPoint.Z / farPoint.W <= 1, "posed bounds set orthographic far plane");

        var one = geometry.ByteSize + skeleton.Joints.Count * 64L;
        new GpuScene { ResourceBudgetBytes = one }.Update(new Scene().Add(rest), camera, 120, 120);
        var two = new GpuScene { ResourceBudgetBytes = geometry.ByteSize + skeleton.Joints.Count * 128L };
        two.Update(new Scene().Add(rest).Add(bent.At(2, 0, 0)), camera, 120, 120);
        Check(ReferenceEquals(two.Items[0].Geometry, two.Items[1].Geometry) &&
              ReferenceEquals(two.Items[0].Joints, skeleton.RestPose.SkinningMatrices) &&
              ReferenceEquals(two.Items[1].Joints, bend.SkinningMatrices), "instances share geometry and retain separate palettes");
        try
        {
            new GpuScene { ResourceBudgetBytes = geometry.ByteSize + skeleton.Joints.Count * 128L - 1 }.Update(new Scene().Add(rest).Add(bent.At(2, 0, 0)), camera, 120, 120);
            Check(false, "each skinned instance must count its palette");
        }
        catch (GpuSceneException) { }
        RunSkinnedAttributesAndCulling();
        Console.WriteLine("PASS GPU bind geometry, pose palettes, bounds, picking and budget");
    }

    private static void RunSkinnedAttributesAndCulling()
    {
        var skeleton = new Skeleton([new Joint("root", -1, default, Quaternion.Identity, Vector3.One, Matrix4x4.Identity)]);
        var mesh = new Mesh([new(-.5f, -.5f, 0), new(.5f, -.5f, 0), new(0, .5f, 0)], [0, 1, 2],
            [new(.2f, .3f), new(.4f, .5f), new(.6f, .7f)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [0xFF204060, 0xFFFFFFFF, 0xFFFFFFFF]);
        var skinned = new SkinnedMesh(mesh, skeleton, Enumerable.Repeat(new VertexInfluence(new(0, 0, 0, 0), Vector4.UnitX), 3));
        var front = Shape.FromSkinnedMesh(skinned, new Material(doubleSided: false));
        var geometry = new GpuGeometry(front);
        var data = geometry.Expand();
        Check(data[4] == 0 && data[5] == 0 && data[6] == 1 && data[8] == .2f && data[9] == .3f,
            "skinned normal and UV attributes");
        Check(MathF.Abs(data[12] - 32 / 255f) < 1e-6f && MathF.Abs(data[13] - 64 / 255f) < 1e-6f &&
              MathF.Abs(data[14] - 96 / 255f) < 1e-6f && data[15] == 1, "skinned vertex color");

        var camera = new Camera(5, 0, 0) { Projection = CameraProjection.Orthographic, Target = new(1, 0, 0) };
        var pose = SkeletonPose.Create(skeleton, [Quaternion.Identity], new(2, 0, 0));
        var moved = front.WithPose(pose);
        var scene = new Scene().Add(moved).AddLabel(new WorldLabel("moved", new(2, 0, 0)));
        var frame = new GpuScene();
        frame.Update(scene, camera, 120, 120);
        var pixel = frame.Labels[0].Position;
        Check(ReferenceEquals(frame.Pick(pixel.X, pixel.Y), moved), "front-facing posed triangle picks at moved location");
        Check(frame.Pick(60, 60) is null, "bind location no longer picks");
        var backMesh = new Mesh(mesh.Vertices, [0, 2, 1]);
        var back = Shape.FromSkinnedMesh(new SkinnedMesh(backMesh, skeleton, skinned.Influences), new Material(doubleSided: false)).WithPose(pose);
        frame.Update(new Scene().Add(back), camera, 120, 120);
        Check(frame.Pick(pixel.X, pixel.Y) is null, "posed back face remains culled");
    }

    // Scene content the software renderer accepts must not fail GPU preparation; only real
    // scene errors use GpuSceneException, which SceneView reports without disabling hardware.
    // Reversed depth must keep x, y and w identical to the standard projection and map the near
    // plane to 1 and increasing distance toward 0 (perspective: near/z; orthographic: linear to far).
    private static void RunReversedDepth()
    {
        var frame = new GpuScene();
        var scene = new Scene().Add(Shape.Box().Scaled(4));
        var camera = new Camera(5, .3f, .2f);
        foreach (var projection in new[] { CameraProjection.Perspective, CameraProjection.Orthographic })
        {
            camera.Projection = projection;
            frame.Update(scene, camera, 120, 80);
            var previous = float.PositiveInfinity;
            foreach (var distance in new[] { camera.NearPlane, 1f, 3f, 5f, 7f })
            {
                var basis = camera.Basis();
                var world = basis.Eye + basis.Forward * distance + basis.Right * .1f + basis.Up * .2f;
                var standard = Vector4.Transform(new Vector4(world, 1), frame.ViewProjection);
                var reversed = Vector4.Transform(new Vector4(world, 1), frame.ReversedViewProjection);
                Check(Math.Abs(standard.X - reversed.X) < 1e-4f && Math.Abs(standard.Y - reversed.Y) < 1e-4f && Math.Abs(standard.W - reversed.W) < 1e-4f,
                    $"{projection} reversed depth changed x/y/w");
                var depth = reversed.Z / reversed.W;
                Check(depth <= 1.0001f && depth > 0 && depth < previous, $"{projection} reversed depth must decrease from 1 at the near plane ({depth} at {distance})");
                previous = depth;
            }
        }
        camera.Projection = CameraProjection.Perspective;
        frame.Update(scene, camera, 120, 80);
        var b = camera.Basis();
        var near = Vector4.Transform(new Vector4(b.Eye + b.Forward * camera.NearPlane, 1), frame.ReversedViewProjection);
        Check(Math.Abs(near.Z / near.W - 1) < 1e-4f, "perspective near plane must map to reversed depth 1");
    }

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
        // The node visit budget (enforced by Scene.Flatten) is a scene error too.
        var group = Shape.Group(); // No geometry, so only the node budget applies.
        for (var depth = 0; depth < 4; depth++) group = Shape.Group(Enumerable.Repeat(group, 20).ToArray());
        scene = new Scene().Add(group);
        try { frame.Update(scene, camera, 100, 100); Check(false, "over-node-budget scene accepted"); }
        catch (GpuSceneException error) { Check(error.Message.Contains("node"), "node budget message"); }
        // The GPU resource budget counts each distinct mesh and texture once, before any upload.
        var texture = new Texture2D(64, 64, Enumerable.Repeat(0xFF808080u, 64 * 64)); // 16 KiB
        var budgeted = new GpuScene { ResourceBudgetBytes = 20 * 1024 };
        var textured = Shape.Box().WithMaterial(new Material(texture));
        budgeted.Update(new Scene().Add(textured).Add(textured.At(2, 0, 0)), camera, 100, 100); // Shared texture and mesh: counted once.
        try { budgeted.Update(new Scene().Add(textured).Add(Shape.Box().WithMaterial(new Material(new Texture2D(64, 64, Enumerable.Repeat(0xFF808080u, 64 * 64))))), camera, 100, 100); Check(false, "over-resource-budget scene accepted"); }
        catch (GpuSceneException error) { Check(error.Message.Contains("MiB"), "resource budget message"); }
        budgeted.Clear();
        Check(budgeted.Items.Count == 0 && budgeted.Labels.Count == 0, "Clear must drop prepared items");
        Console.WriteLine("PASS GPU scene tolerates singular and overflowing transforms; budgets are scene errors");
    }
    private static float R(Random random) => .2f + (float)random.NextDouble();
    private static bool AllFinite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M21) && float.IsFinite(m.M22) &&
        float.IsFinite(m.M23) && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

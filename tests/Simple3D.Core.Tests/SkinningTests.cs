using System.Numerics;
using Simple3D.Core;

internal static class SkinningTests
{
    // A vertical two-joint bar: the lower half follows "root", the upper half follows "tip" at y = 1.
    internal static (SkinnedMesh Mesh, Skeleton Skeleton) Bar()
    {
        var skeleton = new Skeleton([
            new Joint("root", -1, Vector3.Zero, Quaternion.Identity, Vector3.One, Matrix4x4.Identity),
            new Joint("tip", 0, new(0, 1, 0), Quaternion.Identity, Vector3.One, Matrix4x4.CreateTranslation(0, -1, 0)),
        ]);
        var vertices = new List<Vector3>();
        var influences = new List<VertexInfluence>();
        for (var y = 0; y <= 2; y++)
            foreach (var x in new[] { -.1f, .1f })
                foreach (var z in new[] { -.1f, .1f })
                {
                    vertices.Add(new(x, y, z));
                    influences.Add(y == 0 ? new(new(0, 0, 0, 0), Vector4.UnitX)
                        : y == 1 ? new(new(0, 1, 0, 0), new(.5f, .5f, 0, 0)) : new(new(1, 0, 0, 0), Vector4.UnitX));
                }
        var indices = new List<int>();
        for (var level = 0; level < 2; level++)
        {
            var b = level * 4;
            // Four side quads between rings (x,z order: --, -+, +-, ++).
            foreach (var (p, q) in new[] { (0, 1), (1, 3), (3, 2), (2, 0) })
                indices.AddRange([b + p, b + q, b + 4 + q, b + p, b + 4 + q, b + 4 + p]);
        }
        var mesh = new Mesh(vertices, indices).WithCalculatedNormals();
        return (new SkinnedMesh(mesh, skeleton, influences), skeleton);
    }

    internal static void Run()
    {
        var (skinned, skeleton) = Bar();
        var rest = Shape.FromSkinnedMesh(skinned).Named("bar");
        Check(ReferenceEquals(rest.Pose, skeleton.RestPose) && rest.TriangleCount == skinned.Mesh.TriangleCount, "rest pose shape");
        var restTop = rest.Mesh.Max(t => MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y)));
        Check(MathF.Abs(restTop - 2) < 1e-5f, "rest pose must reproduce bind geometry");

        // Bend the tip 90° about Z: the top ring (x=±.1, y=2) moves to x=-1, y=1±.1.
        var bend = SkeletonPose.Create(skeleton, [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)]);
        var bent = rest.WithPose(bend);
        Check(ReferenceEquals(bent.SkinnedMesh, skinned) && bent.Name == "bar", "posed copy keeps mesh and name");
        var minX = bent.Mesh.Min(t => MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)));
        Check(MathF.Abs(minX + 1) < 1e-4f, $"tip must swing to x=-1, got {minX}");
        Check(bent.ShadingNormals is { } normals && normals.All(n => MathF.Abs(n.Length() - 1) < 1e-4f), "posed normals stay unit length");

        // Groups: WithPose reaches skinned descendants; transforms and other children are preserved.
        var group = Shape.Group(Shape.Box().Named("prop"), rest).At(3, 0, 0);
        var posedGroup = group.WithPose(bend);
        Check(posedGroup.Position.X == 3 && ReferenceEquals(posedGroup.Children[0], group.Children[0]) && ReferenceEquals(posedGroup.Children[1].Pose, bend), "group pose");
        Check(ReferenceEquals(posedGroup.WithPose(bend), posedGroup), "same pose is a no-op");
        try { Shape.Box().WithPose(bend); throw new Exception("unskinned pose accepted"); } catch (ArgumentException) { }

        // Conservative bounds enclose the exact posed vertices.
        var (lo, hi) = skinned.PosedBounds(bend);
        foreach (var t in bent.Mesh)
            foreach (var p in new[] { t.A, t.B, t.C })
                Check(p.X >= lo.X - 1e-4f && p.Y >= lo.Y - 1e-4f && p.X <= hi.X + 1e-4f && p.Y <= hi.Y + 1e-4f, "posed bounds");

        // Software rendering, bounds and picking use the posed geometry.
        var scene = new Scene().Add(bent);
        var bounds = scene.GetBounds()!.Value;
        Check(MathF.Abs(bounds.Min.X + 1) < 1e-4f, "scene bounds follow pose");
        var camera = new Camera(6, 0, 0) { Target = new(-.5f, 1, 0) };
        var frame = new DepthRenderer().Render(scene, camera, 120, 120);
        var tip = Project(camera, new(-.9f, 1, 0), 120, 120);
        Check(ReferenceEquals(frame.Pick((int)tip.X, (int)tip.Y), bent), "software render shows the posed tip");
        var restFrame = new DepthRenderer().Render(new Scene().Add(rest), camera, 120, 120);
        Check(restFrame.Pick((int)tip.X, (int)tip.Y) is null, "rest pose must not cover the posed tip");

        // Morph targets: offsets blend before skinning, survive pose changes and stay inside posed bounds.
        var lift = new MorphTarget("lift", skinned.Mesh.Vertices.Select(v => v.Y == 2 ? new Vector3(0, .5f, 0) : Vector3.Zero));
        var morphing = new SkinnedMesh(skinned.Mesh, skeleton, skinned.Influences, [lift]);
        var morphShape = Shape.FromSkinnedMesh(morphing);
        var lifted = morphShape.WithMorphWeights(morphing, [1f]);
        Check(MathF.Abs(lifted.Mesh.Max(t => MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y))) - 2.5f) < 1e-4f, "full morph weight lifts the top ring");
        Check(lifted.WithPose(bend).MorphWeights is [1f] && lifted.WithPose(bend).WithMorphWeights(morphing, [0f]).MorphWeights is null, "pose keeps weights; zero weights clear them");
        var (mlo, mhi) = morphing.PosedBounds(bend);
        foreach (var t in lifted.WithPose(bend).Mesh)
            foreach (var p in new[] { t.A, t.B, t.C })
                Check(p.X >= mlo.X - 1e-4f && p.Y >= mlo.Y - 1e-4f && p.X <= mhi.X + 1e-4f && p.Y <= mhi.Y + 1e-4f, "morphed posed bounds");
        try { morphShape.WithMorphWeights(morphing, [1.5f]); throw new Exception("weight above 1 accepted"); } catch (ArgumentOutOfRangeException) { }

        // Huge finite weights normalize instead of overflowing to zero.
        var huge = new SkinnedMesh(skinned.Mesh, skeleton, skinned.Influences.Select(i => i with { Weights = i.Weights * 3e38f }));
        Check(huge.Influences.All(i => MathF.Abs(i.Weights.X + i.Weights.Y + i.Weights.Z + i.Weights.W - 1) < 1e-5f), "overflow-safe weight normalization");
        Check(bend.WorldTransforms is not Matrix4x4[], "world transforms must not expose the cached array");
        // Validation.
        try { _ = new SkinnedMesh(skinned.Mesh, skeleton, skinned.Influences.Take(3)); throw new Exception("missing influences accepted"); } catch (ArgumentException) { }
        try { _ = new Skeleton([new Joint("a", 0, default, Quaternion.Identity, Vector3.One, Matrix4x4.Identity)]); throw new Exception("self parent accepted"); } catch (ArgumentException) { }
        try { _ = SkeletonPose.Create(skeleton, [Quaternion.Identity]); throw new Exception("short pose accepted"); } catch (ArgumentException) { }
        Console.WriteLine("PASS skinned meshes, poses, bounds and software rendering");
    }

    private static Vector2 Project(Camera camera, Vector3 point, int width, int height)
    {
        var frame = new DepthRenderer().Render(new Scene().AddLabel(new WorldLabel("p", point)), camera, width, height);
        return frame.Labels[0].Position;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

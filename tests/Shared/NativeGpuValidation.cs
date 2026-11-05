using System.Diagnostics;
using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared;

/// <param name="Selected">Shape to outline.</param>
/// <param name="ForceComposite">Render through the offscreen mask/outline composite even without a selection.</param>
/// <param name="OutlineRadius">Selection outline radius in render pixels.</param>
/// <param name="SkipReadback">Return no pixels: measures rendering alone. A default instance reads back.</param>
internal readonly record struct GpuProbeOptions(Shape? Selected = null, bool ForceComposite = false, int OutlineRadius = 1, bool SkipReadback = false);

internal readonly record struct GpuUploadCounts(int Geometry, int Textures, int Palettes, int PaletteBytes,
    int Deltas, long DeltaBytes, int Morphs, int MorphBytes);

// Executes the real native API and readback. Timing includes completion/readback and is
// intentionally distinct from the interactive view's CPU submission metric.
internal static class NativeGpuValidation
{
    internal const int Size = 256;
    internal const uint Background = 0xFF18243B;
    /// <param name="lastFrameDirect">Whether the renderer's last frame skipped the offscreen composite.</param>
    internal static void Run(string backend, Func<Scene, Camera, GpuProbeOptions, uint[]> render, Func<bool> lastFrameDirect,
        Func<GpuUploadCounts> uploads)
    {
        Console.WriteLine("GPU_PROBE_BACKEND: " + backend);
        var near = Shape.Box().WithMaterial(new Material(0xFFFF0000, lit: false)).At(0, 0, 1);
        var far = Shape.Box().WithMaterial(new Material(0xFF0000FF, lit: false));
        var scene = new Scene().Add(near).Add(far);
        var camera = new Camera(5, 0, 0);
        var pixels = render(scene, camera, default);
        if (!Valid(pixels))
        {
            var path = lastFrameDirect() ? "direct" : "composite";
            var composite = render(scene, camera, new(ForceComposite: true));
            throw new InvalidOperationException($"Native GPU depth, coverage or channel-order failure: {path} {Describe(pixels)}; forced composite {Describe(composite)}.");
        }
        var direct = lastFrameDirect();
        Console.WriteLine($"GPU_PROBE path without selection: {(direct ? "direct" : "composite")}");
        var selected = render(scene, camera, new(near));
        if (lastFrameDirect()) throw new InvalidOperationException("A visible selection must use the outline composite.");
        if (pixels.SequenceEqual(selected)) throw new InvalidOperationException("GPU selection missing.");
        // Wider outlines (high-density screens) must cover more pixels than the 1-pixel outline.
        var thin = Count(selected, OutlineColor);
        var thick = Count(render(scene, camera, new(near, OutlineRadius: 3)), OutlineColor);
        if (thin == 0 || thick <= thin) throw new InvalidOperationException($"Outline radius has no effect ({thin} vs {thick} outline pixels).");
        Console.WriteLine($"GPU_PROBE outline pixels radius1={thin} radius3={thick}");
        foreach (var sample in DemoScenes.All)
        {
            var actual = render(sample.Scene, sample.Camera, default);
            var expected = new DepthRenderer().Render(sample.Scene, sample.Camera, Size, Size, Background).Pixels.ToArray();
            var mismatches = Mismatches(actual, expected);
            if (mismatches > Size * Size * .03) throw new InvalidOperationException($"{sample.Name}: {mismatches} GPU pixels disagree with software.");
            // The single-pass and composite paths must produce the same image.
            var composite = render(sample.Scene, sample.Camera, new(ForceComposite: true));
            var pathDifferences = Mismatches(actual, composite);
            if (pathDifferences > Size * Size * .001) throw new InvalidOperationException($"{sample.Name}: direct and composite paths differ in {pathDifferences} pixels.");
            var times = new double[20];
            for (var i = 0; i < 3; i++) render(sample.Scene, sample.Camera, new(SkipReadback: true));
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < times.Length; i++)
            {
                sample.Camera.Orbit(.01f, 0);
                var start = Stopwatch.GetTimestamp(); render(sample.Scene, sample.Camera, new(SkipReadback: true));
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / times.Length;
            Array.Sort(times);
            Console.WriteLine($"GPU_PROBE {sample.Name}: {mismatches} mismatches vs software, {pathDifferences} direct/composite differences; completed median={times[10]:F3}ms p95={times[18]:F3}ms; {allocated}B managed/frame (prepare+render) at {Size}x{Size}; not displayed FPS");
        }
        Skinning(render, uploads, 2);
        Skinning(render, uploads, Skeleton.MaximumJoints);
        Morphing(render, uploads);
        if (render(new Scene(), camera, default).Any(pixel => pixel != Background)) throw new InvalidOperationException("GPU empty scene retained pixels.");
        Console.WriteLine("NATIVE_GPU_PROBE_PASS");
    }
    private static void Skinning(Func<Scene, Camera, GpuProbeOptions, uint[]> render,
        Func<GpuUploadCounts> uploads, int jointCount)
    {
        var mesh = SkinnedBar(jointCount);
        var rotations = Enumerable.Repeat(Quaternion.Identity, jointCount).ToArray();
        rotations[^1] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 3);
        if (jointCount > 2) rotations[^2] = rotations[^1];
        var pose = SkeletonPose.Create(mesh.Skeleton, rotations, new(.1f, .05f, .2f));
        var texture = new Texture2D(2, 2, new uint[] { 0xFFFFE0A0, 0xFF90C0FF, 0xFF90C0FF, 0xFFFFE0A0 }, TextureFilter.Bilinear);
        var rest = Shape.FromSkinnedMesh(mesh).WithMaterial(new Material(texture, doubleSided: false)).At(.65f, 0, 0).Scaled(.8f, 1, 1.2f);
        var bent = rest.WithPose(pose);
        var other = rest.At(-.65f, 0, -.2f);
        var scene = new Scene().Add(rest).Add(other).Add(Shape.Box().Scaled(.15f).At(0, -.2f, 0));
        var camera = new Camera(5, .12f, .08f) { Target = new(0, .9f, 0) };
        Shape current = rest;
        uint[]? restPixels = null;
        foreach (var shape in new[] { rest, bent })
        {
            scene.Replace(current, shape); current = shape;
            var actual = render(scene, camera, default);
            if (shape == bent) CheckUploads();
            var expected = new DepthRenderer().Render(scene, camera, Size, Size, Background).Pixels.ToArray();
            var differences = Mismatches(actual, expected);
            if (differences > Size * Size * .03) throw new InvalidOperationException($"Skinned {jointCount} joints: {differences} pixels disagree with software.");
            if (actual.Count(p => p != Background) < 500) throw new InvalidOperationException("Skinned scene has insufficient coverage.");
            var composite = render(scene, camera, new(ForceComposite: true));
            if (Mismatches(actual, composite) > Size * Size * .001) throw new InvalidOperationException("Skinned direct/composite paths differ.");
            var selected = render(scene, camera, new(shape));
            if (Count(selected, OutlineColor) == 0 || selected.SequenceEqual(actual)) throw new InvalidOperationException("Skinned selection mask missing.");
            if (shape == rest) restPixels = actual;
            else if (Mismatches(restPixels!, actual) < 500) throw new InvalidOperationException("Pose change did not move skinned geometry.");
            Console.WriteLine($"PASS GPU skinning {jointCount} joints {(shape == rest ? "rest" : "bent")}: {differences} mismatches; direct/composite and selection");
        }
        // Prebuilt poses isolate preparation/upload costs from application animation allocation.
        var times = new double[20];
        for (var i = 0; i < 3; i++) render(scene, camera, new(SkipReadback: true));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < times.Length; i++)
        {
            var next = i % 2 == 0 ? rest : bent;
            scene.Replace(current, next); current = next;
            var start = Stopwatch.GetTimestamp(); render(scene, camera, new(SkipReadback: true));
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            CheckUploads();
        }
        allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / times.Length;
        Array.Sort(times);
        Console.WriteLine($"PASS GPU skinning pose-only reuse: {jointCount} joints, 0 vertex/texture uploads; {uploads().Palettes} palettes/{uploads().PaletteBytes}B per frame");
        Console.WriteLine($"GPU_PROBE skinning {jointCount} joints: completed median={times[10]:F3}ms p95={times[18]:F3}ms; {allocated}B managed/frame (pose replacement+prepare+render, prebuilt poses, no readback); not isolated upload time or displayed FPS");
        void CheckUploads()
        {
            var counts = uploads();
            if (counts.Geometry != 0 || counts.Textures != 0 || counts.Palettes != 2 || counts.PaletteBytes != jointCount * 64 * 2)
                throw new InvalidOperationException($"Pose-only resource uploads: {counts}.");
        }
    }
    private static void Morphing(Func<Scene, Camera, GpuProbeOptions, uint[]> render, Func<GpuUploadCounts> uploads)
    {
        var bar = SkinnedBar(2);
        var targets = Enumerable.Range(0, 10).Select(t => new MorphTarget($"target{t}",
            bar.Mesh.Vertices.Select(p => new Vector3(.04f * (1 + p.Y) * (t % 2 == 0 ? 1 : -.4f), .01f * p.Y, .004f * t)),
            bar.Mesh.Vertices.Select(p => new Vector3(.015f * t, -.01f, .02f)))).ToArray();
        var mesh = new SkinnedMesh(bar.Mesh, bar.Skeleton, bar.Influences, targets);
        var pose = SkeletonPose.Create(mesh.Skeleton, [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .8f)]);
        var texture = new Texture2D(2, 2, new uint[] { 0xFFFFE0A0, 0xFF90C0FF, 0xFF90C0FF, 0xFFFFE0A0 });
        var rest = Shape.FromSkinnedMesh(mesh, new Material(texture)).WithPose(pose).At(.6f, 0, 0);
        var scene = new Scene().Add(rest).Add(rest.At(-.8f, 0, -.3f)).Add(Shape.Box().Scaled(.15f).At(0, -.2f, 0));
        var camera = new Camera(5, .12f, .08f) { Target = new(0, .9f, 0) };
        var current = rest;
        uint[]? previous = null;
        foreach (var weight in new[] { 0f, .5f, 1f, 0f })
        {
            var shape = rest.WithMorphWeights(mesh, Enumerable.Range(0, 10).Select(t => t < 2 ? 0 : weight).ToArray());
            scene.Replace(current, shape); current = shape;
            var actual = render(scene, camera, default);
            var counts = uploads();
            if (previous is null)
            {
                if (counts.Deltas != 1 || counts.DeltaBytes < targets.Length * mesh.Mesh.Vertices.Count * 32L)
                    throw new InvalidOperationException($"Initial morph delta upload: {counts}.");
            }
            else if (counts.Geometry != 0 || counts.Textures != 0 || counts.Deltas != 0 || counts.DeltaBytes != 0)
                throw new InvalidOperationException($"Weight-only resource uploads: {counts}.");
            if (counts.Morphs != 2 || counts.MorphBytes != 160)
                throw new InvalidOperationException($"Morph constants: {counts}.");
            var expected = new DepthRenderer().Render(scene, camera, Size, Size, Background).Pixels.ToArray();
            var differences = Mismatches(actual, expected);
            if (differences > Size * Size * .03) throw new InvalidOperationException($"Morph {weight}: {differences} GPU pixels disagree with software.");
            if (previous is not null && Mismatches(previous, actual) < 100) throw new InvalidOperationException("Morph weight did not visibly move geometry.");
            var composite = render(scene, camera, new(ForceComposite: true));
            if (Mismatches(actual, composite) > Size * Size * .001) throw new InvalidOperationException("Morph direct/composite paths differ.");
            if (Count(render(scene, camera, new(shape)), OutlineColor) == 0) throw new InvalidOperationException("Morph selection mask missing.");
            Console.WriteLine($"PASS GPU morph weight={weight:0.0} bent pose: {differences} mismatches; direct/composite and selection");
            previous = actual;
        }
        Console.WriteLine("PASS GPU morph weight-only reuse: 0 vertex/texture/delta uploads; 2 morph blocks/160B per frame");
        var cancel = new SkinnedMesh(bar.Mesh, bar.Skeleton, bar.Influences,
            [new MorphTarget("cancel-normal", bar.Mesh.Vertices.Select(_ => Vector3.Zero), bar.Mesh.Normals!.Select(n => -n))]);
        var cancelled = Shape.FromSkinnedMesh(cancel).WithPose(pose).WithMorphWeights(cancel, [1f]).At(1.2f, 0, 0);
        scene.Clear();
        scene.Add(current.At(-1.2f, 0, 0)).Add(cancelled).Add(Shape.FromSkinnedMesh(bar).WithPose(pose));
        var mixed = render(scene, camera, default);
        var mixedExpected = new DepthRenderer().Render(scene, camera, Size, Size, Background).Pixels.ToArray();
        var mixedDifferences = Mismatches(mixed, mixedExpected);
        if (mixedDifferences > Size * Size * .03) throw new InvalidOperationException($"Mixed morph meshes and cancelled normals: {mixedDifferences} pixels disagree.");
        render(scene, camera, default);
        if (uploads().Deltas != 0 || uploads().Geometry != 0) throw new InvalidOperationException("Mixed morph meshes did not retain their uploads.");
        Console.WriteLine($"PASS GPU morph mixed meshes and cancelled normals: {mixedDifferences} mismatches; cached deltas");
    }
    private static SkinnedMesh SkinnedBar(int jointCount)
    {
        // Same two-ring segments as the portable bar; the large palette exercises joint 127 and all four slots.
        var joints = new Joint[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            var tip = i == jointCount - 1 || (jointCount > 2 && i == jointCount - 2);
            joints[i] = new($"joint{i}", i == 0 ? -1 : 0, tip ? Vector3.UnitY : Vector3.Zero,
                Quaternion.Identity, Vector3.One, tip ? Matrix4x4.CreateTranslation(0, -1, 0) : Matrix4x4.Identity);
        }
        var vertices = new List<Vector3>(); var influences = new List<VertexInfluence>();
        for (var y = 0; y <= 2; y++)
            foreach (var x in new[] { -.18f, .18f })
                foreach (var z in new[] { -.18f, .18f })
                {
                    vertices.Add(new(x, y, z));
                    influences.Add(y == 0 ? new(new(0, 0, 0, 0), Vector4.UnitX)
                        : y == 2 ? new(new(jointCount - 1, 0, 0, 0), Vector4.UnitX)
                        : jointCount == 2 ? new(new(0, 1, 0, 0), new(.5f, .5f, 0, 0))
                        : new(new(0, 1, jointCount - 2, jointCount - 1), new(.25f)));
                }
        var indices = new List<int>();
        for (var level = 0; level < 2; level++)
        {
            var b = level * 4;
            foreach (var (p, q) in new[] { (0, 1), (1, 3), (3, 2), (2, 0) })
                indices.AddRange([b + p, b + q, b + 4 + q, b + p, b + 4 + q, b + 4 + p]);
        }
        var geometry = new Mesh(vertices, indices, vertices.Select(p => new Vector2((p.X + .18f) / .36f, p.Y / 2)),
            colors: vertices.Select(p => p.Y == 1 ? 0xFFC0E0FFu : 0xFFFFFFFFu)).WithCalculatedNormals();
        return new(geometry, new Skeleton(joints), influences);
    }
    private static bool Valid(uint[] pixels) => pixels.Length == Size * Size && pixels[Size / 2 * Size + Size / 2] == 0xFFFF0000 && pixels[0] == Background;
    private static string Describe(uint[] pixels) => pixels.Length == Size * Size
        ? $"center 0x{pixels[Size / 2 * Size + Size / 2]:X8} corner 0x{pixels[0]:X8} ({(Valid(pixels) ? "valid" : "invalid")})" : $"{pixels.Length} pixels";
    private const uint OutlineColor = 0xFF4CB2FF;
    private static int Count(uint[] pixels, uint color) => pixels.Count(p => !Different(p, color));
    /// <summary>Pixels differing by more than 4 in any channel: raster edge rules and lighting rounding differ between renderers.</summary>
    internal static int Mismatches(ReadOnlySpan<uint> actual, ReadOnlySpan<uint> expected)
    {
        var count = 0;
        for (var i = 0; i < actual.Length; i++) if (Different(actual[i], expected[i])) count++;
        return count;
    }
    private static bool Different(uint a, uint b) =>
        Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) > 4 ||
        Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) > 4 || Math.Abs((int)(a & 255) - (int)(b & 255)) > 4;
}

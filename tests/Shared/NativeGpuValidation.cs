using System.Diagnostics;
using Simple3D.Core;
using Simple3D.Shared;

/// <param name="Selected">Shape to outline.</param>
/// <param name="ForceComposite">Render through the offscreen mask/outline composite even without a selection.</param>
/// <param name="OutlineRadius">Selection outline radius in render pixels.</param>
/// <param name="SkipReadback">Return no pixels: measures rendering alone. A default instance reads back.</param>
internal readonly record struct GpuProbeOptions(Shape? Selected = null, bool ForceComposite = false, int OutlineRadius = 1, bool SkipReadback = false);

// Executes the real native API and readback. Timing includes completion/readback and is
// intentionally distinct from the interactive view's CPU submission metric.
internal static class NativeGpuValidation
{
    internal const int Size = 256;
    internal const uint Background = 0xFF18243B;
    /// <param name="lastFrameDirect">Whether the renderer's last frame skipped the offscreen composite.</param>
    internal static void Run(string backend, Func<Scene, Camera, GpuProbeOptions, uint[]> render, Func<bool> lastFrameDirect)
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
        if (render(new Scene(), camera, default).Any(pixel => pixel != Background)) throw new InvalidOperationException("GPU empty scene retained pixels.");
        Console.WriteLine("NATIVE_GPU_PROBE_PASS");
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

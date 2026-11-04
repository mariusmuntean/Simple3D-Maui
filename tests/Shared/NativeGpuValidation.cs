using System.Diagnostics;
using Simple3D.Core;
using Simple3D.Shared;

// Executes the real native API and readback. Timing includes completion/readback and is
// intentionally distinct from the interactive view's CPU submission metric.
internal static class NativeGpuValidation
{
    internal const int Size = 256;
    internal const uint Background = 0xFF18243B;
    internal static void Run(string backend, Func<Scene, Camera, Shape?, uint[]> render)
    {
        Console.WriteLine("GPU_PROBE_BACKEND: " + backend);
        var near = Shape.Box().WithMaterial(new Material(0xFFFF0000, lit: false)).At(0, 0, 1);
        var far = Shape.Box().WithMaterial(new Material(0xFF0000FF, lit: false));
        var scene = new Scene().Add(near).Add(far);
        var camera = new Camera(5, 0, 0);
        var pixels = render(scene, camera, null);
        if (pixels.Length != Size * Size || pixels[Size / 2 * Size + Size / 2] != 0xFFFF0000 || pixels[0] != Background)
            throw new InvalidOperationException("Native GPU depth, coverage or channel-order failure.");
        if (pixels.SequenceEqual(render(scene, camera, near))) throw new InvalidOperationException("GPU selection missing.");
        foreach (var sample in DemoScenes.All)
        {
            var actual = render(sample.Scene, sample.Camera, null);
            var expected = new DepthRenderer().Render(sample.Scene, sample.Camera, Size, Size, Background).Pixels.ToArray();
            var mismatches = actual.Zip(expected).Count(pair => Different(pair.First, pair.Second));
            if (mismatches > Size * Size * .03) throw new InvalidOperationException($"{sample.Name}: {mismatches} GPU pixels disagree with software.");
            var times = new double[20];
            for (var i = 0; i < 3; i++) render(sample.Scene, sample.Camera, null);
            for (var i = 0; i < times.Length; i++)
            {
                sample.Camera.Orbit(.01f, 0);
                var start = Stopwatch.GetTimestamp(); render(sample.Scene, sample.Camera, null);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            Array.Sort(times);
            Console.WriteLine($"GPU_PROBE {sample.Name}: {mismatches} mismatches; completed+readback median={times[10]:F3}ms p95={times[18]:F3}ms at {Size}x{Size}; not displayed FPS");
        }
        if (render(new Scene(), camera, null).Any(pixel => pixel != Background)) throw new InvalidOperationException("GPU empty scene retained pixels.");
        Console.WriteLine("NATIVE_GPU_PROBE_PASS");
    }
    private static bool Different(uint a, uint b) =>
        Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) > 4 ||
        Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) > 4 || Math.Abs((int)(a & 255) - (int)(b & 255)) > 4;
}

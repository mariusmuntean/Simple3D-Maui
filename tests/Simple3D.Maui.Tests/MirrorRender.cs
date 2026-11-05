using System.IO.Compression;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using SkiaSharp;
using System.Diagnostics;

/// <summary>Renders the Mirror character driven by recorded landmarks, for visual review next to the source video.</summary>
/// <remarks><c>dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-mirror out [every-nth-frame]</c>
/// writes <c>frame-NNNN.png</c> for every nth analyzed frame plus <c>timeline.txt</c> with tracking states.</remarks>
internal static class MirrorRender
{
    internal static int Run(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "mirror-render";
        var every = args.Length > 1 ? int.Parse(args[1]) : 5;
        Directory.CreateDirectory(output);
        using var asset = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "froggy.vrm"));
        var character = GltfLoader.Load(asset);
        var session = new MirrorSession(character.Shape, character.Model.Skeletons[0], character.Model.HumanoidBones, facesPositiveZ: false);
        var renderer = new DepthRenderer();
        var gpu = new GpuScene();
        var timeline = new List<string>();
        var (pose, prepare, render) = (new List<double>(), new List<double>(), new List<double>());
        var index = 0;
        foreach (var frame in Frames())
        {
            var started = Stopwatch.GetTimestamp();
            if (session.Apply(frame, mirrored: false)) pose.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            session.Stage.Animate((float)frame.Timestamp.TotalSeconds);
            timeline.Add($"{index} {frame.Timestamp.TotalSeconds:0.00} {session.Tracker.State}");
            // What a GPU frame costs on the CPU: scene preparation with joint palettes, no skinning.
            started = Stopwatch.GetTimestamp();
            gpu.Update(session.Stage.Scene, session.Stage.Camera, 720, 1280);
            prepare.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (index++ % every != 0) continue;
            started = Stopwatch.GetTimestamp();
            var image = renderer.Render(session.Stage.Scene, session.Stage.Camera, 360, 640, 0xFF0E1A2B);
            render.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(Path.Combine(output, $"frame-{index - 1:0000}.png"), png.ToArray());
        }
        File.WriteAllLines(Path.Combine(output, "timeline.txt"), timeline);
        Console.WriteLine($"MIRROR_RENDER frames={index} images={render.Count} output={Path.GetFullPath(output)}");
        Console.WriteLine($"MIRROR_PERF track+retarget+pose median={Median(pose):0.000}ms p95={P95(pose):0.000}ms; GPU scene prepare 720x1280 median={Median(prepare):0.000}ms; " +
            $"software render (CPU skinning + raster) 360x640 median={Median(render):0.0}ms; CPU only, not displayed FPS");
        return 0;
    }

    private static double Median(List<double> values) { values.Sort(); return values[values.Count / 2]; }
    private static double P95(List<double> values) { values.Sort(); return values[(int)(values.Count * .95)]; }

    private static IReadOnlyList<PoseFrame> Frames()
    {
        using var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mirror-landmarks.jsonl.gz")), CompressionMode.Decompress));
        return PoseRecording.Read(reader);
    }
}

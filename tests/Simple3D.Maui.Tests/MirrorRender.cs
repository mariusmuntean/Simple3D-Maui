using System.IO.Compression;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using SkiaSharp;
using System.Diagnostics;

/// <summary>Renders the Mirror character driven by recorded landmarks, for visual review next to the source video.</summary>
/// <remarks><c>dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-mirror out [every-nth-frame] [fixture-name]</c>
/// writes <c>frame-NNNN.png</c> for every nth analyzed frame plus <c>timeline.txt</c> with tracking states.</remarks>
internal static class MirrorRender
{
    internal static int Run(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "mirror-render";
        var every = args.Length > 1 ? int.Parse(args[1]) : 5;
        var fixture = args.Length > 2 ? args[2] : "mirror-landmarks";
        Directory.CreateDirectory(output);
        var character = Character("cat.vrm");
        Console.WriteLine($"MIRROR_CHARACTER faces {(character.FacesPositiveZ ? "+Z" : "-Z")}, torso radius {character.TorsoRadius:0.000}");
        var session = new MirrorSession(character);
        var renderer = new DepthRenderer();
        var gpu = new GpuScene();
        var timeline = new List<string>();
        var (pose, prepare, render) = (new List<double>(), new List<double>(), new List<double>());
        var index = 0;
        foreach (var frame in Frames(fixture))
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

    /// <summary>Renders a character swap at 60 frames per second while the fixture keeps the person tracked.</summary>
    internal static int Swap(string output)
    {
        Directory.CreateDirectory(output);
        var session = new MirrorSession(Character("cat.vrm"));
        var next = Character("froggy.vrm");
        var renderer = new DepthRenderer();
        var frames = Frames("mirror-landmarks");
        var swapped = false;
        var shot = 0;
        for (var i = 0; i < 400; i++)
        {
            var t = i / 60f;
            session.Apply(frames.Count > 0 ? PoseRecording.Nearest(frames, TimeSpan.FromSeconds(5 + t)) with { Timestamp = TimeSpan.FromSeconds(t) } : frames[0], mirrored: false);
            session.Stage.Animate(t);
            if (!swapped && t >= 2) { session.SwitchCharacter(next); swapped = true; }
            if (t < 1.95f || t > 3.0f || i % 3 != 0) continue;
            var image = renderer.Render(session.Stage.Scene, session.Stage.Camera, 270, 480, 0xFF0E1A2B);
            using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(Path.Combine(output, $"swap-{shot++:00}.png"), png.ToArray());
        }
        Console.WriteLine($"MIRROR_SWAP frames={shot} swapping={session.Stage.IsSwapping} output={Path.GetFullPath(output)}");
        return 0;
    }

    internal static MirrorCharacter Character(string file)
    {
        using var asset = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", file));
        var loaded = GltfLoader.Load(asset);
        return MirrorCharacter.FromVrm(loaded.Model, loaded.Shape);
    }

    private static IReadOnlyList<PoseFrame> Frames(string fixture)
    {
        using var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".jsonl.gz")), CompressionMode.Decompress));
        return PoseRecording.Read(reader);
    }
}

using System.Numerics;
using System.IO.Compression;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using SkiaSharp;
using System.Diagnostics;

/// <summary>Renders the Mirror character driven by recorded landmarks, for visual review next to the source video.</summary>
/// <remarks><c>dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-mirror out [every-nth-frame] [fixture-name] [model.vrm]</c>
/// writes <c>frame-NNNN.png</c> for every nth analyzed frame plus <c>timeline.txt</c> with tracking states.</remarks>
internal static class MirrorRender
{
    internal static int Run(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "mirror-render";
        var every = args.Length > 1 ? int.Parse(args[1]) : 5;
        var fixture = args.Length > 2 ? args[2] : "mirror-landmarks";
        Directory.CreateDirectory(output);
        var character = Character(args.Length > 3 ? args[3] : "cat.vrm");
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
            session.Present(TimeSpan.FromSeconds(1 / 25d));
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
            session.Present(TimeSpan.FromSeconds(1 / 60d));
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

    /// <summary>Renders a character in its rest pose on the stage, to check which way it faces.</summary>
    internal static int Rest(string output, string model, string? expression = null)
    {
        var character = Character(model);
        var stage = new MirrorStage(character);
        stage.ShowCharacter(true);
        if (expression is not null && character.Express is { } express)
            stage.Update(shape => express(shape, expression.Split('+').ToDictionary(e => e, _ => 1f)));
        // Close-up on the face.
        stage.Camera.Target = new Vector3(0, character.Height * .85f, 0);
        stage.Camera.Zoom(stage.Camera.Distance / 1.4f);
        var image = new DepthRenderer().Render(stage.Scene, stage.Camera, 360, 640, 0xFF0E1A2B);
        using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(output, png.ToArray());
        return 0;
    }

    /// <summary>Renders the showcase: three recorded performances, each driving a different character, with animated swaps.
    /// Writes frame-NNNN.png at 15 fps plus manifest.txt ("frame fixture seconds" per line) for the skeleton panel.</summary>
    internal static int Showcase(string output)
    {
        Directory.CreateDirectory(output);
        (string Fixture, double From, double To, string Model)[] scenes =
            [("mirror-landmarks", 5.2, 10.4, "cat.vrm"), ("face-46379", 2.6, 8.6, "juanita.vrm"), ("turning-landmarks", 6.0, 11.0, "weirdcat.vrm")];
        var renderer = new DepthRenderer();
        var session = new MirrorSession(Character(scenes[0].Model));
        var manifest = new List<string>();
        var index = 0;
        var clock = 0.0;
        foreach (var (fixture, from, to, model) in scenes)
        {
            var frames = Frames(fixture);
            if (index > 0) session.SwitchCharacter(Character(model));
            // Warm up so tracking and smoothing are settled when the scene starts.
            foreach (var frame in frames.Where(f => f.Timestamp.TotalSeconds < from && f.Timestamp.TotalSeconds > from - 1.5))
            {
                session.Apply(frame, mirrored: false);
                session.Present(TimeSpan.FromSeconds(.04));
            }
            for (var t = from; t < to; t += 1 / 15.0)
            {
                foreach (var frame in frames.Where(f => f.Timestamp.TotalSeconds > t - 1 / 15.0 && f.Timestamp.TotalSeconds <= t))
                    session.Apply(frame, mirrored: false);
                for (var i = 0; i < 4; i++) session.Present(TimeSpan.FromSeconds(1 / 60.0));
                clock += 1 / 15.0;
                session.Stage.Animate((float)clock);
                // The face scene eases in to head and shoulders once the swap has played, so the face reads at GIF size.
                if (fixture.StartsWith("face") && !session.Stage.IsSwapping)
                {
                    var camera = session.Stage.Camera;
                    camera.Target = Vector3.Lerp(camera.Target, new Vector3(0, session.Stage.Character.Height * .82f, 0), .15f);
                    camera.Zoom(MathF.Pow(camera.Distance / 1.7f, .15f));
                }
                var image = renderer.Render(session.Stage.Scene, session.Stage.Camera, 540, 720, 0xFF0E1A2B);
                using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
                using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(Path.Combine(output, $"frame-{index:0000}.png"), png.ToArray());
                manifest.Add(FormattableString.Invariant($"{index} {fixture} {t:0.000} {model}"));
                index++;
            }
        }
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), manifest);
        Console.WriteLine($"MIRROR_SHOWCASE frames={index} output={Path.GetFullPath(output)}");
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

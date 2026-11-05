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
            stage.Update(shape => express(shape, Intensities(expression)));
        FrameFace(stage, 1);
        var image = new DepthRenderer().Render(stage.Scene, stage.Camera, 480, 480, 0xFF0E1A2B);
        using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(output, png.ToArray());
        return 0;
    }

    // "a+o:.5" → a at 1, o at 0.5.
    private static Dictionary<string, float> Intensities(string spec) => spec.Split('+', StringSplitOptions.RemoveEmptyEntries)
        .Select(e => e.Split(':')).ToDictionary(p => p[0], p => p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 1f);

    /// <summary>Renders a row of labeled face close-ups. Without <paramref name="spec"/>, the tiles are what the character's
    /// <see cref="MirrorFace"/> table shows for a neutral face, a wide-open mouth, each eye closed, a smile and a frown; with
    /// it, each "label=a+o:.5" entry (separated by ';') renders those expressions directly.</summary>
    internal static int Faces(string output, string model, string? spec = null)
    {
        var character = Character(model);
        FaceFrame Face(float mouth = 0, float smile = 0, float brows = 0, float left = 0, float right = 0) => new(0, 0, 0, mouth, smile, brows, left, right);
        var tiles = new List<(string Label, IReadOnlyDictionary<string, float> Expressions)>();
        if (spec is null)
        {
            var face = character.Face;
            tiles.Add(("neutral", face.Expressions(Face(), false)));
            tiles.Add(("mouth open", face.Expressions(Face(mouth: 1), false)));
            // A model that can only close both eyes shows that once, for a blink.
            if (face.SeparateEyes) { tiles.Add(("blink left", face.Expressions(Face(left: 1), false))); tiles.Add(("blink right", face.Expressions(Face(right: 1), false))); }
            else tiles.Add(("blink", face.Expressions(Face(left: 1, right: 1), false)));
            tiles.Add(("smile", face.Expressions(Face(smile: 1), false)));
            tiles.Add(("frown", face.Expressions(Face(brows: -1), false)));
        }
        else
            foreach (var entry in spec.Split(';').Select(t => t.Split('=')))
                tiles.Add((entry[0], Intensities(entry.Length > 1 ? entry[1] : entry[0])));
        const int size = 300, label = 28;
        using var sheet = new SKBitmap(size * tiles.Count, size + label);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(0x0E, 0x1A, 0x2B));
        using var font = new SKFont(SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var renderer = new DepthRenderer();
        for (var i = 0; i < tiles.Count; i++)
        {
            var stage = new MirrorStage(character);
            stage.ShowCharacter(true);
            if (character.Express is { } express) stage.Update(shape => express(shape, tiles[i].Expressions));
            FrameFace(stage, 1);
            var image = renderer.Render(stage.Scene, stage.Camera, size, size, 0xFF0E1A2B);
            using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
            canvas.DrawBitmap(bitmap, i * size, label);
            canvas.DrawText(tiles[i].Label, i * size + size / 2f, 20, SKTextAlign.Center, font, paint);
        }
        using var png = sheet.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(output, png.ToArray());
        return 0;
    }

    /// <summary>Points the stage camera level at the whole head, top to chin and ear to ear, with a margin, for an image of the given aspect.</summary>
    internal static void FrameFace(MirrorStage stage, float aspect)
    {
        var character = stage.Character;
        // Head bounds relative to the body, which the stage scales to character.Height with the feet at the origin.
        var ((headMin, headMax), (bodyMin, bodyMax)) = character.HeadBounds() ?? throw new InvalidOperationException("The character has no head bone.");
        var scale = character.Height / (bodyMax.Y - bodyMin.Y);
        float bottom = (headMin.Y - bodyMin.Y) * scale, top = (headMax.Y - bodyMin.Y) * scale;
        float halfHeight = (top - bottom) / 2, halfWidth = (headMax.X - headMin.X) / 2 * scale, depth = (headMax.Z - headMin.Z) * scale;
        stage.Camera.Orbit(-stage.Camera.Yaw, -stage.Camera.Pitch);
        stage.Camera.Target = new Vector3(0, (bottom + top) / 2, 0);
        // Whiskers and wide ears may be cut: the face matters, so the width counts up to 1.3 times the height.
        var half = MathF.Max(halfHeight, MathF.Min(halfWidth, halfHeight * 1.3f) / aspect) * 1.1f;
        stage.Camera.Zoom(stage.Camera.Distance / (half / MathF.Tan(stage.Camera.FieldOfView / 2) + depth / 2));
    }

    /// <summary>Renders the showcase: three real people (public-domain NASA and CDC videos, see tests/Fixtures/SOURCES.md), each
    /// driving a different character, with animated swaps. Writes frame-NNNN.png at 15 fps plus manifest.txt ("frame fixture
    /// seconds model" per line), which scripts/mirror-showcase.py pairs with the matching video frames.</summary>
    internal static int Showcase(string output)
    {
        Directory.CreateDirectory(output);
        (string Fixture, double From, double To, string Model)[] scenes =
            [("face-nasa-menon", 3.8, 9.8, "juanita.vrm"), ("upper-cdc-press", 19.8, 24.8, "cosmicbot.vrm"), ("body-cdc-squat", 2.5, 7.5, "cat.vrm")];
        var renderer = new DepthRenderer();
        var session = new MirrorSession(Character(scenes[0].Model));
        var manifest = new List<string>();
        var index = 0;
        var clock = 0.0;
        // The body scenes ease back from the face close-up to the stage's own framing.
        var (stageTarget, stageDistance) = (session.Stage.Camera.Target, session.Stage.Camera.Distance);
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
                if (!session.Stage.IsSwapping)
                {
                    var face = fixture.StartsWith("face");
                    var camera = session.Stage.Camera;
                    camera.Target = Vector3.Lerp(camera.Target, face ? new Vector3(0, session.Stage.Character.Height * .82f, 0) : stageTarget, .15f);
                    camera.Zoom(MathF.Pow(camera.Distance / (face ? 1.7f : stageDistance * .8f), .15f));
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
        return MirrorCharacter.FromVrm(loaded.Model, loaded.Shape, MirrorModels.All.FirstOrDefault(m => m.File == file)?.Face);
    }

    internal static IReadOnlyList<PoseFrame> Frames(string fixture)
    {
        using var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".jsonl.gz")), CompressionMode.Decompress));
        return PoseRecording.Read(reader);
    }
}

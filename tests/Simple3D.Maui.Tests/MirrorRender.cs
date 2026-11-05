using System.IO.Compression;
using System.Text.Json;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using SkiaSharp;

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
        var stage = new MirrorStage(character.Shape, facesPositiveZ: false);
        var retargeter = new HumanoidRetargeter(character.Model.Skeletons[0], character.Model.HumanoidBones, stage.CharacterToView);
        var tracker = new PoseTracker();
        var renderer = new DepthRenderer();
        var timeline = new List<string>();
        tracker.StateChanged += state => timeline.Add($"{state}");
        var index = 0;
        var renderTimes = new List<double>();
        foreach (var frame in Frames())
        {
            if (tracker.Update(frame))
                stage.Pose(retargeter.Retarget(tracker.Landmarks, tracker.ImageAspect, mirrored: false));
            stage.ShowCharacter(tracker.State != TrackingState.Searching);
            stage.Animate((float)frame.Timestamp.TotalSeconds);
            timeline.Add($"{index} {frame.Timestamp.TotalSeconds:0.00} {tracker.State}");
            if (index++ % every != 0) continue;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var image = renderer.Render(stage.Scene, stage.Camera, 360, 640, 0xFF0E1A2B);
            renderTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(Path.Combine(output, $"frame-{index - 1:0000}.png"), png.ToArray());
        }
        File.WriteAllLines(Path.Combine(output, "timeline.txt"), timeline);
        renderTimes.Sort();
        Console.WriteLine($"MIRROR_RENDER frames={index} images={renderTimes.Count} median_render_ms={renderTimes[renderTimes.Count / 2]:0.0} output={Path.GetFullPath(output)}");
        return 0;
    }

    private static IEnumerable<PoseFrame> Frames()
    {
        using var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mirror-landmarks.jsonl.gz")), CompressionMode.Decompress));
        while (reader.ReadLine() is { } line)
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var landmarks = root.GetProperty("lm").ValueKind == JsonValueKind.Array
                ? root.GetProperty("lm").EnumerateArray().Select(p => new Landmark(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray()
                : null;
            yield return new(TimeSpan.FromSeconds(root.GetProperty("t").GetDouble()), root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(), landmarks);
        }
    }
}

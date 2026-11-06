using Simple3D.Core;
using Simple3D.Shared;
using SkiaSharp;

namespace Simple3D.Demo;

/// <summary>What the gallery says about each scene: the lesson tags, and thumbnails rendered from the scenes themselves.</summary>
internal static class SceneCatalog
{
    private static readonly Dictionary<string, (string Short, string Long)> Tags = new()
    {
        ["Equipment"] = ("01 · Primitives", "01 · Primitives & textures"),
        ["Telemetry"] = ("02 · Data", "02 · Data to geometry"),
        ["Surface"] = ("03 · Meshes", "03 · Meshes & shading"),
        ["Robot Arm"] = ("04 · Hierarchy", "04 · Hierarchy"),
        ["Gantry Crane"] = ("05 · Linked motion", "05 · Linked motion"),
    };

    public const string MirrorTag = "06 · Camera";
    public static string ShortTag(string scene) => Tags.TryGetValue(scene, out var tag) ? tag.Short : "";
    public static string LongTag(string scene) => Tags.TryGetValue(scene, out var tag) ? tag.Long : "";

    // Card images are 3:2; rendered at twice the pixels needed and scaled down, for smooth edges.
    private const int ThumbnailWidth = 540, ThumbnailHeight = 360;
    private static Task<Dictionary<string, byte[]>>? _thumbnails;

    /// <summary>A PNG of the scene's opening view, rendered in software once, off the UI thread.</summary>
    public static ImageSource Thumbnail(string scene) => ImageSource.FromStream(async _ =>
    {
        _thumbnails ??= Task.Run(RenderThumbnails);
        return (await _thumbnails).TryGetValue(scene, out var png) ? new MemoryStream(png) : null;
    });

    /// <summary>A Mirror character's thumbnail, rendered from its model and bundled (see docs/DEVELOPMENT.md).</summary>
    public static ImageSource Character(string file) =>
        ImageSource.FromStream(async _ => await FileSystem.OpenAppPackageFileAsync("mirror/" + Path.ChangeExtension(file, ".png")));

    private static Dictionary<string, byte[]> RenderThumbnails()
    {
        var renderer = new DepthRenderer();
        var background = (uint)Theme.SceneBackground.ToInt();
        var thumbnails = new Dictionary<string, byte[]>();
        foreach (var sample in DemoScenes.All)
        {
            sample.Animate(0);
            var camera = new Camera(sample.Camera.Distance, sample.Camera.Yaw, sample.Camera.Pitch)
            {
                Target = sample.Camera.Target, Projection = sample.Camera.Projection, FieldOfView = sample.Camera.FieldOfView,
                OrthographicHeight = sample.Camera.OrthographicHeight, NearPlane = sample.Camera.NearPlane
            };
            camera.FitToScene(sample.Scene, (float)ThumbnailWidth / ThumbnailHeight, 1.02f);
            var frame = renderer.Render(sample.Scene, camera, ThumbnailWidth * 2, ThumbnailHeight * 2, background);
            using var full = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            frame.Pixels.Span.CopyTo(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(full.GetPixelSpan()));
            using var small = full.Resize(new SKImageInfo(ThumbnailWidth, ThumbnailHeight, SKColorType.Bgra8888, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var data = small.Encode(SKEncodedImageFormat.Png, 100);
            thumbnails[sample.Name] = data.ToArray();
        }
        return thumbnails;
    }
}

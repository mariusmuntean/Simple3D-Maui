#if DEBUG || NATIVE_RENDER_PROBE
using System.Diagnostics;
using System.Numerics;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;
using SkiaSharp;

namespace Simple3D.Demo;

// Opt-in diagnostic runs inside the native runtime, then exits without starting a gallery session.
internal static class NativeRenderProbe
{
    public static void RunIfRequested()
    {
        if (Environment.GetEnvironmentVariable("SIMPLE3D_RENDER_PROBE") != "1") return;
        var renderer = new DepthRenderer();
        var target = new RenderTarget(512, 315);
        var boxScene = new Scene().Add(Shape.Box());
        renderer.RenderInto(boxScene, new Camera(5), target);
        CheckRendered("Box", target);
        if (!ReferenceEquals(target.Pick(256, 157), boxScene.Shapes[0]))
            throw new InvalidOperationException("Native box picking failed.");
        CheckAppearance(renderer);

        foreach (var sample in DemoScenes.All)
        {
            renderer.RenderInto(sample.Scene, sample.Camera, target);
            CheckRendered(sample.Name, target);
            var owned = renderer.Render(sample.Scene, sample.Camera, target.Width, target.Height);
            var original = owned.Pixels.ToArray();
            if (!owned.Pixels.Span.SequenceEqual(target.Pixels.Span))
                throw new InvalidOperationException($"{sample.Name}: owned and reusable pixels differ.");
            for (var i = 0; i < 2; i++) renderer.RenderInto(sample.Scene, sample.Camera, target);
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 10; i++)
            {
                sample.Camera.Orbit(.024f, .012f);
                renderer.RenderInto(sample.Scene, sample.Camera, target);
            }
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 10;
            CheckRendered(sample.Name, target);
            if (!owned.Pixels.Span.SequenceEqual(original))
                throw new InvalidOperationException($"{sample.Name}: retained snapshot was overwritten.");
            Console.WriteLine($"RENDER_PROBE {sample.Name}: {elapsed:F2} ms/frame; {target.RasterSamples} samples");
        }
        Console.WriteLine("NATIVE_RENDER_PROBE_PASS");
        Environment.Exit(0);
    }

    private static void CheckAppearance(DepthRenderer renderer)
    {
        // Exercise imported attributes and clipping under the platform runtime, including image codecs.
        using var bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, new SKColor(171, 205, 239));
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = encoded.AsStream();
        var texture = TextureLoader.Load(stream);
        var mesh = ObjReader.Parse("""
            v -2 -1 4.8
            v 2 -1 -1
            v 0 2 0
            vt 0 0
            vt 1 0
            vt 0.3 1
            vn 0 0 1
            f 1/1/1 2/2/1 3/3/1
            """);
        var shape = Shape.FromMesh(mesh, new Material(texture, lit: false));
        var scene = new Scene().Add(shape);
        var camera = new Camera(5, 0, 0) { NearPlane = .5f };
        var target = new RenderTarget(40, 40);
        renderer.RenderInto(scene, camera, target);
        var count = 0;
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
                if (ReferenceEquals(target.Pick(x, y), shape))
                {
                    if (target.Pixels.Span[y * 40 + x] != 0xFFABCDEF)
                        throw new InvalidOperationException("Native clipped texture changed its constant color.");
                    count++;
                }
        if (count < 100) throw new InvalidOperationException("Native clipped texture has insufficient coverage.");
        var colored = new Mesh(mesh.Vertices, mesh.Indices, mesh.TextureCoordinates, mesh.Normals,
            [0xFFFF0000, 0xFF00FF00, 0xFF0000FF]);
        shape = Shape.FromMesh(colored, new Material(0xFFFFFFFF));
        scene = new Scene { AmbientLight = 0, Light = new(-Vector3.UnitZ) };
        scene.Add(shape);
        renderer.RenderInto(scene, camera, target);
        CheckRendered("Clipped vertex colors and normals", target);
        var center = target.Pixels.Span[20 * 40 + 20];
        if ((center & 255) == 0 || ((center >> 8) & 255) == 0 || ((center >> 16) & 255) == 0)
            throw new InvalidOperationException("Native vertex colors or directional smooth light were lost.");
        Console.WriteLine($"APPEARANCE_PROBE_PASS: decoded texture, imported UV/normals, clipped colors; {count} pixels");
    }

    private static void CheckRendered(string name, RenderTarget target)
    {
        if (target.RasterSamples == 0) throw new InvalidOperationException($"{name}: no raster work.");
        var pixels = target.Pixels.Span;
        for (var i = 0; i < pixels.Length; i++)
            if (pixels[i] != 0xFFF4F6FAu)
            {
                if (target.Pick(i % target.Width, i / target.Width) is null)
                    throw new InvalidOperationException($"{name}: visible pixel has no pick.");
                return;
            }
        throw new InvalidOperationException($"{name}: no visible geometry.");
    }
}
#endif

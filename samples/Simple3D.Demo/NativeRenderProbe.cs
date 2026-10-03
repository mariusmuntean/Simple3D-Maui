#if DEBUG
using System.Diagnostics;
using Simple3D.Core;
using Simple3D.Shared;

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

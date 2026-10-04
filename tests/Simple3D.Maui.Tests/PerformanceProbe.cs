using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;

internal static class PerformanceProbe
{
    // Measure selected animation plus orbit with edge filtering at the drag preview size.
    // This excludes native presentation and does not measure displayed FPS.
    public static int Run()
    {
        var rows = new List<object>();
        foreach (var sample in DemoScenes.RegressionScenes)
        {
            sample.Animate(0);
            var roots = sample.Scene.Shapes.ToArray();
            sample.Animate(.1f);
            var moving = Enumerable.Range(0, roots.Length).Single(i => !ReferenceEquals(roots[i], sample.Scene.Shapes[i]));
            sample.Animate(0);
            var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera, MaximumRenderDimension = 768 };
            var target = view.CapturePaintTarget(410, 512);
            var leaves = new HashSet<Shape>();
            Visit(sample.Scene.Shapes[moving]);
            var point = Enumerable.Range(0, target.Width * target.Height).First(i =>
                target.Pick(i % target.Width, i / target.Width) is Shape shape && leaves.Contains(shape));
            view.SelectAt(point % target.Width, point / target.Width, target.Width, target.Height);
            view.ApplyPan(GestureStatus.Started, 0, 0);
            var rounds = new List<double>();
            long allocation = 0;
            for (var round = -1; round < 5; round++)
            {
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var started = Stopwatch.GetTimestamp();
                for (var frame = 0; frame < 80; frame++)
                {
                    sample.Animate(frame / 60f);
                    // Repeat the same orbit and its inverse, keeping the pose comparable.
                    view.ApplyPan(GestureStatus.Running, frame % 2 == 0 ? 2 : 0, frame % 2 == 0 ? 1 : 0);
                    target = view.CapturePaintTarget(410, 512);
                    view.PaintBitmap(target);
                }
                if (round >= 0)
                {
                    rounds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds / 80);
                    allocation += (GC.GetAllocatedBytesForCurrentThread() - bytes) / 80;
                }
            }
            var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(target.Pixels.Span)));
            rows.Add(new { sample.Name, MedianMs = rounds.Order().ElementAt(2), AllocatedBytes = allocation / 5, PixelHash = hash });
            view.ReleaseRenderResources();
            void Visit(Shape shape)
            {
                if (shape.Mesh.Count > 0) leaves.Add(shape);
                foreach (var child in shape.Children) Visit(child);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}

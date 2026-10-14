using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Simple3D.Core;
using Simple3D.Maui;
using SkiaSharp;

// Opt-in, fixed-input CPU measurements shared by the host and native runtime probes.
internal static class AppearancePerformanceProbe
{
    public static void Run()
    {
        foreach (var filter in new[] { TextureFilter.Nearest, TextureFilter.Bilinear })
        {
            var pixels = Enumerable.Range(0, 64 * 64).Select(i =>
                0xFF000000u | (uint)((i % 64) * 4) << 16 | (uint)((i / 64) * 4) << 8 | (uint)((i % 7) * 36)).ToArray();
            var texture = new Texture2D(64, 64, pixels, filter, TextureAddressMode.Repeat);
            var shape = Shape.Sphere().WithMaterial(new Material(texture));
            Measure($"Textured {filter}", shape);
        }
        var geometry = Shape.Sphere().WithMaterial(new Material(new Texture2D(1, 1, [0xFFFFFFFF]))).Geometry!;
        var colors = geometry.Vertices.Select(p => 0xFF000000u | (uint)((p.X + 1) * 127) << 16 |
            (uint)((p.Y + 1) * 127) << 8 | (uint)((p.Z + 1) * 127)).ToArray();
        var smooth = new Mesh(geometry.Vertices, geometry.Indices, colors: colors).WithCalculatedNormals();
        Measure("Smooth vertex colors", Shape.FromMesh(smooth));

        using var bitmap = new SKBitmap(1024, 1024);
        bitmap.Erase(new SKColor(18, 52, 86));
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = encoded.AsStream();
        for (var i = 0; i < 3; i++) { stream.Position = 0; TextureLoader.Load(stream); }
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        stream.Position = 0;
        var loaded = TextureLoader.Load(stream);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(loaded.Pixels.ToArray().AsSpan())));
        Console.WriteLine("TEXTURE_LOAD_PERFORMANCE " + JsonSerializer.Serialize(new
            { Width = 1024, Height = 1024, Ms = elapsed, AllocatedBytes = allocated, PixelHash = hash }));
    }

    private static void Measure(string name, Shape shape)
    {
        var scene = new Scene().Add(shape.Scaled(1.5f)).Add(shape.Scaled(.7f).At(1.5f, .6f, -.5f));
        var camera = new Camera(5, .3f, .2f);
        var renderer = new DepthRenderer();
        var target = new RenderTarget(400, 300);
        for (var i = 0; i < 100; i++) renderer.RenderInto(scene, camera, target);
        var rounds = new double[5];
        long allocation = 0;
        for (var round = 0; round < rounds.Length; round++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 100; i++) renderer.RenderInto(scene, camera, target);
            rounds[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 100;
            allocation += (GC.GetAllocatedBytesForCurrentThread() - bytes) / 100;
        }
        var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(target.Pixels.Span)));
        Console.WriteLine("APPEARANCE_PERFORMANCE " + JsonSerializer.Serialize(new
            { Name = name, Width = 400, Height = 300, Triangles = scene.Shapes.Sum(s => s.TriangleCount),
              MedianMs = rounds.Order().ElementAt(2), AllocatedBytes = allocation / rounds.Length, PixelHash = hash }));
    }
}

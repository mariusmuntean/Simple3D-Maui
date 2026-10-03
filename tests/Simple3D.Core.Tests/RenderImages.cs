using System.Text;
using Simple3D.Core;
using Simple3D.Shared;

internal static class RenderImages
{
    internal static int Run(string[] args)
    {
        var scenes = DemoScenes.All;
        if (args.Length != 2 || (args[0] != "--all" && !scenes.Any(s => s.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase))))
        {
            Console.Error.WriteLine("Usage: --render-images (--all | SCENE_NAME) OUTPUT_PATH");
            return 2;
        }
        var selected = args[0] == "--all" ? scenes : scenes.Where(s => s.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        if (args[0] == "--all") Directory.CreateDirectory(args[1]);
        var renderer = new DepthRenderer();
        foreach (var sample in selected)
        {
            var path = args[0] == "--all" ? Path.Combine(args[1], sample.Name + ".ppm") : args[1];
            var frame = renderer.Render(sample.Scene, sample.Camera, 800, 600, 0xFF18243B);
            var pixels = frame.Pixels.Span;
            var rgb = new byte[pixels.Length * 3];
            for (var i = 0; i < pixels.Length; i++)
            {
                rgb[i * 3] = (byte)(pixels[i] >> 16);
                rgb[i * 3 + 1] = (byte)(pixels[i] >> 8);
                rgb[i * 3 + 2] = (byte)pixels[i];
            }
            using var output = File.Create(path);
            output.Write(Encoding.ASCII.GetBytes($"P6\n{frame.Width} {frame.Height}\n255\n"));
            output.Write(rgb);
            Console.WriteLine($"{sample.Name}: {path}");
        }
        return 0;
    }
}

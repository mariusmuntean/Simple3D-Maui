using Simple3D.Core;

internal static class TextureStorageTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("public texture construction copies a bounded iterator into one pixel buffer", () =>
        {
            const int size = 256;
            _ = new Texture2D(1, 1, Pixels(1));
            var start = GC.GetAllocatedBytesForCurrentThread();
            var texture = new Texture2D(size, size, Pixels(size * size));
            var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(bytes < size * size * 4 + 32_768, $"texture construction allocated {bytes} bytes");
            Check(texture.Pixels.Count == size * size && texture.Pixels[^1] == 0xFF123456, "iterator pixels changed");
            var visits = 0;
            IEnumerable<uint> Infinite()
            {
                while (true) { visits++; yield return 0xFFFFFFFF; }
            }
            Throws<ArgumentException>(() => new Texture2D(2, 2, Infinite()));
            Check(visits == 5, "overlong iterator was not bounded to one extra pixel");
        }),
        ("public texture pixels cannot alias mutable arrays or collections", () =>
        {
            foreach (var source in new IEnumerable<uint>[] { new uint[] { 0xFF123456 }, new List<uint> { 0xFF123456 } })
            {
                var texture = new Texture2D(1, 1, source);
                ((IList<uint>)source)[0] = 0xFFABCDEF;
                Check(texture.Sample(.5f, .5f) == 0xFF123456, "source mutation reached texture");
                var exposed = (IList<uint>)texture.Pixels;
                Throws<NotSupportedException>(() => exposed[0] = 0xFFFFFFFF);
                Check(texture.Sample(.5f, .5f) == 0xFF123456, "exposed pixels mutated texture");
            }
        })
    ];

    private static IEnumerable<uint> Pixels(int count)
    {
        for (var i = 0; i < count; i++) yield return 0xFF123456;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

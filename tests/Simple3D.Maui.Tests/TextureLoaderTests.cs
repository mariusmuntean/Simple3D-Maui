using Simple3D.Core;
using Simple3D.Maui;
using SkiaSharp;

internal static class TextureLoaderTests
{
    public static readonly (string Name, Action Run)[] Cases =
    [
        ("image decoding allocates one pixel buffer and exposes no mutable alias", () =>
        {
            const int size = 256;
            using var source = Png(size, size, Enumerable.Repeat(0xFF123456u, size * size).ToArray());
            _ = TextureLoader.Load(source);
            source.Position = 0;
            var start = GC.GetAllocatedBytesForCurrentThread();
            var texture = TextureLoader.Load(source);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert(bytes < size * size * 4 + 32_768, $"decode allocated {bytes} bytes");
            source.Position = 0;
            source.WriteByte(0);
            var exposed = (IList<uint>)texture.Pixels;
            Throws<NotSupportedException>(() => exposed[0] = 0xFFFFFFFF);
            Assert(texture.Pixels.All(pixel => pixel == 0xFF123456), "decoded texture has a mutable alias");
        }),
        ("image decoding releases failed decode resources before retry", () =>
        {
            using var source = Png(16, 16, Enumerable.Repeat(0x80123456u, 256).ToArray());
            for (var attempt = 0; attempt < 4; attempt++)
            {
                source.Position = 0;
                Throws<InvalidDataException>(() => TextureLoader.Load(source));
                Assert(source.CanRead, "failed decode closed its stream");
            }
            using var valid = Png(1, 1, [0xFF123456]);
            Assert(TextureLoader.Load(valid).Pixels[0] == 0xFF123456, "failed decode affected later loading");
        }),
        ("image textures preserve RGB channels and top-to-bottom rows", () =>
        {
            using var stream = Png(2, 2, [0xFF123456, 0xFFABCDEF, 0xFF987654, 0xFF2468AC]);
            var texture = TextureLoader.Load(stream);
            Assert(texture.Width == 2 && texture.Height == 2, "image dimensions changed");
            Assert(texture.Pixels.SequenceEqual(new uint[] { 0xFF123456, 0xFFABCDEF, 0xFF987654, 0xFF2468AC }),
                "decoded channels or row order changed");
            Assert(texture.Sample(0, 1) == 0xFF123456 && texture.Sample(0, 0) == 0xFF987654,
                "image orientation disagrees with texture UVs");
            Assert(stream.CanRead, "loader disposed the caller's stream");
        }),
        ("image textures keep requested sampler settings", () =>
        {
            using var stream = Png(2, 1, [0xFF000000, 0xFFFFFFFF]);
            var texture = TextureLoader.Load(stream, TextureFilter.Bilinear, TextureAddressMode.Repeat);
            Assert(texture.Sample(1.5f, .5f) == 0xFF808080, "sampler settings were ignored");
        }),
        ("image textures reject translucent pixels and leave streams open", () =>
        {
            foreach (var pixel in new uint[] { 0x00123456, 0x80123456 })
            {
                using var stream = Png(1, 1, [pixel]);
                Throws<InvalidDataException>(() => TextureLoader.Load(stream));
                Assert(stream.CanRead, "rejected image closed the caller's stream");
            }
        }),
        ("image textures reject corrupt and incomplete images", () =>
        {
            using var valid = Png(2, 2, [0xFF123456, 0xFFABCDEF, 0xFF987654, 0xFF2468AC]);
            foreach (var bytes in new[] { Array.Empty<byte>(), new byte[] { 1, 2, 3, 4 }, valid.ToArray()[..50] })
            {
                using var stream = new MemoryStream(bytes);
                Throws<InvalidDataException>(() => TextureLoader.Load(stream));
                Assert(stream.CanRead, "invalid image closed the caller's stream");
            }
        }),
        ("image textures reject oversized dimensions", () =>
        {
            foreach (var (width, height) in new[] { (4097, 1), (1, 4097) })
            {
                using var stream = Png(width, height, Enumerable.Repeat(0xFFFFFFFFu, width * height).ToArray());
                Throws<InvalidDataException>(() => TextureLoader.Load(stream));
                Assert(stream.CanRead, "oversized image closed the caller's stream");
            }
        }),
        ("image textures support caller-owned nonseekable streams", () =>
        {
            using var source = Png(1, 1, [0xFF123456]);
            using var stream = new NonseekableStream(source.ToArray());
            Assert(TextureLoader.Load(stream).Pixels[0] == 0xFF123456, "nonseekable stream did not decode");
            Assert(stream.CanRead, "loader disposed the caller's nonseekable stream");
        }),
        ("image textures validate null streams and sampler enums", () =>
        {
            Throws<ArgumentNullException>(() => TextureLoader.Load(null!));
            using var stream = Png(1, 1, [0xFFFFFFFF]);
            Throws<ArgumentOutOfRangeException>(() => TextureLoader.Load(stream, (TextureFilter)42));
            Throws<ArgumentOutOfRangeException>(() => TextureLoader.Load(stream, addressMode: (TextureAddressMode)42));
        })
    ];

    private static MemoryStream Png(int width, int height, uint[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Pixels = pixels.Select(pixel => new SKColor(pixel)).ToArray();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class NonseekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}

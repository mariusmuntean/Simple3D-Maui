namespace Simple3D.Core;

/// <summary>How a texture is sampled between pixels.</summary>
public enum TextureFilter
{
    /// <summary>Use the nearest pixel.</summary>
    Nearest,
    /// <summary>Blend the four neighboring pixels.</summary>
    Bilinear
}

/// <summary>How texture coordinates outside the unit square are addressed.</summary>
public enum TextureAddressMode
{
    /// <summary>Extend the edge pixels.</summary>
    Clamp,
    /// <summary>Repeat every unit, including negative coordinates.</summary>
    Repeat
}

/// <summary>Immutable opaque image. Pixels are top-to-bottom ARGB; UV (0,0) is the bottom-left corner.</summary>
public sealed class Texture2D
{
    /// <summary>Maximum width or height accepted for an image.</summary>
    public const int MaximumDimension = 4096;
    private readonly uint[] _pixels;
    /// <summary>Image width in pixels.</summary>
    public int Width { get; }
    /// <summary>Image height in pixels.</summary>
    public int Height { get; }
    /// <summary>Copied row-major pixels packed as 0xFFRRGGBB.</summary>
    public IReadOnlyList<uint> Pixels { get; }
    /// <summary>Sampling filter.</summary>
    public TextureFilter Filter { get; }
    /// <summary>Addressing shared by both axes.</summary>
    public TextureAddressMode AddressMode { get; }

    /// <summary>Copies exactly width * height opaque pixels. Sizes, pixels and sampler settings are validated.</summary>
    public Texture2D(int width, int height, IEnumerable<uint> pixels,
        TextureFilter filter = TextureFilter.Nearest, TextureAddressMode addressMode = TextureAddressMode.Clamp)
        : this(width, height, CopyPixels(width, height, pixels, filter, addressMode), filter, addressMode)
    {
    }

    // The decoder transfers exclusive ownership; neither it nor public callers retain this buffer.
    internal static Texture2D FromOwnedPixels(int width, int height, uint[] pixels,
        TextureFilter filter, TextureAddressMode addressMode) => new(width, height, pixels, filter, addressMode);

    private Texture2D(int width, int height, uint[] pixels, TextureFilter filter, TextureAddressMode addressMode)
    {
        Validate(width, height, filter, addressMode);
        if (pixels.Length != width * height) throw new ArgumentException("Pixel count must match image dimensions.", nameof(pixels));
        foreach (var pixel in pixels)
            if (pixel >> 24 != 255) throw new ArgumentException("Texture pixels must be opaque.", nameof(pixels));
        _pixels = pixels;
        Width = width; Height = height; Filter = filter; AddressMode = addressMode;
        Pixels = Array.AsReadOnly(_pixels);
    }

    private static void Validate(int width, int height, TextureFilter filter, TextureAddressMode addressMode)
    {
        if (width < 1 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        if (!Enum.IsDefined(addressMode)) throw new ArgumentOutOfRangeException(nameof(addressMode));
    }

    private static uint[] CopyPixels(int width, int height, IEnumerable<uint> pixels,
        TextureFilter filter, TextureAddressMode addressMode)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Validate(width, height, filter, addressMode);
        var copy = new uint[width * height];
        using var iterator = pixels.GetEnumerator();
        for (var i = 0; i < copy.Length; i++)
        {
            if (!iterator.MoveNext()) throw new ArgumentException("Pixel count must match image dimensions.", nameof(pixels));
            copy[i] = iterator.Current;
        }
        if (iterator.MoveNext()) throw new ArgumentException("Pixel count must match image dimensions.", nameof(pixels));
        return copy;
    }

    /// <summary>Samples an opaque color at finite UV coordinates.</summary>
    public uint Sample(float u, float v)
    {
        if (!float.IsFinite(u)) throw new ArgumentOutOfRangeException(nameof(u));
        if (!float.IsFinite(v)) throw new ArgumentOutOfRangeException(nameof(v));
        return SampleCore(u, v);
    }

    internal uint SampleCore(double u, double v)
    {
        if (AddressMode == TextureAddressMode.Repeat) { u -= Math.Floor(u); v -= Math.Floor(v); }
        else { u = Math.Clamp(u, 0, 1); v = Math.Clamp(v, 0, 1); }
        var x = u * Width; var y = (1 - v) * Height;
        if (Filter == TextureFilter.Nearest) return Pixel((int)Math.Floor(x), Height - 1 - (int)Math.Floor(v * Height));
        x -= .5; y -= .5;
        var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y);
        var fx = x - ix; var fy = y - iy;
        var a = Pixel(ix, iy); var b = Pixel(ix + 1, iy);
        var c = Pixel(ix, iy + 1); var d = Pixel(ix + 1, iy + 1);
        return 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
        uint Channel(int shift) => (uint)Math.Round(
            (((a >> shift) & 255) * (1 - fx) + ((b >> shift) & 255) * fx) * (1 - fy) +
            (((c >> shift) & 255) * (1 - fx) + ((d >> shift) & 255) * fx) * fy);
    }

    private uint Pixel(int x, int y)
    {
        if (AddressMode == TextureAddressMode.Repeat)
        {
            x = (x % Width + Width) % Width; y = (y % Height + Height) % Height;
        }
        else { x = Math.Clamp(x, 0, Width - 1); y = Math.Clamp(y, 0, Height - 1); }
        return _pixels[y * Width + x];
    }
}

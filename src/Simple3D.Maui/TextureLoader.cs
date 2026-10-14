using Simple3D.Core;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Simple3D.Maui;

/// <summary>Decodes opaque images using the platform's Skia image codecs.</summary>
public static class TextureLoader
{
    /// <summary>Loads the first image frame into an immutable texture, leaving the input stream open.</summary>
    /// <remarks>
    /// Images must contain only opaque pixels and measure at most 4096 pixels per dimension.
    /// Pixel rows retain their encoded order; texture UV (0,0) refers to the bottom-left corner.
    /// </remarks>
    /// <exception cref="InvalidDataException">The image is invalid, incomplete, oversized, or contains transparency.</exception>
    public static Texture2D Load(Stream stream, TextureFilter filter = TextureFilter.Nearest,
        TextureAddressMode addressMode = TextureAddressMode.Clamp)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        if (!Enum.IsDefined(addressMode)) throw new ArgumentOutOfRangeException(nameof(addressMode));

        using SKStream input = stream.CanSeek
            ? new SKManagedStream(stream, disposeManagedStream: false)
            : new SKFrontBufferedManagedStream(stream, SKCodec.MinBufferedBytesNeeded, disposeUnderlyingStream: false);
        using var codec = SKCodec.Create(input)
            ?? throw new InvalidDataException("The stream does not contain a supported image.");
        var source = codec.Info;
        if (source.Width < 1 || source.Width > Texture2D.MaximumDimension ||
            source.Height < 1 || source.Height > Texture2D.MaximumDimension)
            throw new InvalidDataException($"Image dimensions must be between 1 and {Texture2D.MaximumDimension} pixels.");

        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new uint[source.Width * source.Height];
        // Decode into the final buffer. Pin only during native decoding, including failure paths.
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (codec.GetPixels(info, pinned.AddrOfPinnedObject()) != SKCodecResult.Success)
                throw new InvalidDataException("The image could not be completely decoded.");
        }
        finally { pinned.Free(); }

        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        for (var i = 0; i < pixels.Length; i++)
        {
            var offset = i * 4;
            if (bytes[offset + 3] != 255)
                throw new InvalidDataException("Texture images must contain only opaque pixels.");
            pixels[i] = 0xFF000000u | (uint)bytes[offset] << 16 | (uint)bytes[offset + 1] << 8 | bytes[offset + 2];
        }
        return Texture2D.FromOwnedPixels(source.Width, source.Height, pixels, filter, addressMode);
    }
}

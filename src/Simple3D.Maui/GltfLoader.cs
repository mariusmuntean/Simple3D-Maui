using Simple3D.Core;

namespace Simple3D.Maui;

/// <summary>A parsed glTF model and its default scene with decoded textures.</summary>
/// <param name="Model">Reusable geometry, images and skeletons.</param>
/// <param name="Shape">Default scene, ready to add to a Simple3D scene.</param>
public sealed record LoadedGltf(GltfModel Model, Shape Shape);

/// <summary>Loads binary glTF / VRM assets and decodes embedded images with Skia.</summary>
public static class GltfLoader
{
    /// <summary>Loads a bounded GLB, leaving the input stream open. Image alpha is made opaque.</summary>
    public static LoadedGltf Load(Stream stream)
    {
        var model = GltfReader.Read(stream);
        return new(model, model.CreateShape(image =>
        {
            using var encoded = new MemoryStream(image.Bytes.ToArray(), writable: false);
            return TextureLoader.LoadOpaque(encoded, TextureFilter.Bilinear, TextureAddressMode.Repeat);
        }));
    }

    /// <summary>Parses and decodes on a worker thread, leaving the stream open. Do not use the stream until completion.</summary>
    public static Task<LoadedGltf> LoadAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Task.Run(() => Load(stream));
    }
}

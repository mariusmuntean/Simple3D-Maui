using Simple3D.Core;
namespace Simple3D.Maui;

internal static class GpuResources
{
    private static readonly Texture2D White = new(1, 1, new[] { 0xFFFFFFFFu });
    /// <summary>The item's texture, or a 1x1 white texture so every draw samples one.</summary>
    internal static Texture2D TextureOf(GpuItem item) => item.Shape.Material.Texture ?? White;
    /// <summary>Texture pixels (0xAARRGGBB) as opaque RGBA8 bytes for native upload.</summary>
    internal static byte[] Rgba8(Texture2D texture)
    {
        var bytes = new byte[texture.Width * texture.Height * 4];
        for (var i = 0; i < texture.Pixels.Count; i++)
        {
            var c = texture.Pixels[i];
            bytes[i * 4] = (byte)(c >> 16); bytes[i * 4 + 1] = (byte)(c >> 8); bytes[i * 4 + 2] = (byte)c; bytes[i * 4 + 3] = 255;
        }
        return bytes;
    }
}

// Native mesh/texture residency shared by both backends: bounded to the current scene (GpuScene
// enforces the 256 MiB budget) and pruned without per-frame allocation. Backends supply only
// native creation/deletion.
internal sealed class GpuResources<TMesh, TTexture>(Action<TMesh> deleteMesh, Action<TTexture> deleteTexture)
{
    private readonly HashSet<GpuGeometry> _usedMeshes = new();
    private readonly HashSet<Texture2D> _usedTextures = new();
    private readonly List<GpuGeometry> _staleMeshes = new();
    private readonly List<Texture2D> _staleTextures = new();
    internal Dictionary<GpuGeometry, TMesh> Meshes { get; } = new();
    internal Dictionary<Texture2D, TTexture> Textures { get; } = new();

    /// <summary>Frees resources the frame no longer uses, before anything new is uploaded.</summary>
    internal void Prepare(GpuFrame frame)
    {
        _usedMeshes.Clear(); _usedTextures.Clear();
        foreach (var item in frame.Geometry.Items)
        {
            _usedMeshes.Add(item.Geometry);
            _usedTextures.Add(GpuResources.TextureOf(item));
        }
        PruneUnused();
    }

    internal void Clear() { _usedMeshes.Clear(); _usedTextures.Clear(); PruneUnused(); }

    private void PruneUnused()
    {
        Prune(Meshes, _usedMeshes, _staleMeshes, deleteMesh);
        Prune(Textures, _usedTextures, _staleTextures, deleteTexture);
    }

    private static void Prune<TKey, TValue>(Dictionary<TKey, TValue> cache, HashSet<TKey> used, List<TKey> stale, Action<TValue> delete) where TKey : notnull
    {
        stale.Clear();
        foreach (var key in cache.Keys) if (!used.Contains(key)) stale.Add(key);
        foreach (var key in stale) { delete(cache[key]); cache.Remove(key); }
    }
}

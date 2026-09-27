using System.Numerics;

namespace Simple3D.Core;

/// <summary>Immutable indexed triangle geometry. Counterclockwise vertices face outward; both sides render by default.</summary>
public sealed class Mesh
{
    /// <summary>Maximum number of vertices or triangles accepted in one mesh.</summary>
    public const int MaximumElements = 1_000_000;
    /// <summary>Copied vertices in local coordinates.</summary>
    public IReadOnlyList<Vector3> Vertices { get; }
    /// <summary>Copied vertex indices, three per triangle.</summary>
    public IReadOnlyList<int> Indices { get; }
    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Count / 3;
    internal IReadOnlyList<Triangle3> Triangles { get; }

    /// <summary>Copies and validates finite vertices, index ranges and nondegenerate triangles. Empty meshes are allowed. Triangles whose single-precision area overflows or underflows are rejected.</summary>
    public Mesh(IEnumerable<Vector3> vertices, IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        var copiedVertices = vertices.Take(MaximumElements + 1).ToArray();
        var copiedIndices = indices.Take(MaximumElements * 3 + 1).ToArray();
        if (copiedVertices.Length > MaximumElements || copiedIndices.Length > MaximumElements * 3 || copiedIndices.Length % 3 != 0)
            throw new ArgumentException("Mesh size or index count is invalid.");
        foreach (var point in copiedVertices) Validation.Vector(point, nameof(vertices));
        for (var i = 0; i < copiedIndices.Length; i += 3)
        {
            if ((uint)copiedIndices[i] >= copiedVertices.Length ||
                (uint)copiedIndices[i + 1] >= copiedVertices.Length ||
                (uint)copiedIndices[i + 2] >= copiedVertices.Length)
                throw new ArgumentException("Index outside vertex array.", nameof(indices));
            var a = copiedVertices[copiedIndices[i]];
            var b = copiedVertices[copiedIndices[i + 1]];
            var c = copiedVertices[copiedIndices[i + 2]];
            var area = Vector3.Cross(b - a, c - a).LengthSquared();
            if (!float.IsFinite(area) || area <= 0)
                throw new ArgumentException("Degenerate or overflowing triangle.", nameof(vertices));
        }
        Vertices = Array.AsReadOnly(copiedVertices);
        Indices = Array.AsReadOnly(copiedIndices);
        Triangles = new IndexedTriangles(Vertices, Indices);
    }

    // Keep indices as the source of truth; instances never expand shared vertices.
    private sealed class IndexedTriangles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices) : IReadOnlyList<Triangle3>
    {
        public int Count => indices.Count / 3;
        public Triangle3 this[int index] => new(vertices[indices[index * 3]], vertices[indices[index * 3 + 1]], vertices[indices[index * 3 + 2]]);
        public IEnumerator<Triangle3> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Immutable opaque flat material using packed 0xAARRGGBB color.</summary>
public sealed class Material
{
    /// <summary>Opaque packed 0xAARRGGBB color.</summary>
    public uint Color { get; }
    /// <summary>Whether fixed directional lighting affects the color.</summary>
    public bool Lit { get; }
    /// <summary>Whether back faces are rendered.</summary>
    public bool DoubleSided { get; }

    /// <summary>Creates an opaque material. Transparent colors are rejected.</summary>
    public Material(uint color = 0xFF7C9DFF, bool lit = true, bool doubleSided = true)
    {
        if ((color >> 24) != 255) throw new ArgumentOutOfRangeException(nameof(color));
        Color = color;
        Lit = lit;
        DoubleSided = doubleSided;
    }
}

internal static class Validation
{
    internal static void Vector(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name);
    }
}

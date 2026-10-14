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
    /// <summary>Optional UV coordinates, one per vertex; origin is bottom-left.</summary>
    public IReadOnlyList<Vector2>? TextureCoordinates { get; }
    /// <summary>Optional normalized local-space normals, one per vertex. Presence enables Gouraud smooth lighting.</summary>
    public IReadOnlyList<Vector3>? Normals { get; }
    /// <summary>Optional opaque ARGB vertex colors, multiplied by material color and texture.</summary>
    public IReadOnlyList<uint>? Colors { get; }

    /// <summary>Copies and validates finite vertices, index ranges and nondegenerate triangles. Empty meshes are allowed. Triangles whose single-precision area overflows or underflows are rejected.</summary>
    public Mesh(IEnumerable<Vector3> vertices, IEnumerable<int> indices) : this(vertices, indices, null, null, null) { }

    /// <summary>Copies geometry and optional per-vertex attributes. Supplied attribute counts must match the vertex count.</summary>
    public Mesh(IEnumerable<Vector3> vertices, IEnumerable<int> indices,
        IEnumerable<Vector2>? textureCoordinates = null, IEnumerable<Vector3>? normals = null, IEnumerable<uint>? colors = null)
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
        TextureCoordinates = Copy(textureCoordinates, copiedVertices.Length, nameof(textureCoordinates), uv =>
        {
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) throw new ArgumentOutOfRangeException(nameof(textureCoordinates));
            return uv;
        });
        Normals = Copy(normals, copiedVertices.Length, nameof(normals), n => Validation.UnitVector(n, nameof(normals)));
        Colors = Copy(colors, copiedVertices.Length, nameof(colors), c =>
        {
            if (c >> 24 != 255) throw new ArgumentOutOfRangeException(nameof(colors));
            return c;
        });
    }

    private static IReadOnlyList<T>? Copy<T>(IEnumerable<T>? values, int count, string name, Func<T,T> validate)
    {
        if (values is null) return null;
        var copy = values.Take(count + 1).ToArray();
        if (copy.Length != count) throw new ArgumentException("Attribute count must match vertices.", name);
        for (var i = 0; i < copy.Length; i++) copy[i] = validate(copy[i]);
        return Array.AsReadOnly(copy);
    }

    /// <summary>Returns a copy with area-weighted normals averaged at shared indices. Split vertices retain hard edges. Unused or cancelling vertices use +Z.</summary>
    public Mesh WithCalculatedNormals()
    {
        // Double accumulators keep many large incident faces from overflowing.
        var sums = new (double X, double Y, double Z)[Vertices.Count];
        for (var i = 0; i < Indices.Count; i += 3)
        {
            var n = Vector3.Cross(Vertices[Indices[i+1]] - Vertices[Indices[i]], Vertices[Indices[i+2]] - Vertices[Indices[i]]);
            for (var j = 0; j < 3; j++)
            {
                ref var sum = ref sums[Indices[i+j]];
                sum.X += n.X; sum.Y += n.Y; sum.Z += n.Z;
            }
        }
        var normals = new Vector3[Vertices.Count];
        for (var i = 0; i < normals.Length; i++)
        {
            var n = sums[i];
            var length = Math.Sqrt(n.X*n.X + n.Y*n.Y + n.Z*n.Z);
            normals[i] = length == 0 ? Vector3.UnitZ : new((float)(n.X/length), (float)(n.Y/length), (float)(n.Z/length));
        }
        return new Mesh(Vertices, Indices, TextureCoordinates, normals, Colors);
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

/// <summary>Immutable opaque material using packed 0xAARRGGBB color.</summary>
public sealed class Material
{
    /// <summary>Opaque packed 0xAARRGGBB color.</summary>
    public uint Color { get; }
    /// <summary>Whether scene lighting affects the color.</summary>
    public bool Lit { get; }
    /// <summary>Whether back faces are rendered.</summary>
    public bool DoubleSided { get; }

    /// <summary>Optional immutable texture, multiplied by the color.</summary>
    public Texture2D? Texture { get; }

    /// <summary>Creates an opaque material. Transparent colors are rejected.</summary>
    public Material(uint color = 0xFF7C9DFF, bool lit = true, bool doubleSided = true)
    {
        if ((color >> 24) != 255) throw new ArgumentOutOfRangeException(nameof(color));
        Color = color;
        Lit = lit;
        DoubleSided = doubleSided;
    }

    /// <summary>Creates a textured material; white tint preserves texture colors. The shape must have UV coordinates.</summary>
    public Material(Texture2D texture, uint color = 0xFFFFFFFF, bool lit = true, bool doubleSided = true)
        : this(color, lit, doubleSided)
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
    }
}

internal static class Validation
{
    internal static void UnitInterval(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
    }
    internal static Vector3 UnitVector(Vector3 value, string name)
    {
        Vector(value, name);
        var max = MathF.Max(MathF.Abs(value.X), MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));
        if (max == 0) throw new ArgumentOutOfRangeException(name, "Direction must be nonzero.");
        return Vector3.Normalize(value / max);
    }
    internal static void Vector(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name);
    }
}

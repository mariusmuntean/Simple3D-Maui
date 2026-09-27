using System.Numerics;

namespace Simple3D.Core;

/// <summary>An immutable primitive, indexed mesh instance, or transform group.</summary>
public sealed class Shape
{
    internal readonly IReadOnlyList<Triangle3> Mesh;
    internal Shape(IReadOnlyList<Triangle3> mesh, uint color, Vector3 position, Vector3 rotation, Vector3 size, string? name = null, IReadOnlyList<Shape>? children = null, Material? material = null, Mesh? geometry = null)
    {
        if ((color & 0xFF000000) != 0xFF000000)
            throw new ArgumentOutOfRangeException(nameof(color), "Only opaque colors are supported.");
        (Mesh, Color, Position, Rotation, Size) = (mesh, color, position, rotation, size);
        Name = name;
        Children = children ?? Array.Empty<Shape>();
        Material = material ?? new Material(color);
        Geometry = geometry;
        HierarchyDepth = Children.Count == 0 ? 1 : 1 + Children.Max(c => c.HierarchyDepth);
    }

    /// <summary>Opaque packed 0xAARRGGBB surface color.</summary>
    public uint Color { get; }
    /// <summary>Local position relative to the parent.</summary>
    public Vector3 Position { get; }
    /// <summary>Local Euler rotation in radians.</summary>
    public Vector3 Rotation { get; }
    /// <summary>Positive local scale factors.</summary>
    public Vector3 Size { get; }
    /// <summary>Triangle count of this node, excluding descendants.</summary>
    public int TriangleCount => Mesh.Count;

    /// <summary>Creates a cached unit box centered at the origin.</summary>
    public static Shape Box(uint color = 0xFF7C9DFF) => New(Meshes.Box, color);
    /// <summary>Creates a cached unit sphere centered at the origin.</summary>
    public static Shape Sphere(uint color = 0xFFFFBC70) => New(Meshes.Sphere, color);
    /// <summary>Creates a cached unit cylinder centered at the origin.</summary>
    public static Shape Cylinder(uint color = 0xFF79D6C1) => New(Meshes.Cylinder, color);
    /// <summary>Creates a cached unit pyramid centered at the origin.</summary>
    public static Shape Pyramid(uint color = 0xFFE495CB) => New(Meshes.Pyramid, color);

    private static Shape New(Triangle3[] mesh, uint color) => new(mesh, color, Vector3.Zero, Vector3.Zero, Vector3.One);

    /// <summary>Returns a copy with an absolute position, preserving rotation and size.</summary>
    public Shape At(float x, float y, float z)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(z, nameof(z));
        return new(Mesh, Color, new(x, y, z), Rotation, Size, Name, Children, Material, Geometry);
    }

    /// <summary>Returns a copy with absolute Euler angles in radians, preserving position and size.</summary>
    public Shape Rotated(float x, float y, float z)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(z, nameof(z));
        return new(Mesh, Color, Position, new(x, y, z), Size, Name, Children, Material, Geometry);
    }

    /// <summary>Returns a copy with a uniform size; repeated calls replace the previous size.</summary>
    public Shape Scaled(float size)
    {
        RequirePositive(size, nameof(size));
        return new(Mesh, Color, Position, Rotation, new(size), Name, Children, Material, Geometry);
    }

    /// <summary>Returns a copy with absolute positive dimensions, preserving position and rotation.</summary>
    public Shape Scaled(float x, float y, float z)
    {
        RequirePositive(x, nameof(x));
        RequirePositive(y, nameof(y));
        RequirePositive(z, nameof(z));
        return new(Mesh, Color, Position, Rotation, new(x, y, z), Name, Children, Material, Geometry);
    }
    /// <summary>Optional application identifier.</summary>
    public string? Name { get; }
    /// <summary>Immutable child nodes, transformed in this node's coordinate system.</summary>
    public IReadOnlyList<Shape> Children { get; }
    /// <summary>Surface appearance.</summary>
    public Material Material { get; }
    /// <summary>Custom indexed mesh, or null for cached primitives and groups.</summary>
    public Mesh? Geometry { get; }
    /// <summary>Returns a copy with an application identifier.</summary>
    public Shape Named(string? name) => new(Mesh, Color, Position, Rotation, Size, name, Children, Material, Geometry);
    /// <summary>Returns a copy with a different immutable material.</summary>
    public Shape WithMaterial(Material material) => new(Mesh, (material ?? throw new ArgumentNullException(nameof(material))).Color, Position, Rotation, Size, Name, Children, material, Geometry);
    /// <summary>Creates an instance sharing an immutable indexed mesh.</summary>
    public static Shape FromMesh(Mesh mesh, Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        material ??= new Material();
        return new(mesh.Triangles, material.Color, Vector3.Zero, Vector3.Zero, Vector3.One, material: material, geometry: mesh);
    }
    /// <summary>Creates an immutable group. Children are copied; maximum hierarchy depth is 64.</summary>
    public static Shape Group(params Shape[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Any(c => c is null)) throw new ArgumentException("Null child.", nameof(children));
        if (children.Any(c => c.HierarchyDepth >= 64)) throw new ArgumentException("Hierarchy exceeds 64 levels.", nameof(children));
        return new([], 0xFFFFFFFF, Vector3.Zero, Vector3.Zero, Vector3.One, children: Array.AsReadOnly(children.ToArray()));
    }
    internal int HierarchyDepth { get; }
    internal Matrix4x4 Transform => Matrix4x4.CreateScale(Size) * Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z) * Matrix4x4.CreateTranslation(Position);
    /// <summary>Creates a cylindrical line between distinct finite endpoints. Thickness is a world-space diameter.</summary>
    public static Shape Line(Vector3 start, Vector3 end, float thickness = .03f, uint color = 0xFF7C9DFF)
        => Segment(start, end, thickness, color, false);
    /// <summary>Creates a cylindrical shaft and conical arrowhead between distinct finite endpoints.</summary>
    public static Shape Arrow(Vector3 start, Vector3 end, float thickness = .03f, uint color = 0xFF7C9DFF)
        => Segment(start, end, thickness, color, true);
    private static Shape Segment(Vector3 start, Vector3 end, float thickness, uint color, bool arrow)
    {
        Validation.Vector(start, nameof(start));
        Validation.Vector(end, nameof(end));
        RequirePositive(thickness, nameof(thickness));
        var delta = end - start;
        var length = delta.Length();
        RequirePositive(length, nameof(end));
        var axis = delta / length;
        var right = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < .9f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(axis, right);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var shaftEnd = arrow ? end - axis * MathF.Min(length * .3f, thickness * 4) : end;
        for (var i = 0; i < 16; i++)
        {
            var a = 2 * MathF.PI * i / 16;
            var b = 2 * MathF.PI * (i + 1) / 16;
            var u = (right * MathF.Cos(a) + up * MathF.Sin(a)) * thickness * .5f;
            var v = (right * MathF.Cos(b) + up * MathF.Sin(b)) * thickness * .5f;
            Face(start + u, start + v, shaftEnd + u);
            Face(start + v, shaftEnd + v, shaftEnd + u);
            Face(start, start + v, start + u);
            Face(shaftEnd, shaftEnd + u, shaftEnd + v);
            if (arrow)
            {
                Face(shaftEnd + u * 2.5f, shaftEnd + v * 2.5f, end);
                Face(shaftEnd, shaftEnd + v * 2.5f, shaftEnd + u * 2.5f);
            }
        }
        return FromMesh(new Mesh(vertices, indices), new Material(color));

        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            var i = vertices.Count;
            vertices.AddRange([a, b, c]);
            indices.AddRange([i, i + 1, i + 2]);
        }
    }

    private static void RequireFinite(float value, string parameter)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Transforms must be finite.");
    }
    private static void RequirePositive(float value, string parameter)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameter, "Dimensions must be positive and finite.");
    }
}

internal readonly record struct Triangle3(Vector3 A, Vector3 B, Vector3 C);

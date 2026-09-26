using System.Numerics;

namespace Simple3D.Core;

/// <summary>A colored, immutable instance of a cached unit mesh.</summary>
public sealed class Shape
{
    internal readonly Triangle3[] Mesh;
    internal Shape(Triangle3[] mesh, uint color, Vector3 position, Vector3 rotation, Vector3 size)
    {
        if ((color & 0xFF000000) != 0xFF000000)
            throw new ArgumentOutOfRangeException(nameof(color), "Only opaque colors are supported.");
        (Mesh, Color, Position, Rotation, Size) = (mesh, color, position, rotation, size);
    }

    public uint Color { get; }
    public Vector3 Position { get; }
    public Vector3 Rotation { get; }
    public Vector3 Size { get; }
    public int TriangleCount => Mesh.Length;

    public static Shape Box(uint color = 0xFF7C9DFF) => New(Meshes.Box, color);
    public static Shape Sphere(uint color = 0xFFFFBC70) => New(Meshes.Sphere, color);
    public static Shape Cylinder(uint color = 0xFF79D6C1) => New(Meshes.Cylinder, color);
    public static Shape Pyramid(uint color = 0xFFE495CB) => New(Meshes.Pyramid, color);

    private static Shape New(Triangle3[] mesh, uint color) => new(mesh, color, Vector3.Zero, Vector3.Zero, Vector3.One);
    public Shape At(float x, float y, float z)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(z, nameof(z));
        return Copy(new(x, y, z), Rotation, Size);
    }
    public Shape Rotated(float x, float y, float z)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(z, nameof(z));
        return Copy(Position, new(x, y, z), Size);
    }
    public Shape Scaled(float size)
    {
        RequirePositive(size, nameof(size));
        return Scaled(size, size, size);
    }
    public Shape Scaled(float x, float y, float z)
    {
        RequirePositive(x, nameof(x));
        RequirePositive(y, nameof(y));
        RequirePositive(z, nameof(z));
        return Copy(Position, Rotation, new(x, y, z));
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
    private Shape Copy(Vector3 position, Vector3 rotation, Vector3 size)
    {
        if (!Valid(position.X) || !Valid(position.Y) || !Valid(position.Z) ||
            !Valid(rotation.X) || !Valid(rotation.Y) || !Valid(rotation.Z))
            throw new ArgumentOutOfRangeException(nameof(position), "Transforms must be finite.");
        return new(Mesh, Color, position, rotation, size);
    }
    private static bool Valid(float n) => float.IsFinite(n);
}

internal readonly record struct Triangle3(Vector3 A, Vector3 B, Vector3 C);

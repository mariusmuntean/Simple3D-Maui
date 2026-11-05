using System.Numerics;

namespace Simple3D.Core;

/// <summary>An immutable primitive, indexed mesh instance, or transform group.</summary>
public sealed class Shape
{
    internal readonly IReadOnlyList<Triangle3> Mesh;
    private readonly Quaternion? _quaternion;
    internal Shape(IReadOnlyList<Triangle3> mesh, uint color, Vector3 position, Vector3 rotation, Vector3 size, string? name = null, IReadOnlyList<Shape>? children = null, Material? material = null, Mesh? geometry = null, Quaternion? quaternion = null)
    {
        if ((color & 0xFF000000) != 0xFF000000)
            throw new ArgumentOutOfRangeException(nameof(color), "Only opaque colors are supported.");
        (Mesh, Color, Position, Rotation, Size) = (mesh, color, position, rotation, size);
        Name = name;
        Children = children ?? Array.Empty<Shape>();
        Material = material ?? new Material(color);
        Geometry = geometry;
        _quaternion = quaternion;
        HierarchyDepth = Children.Count == 0 ? 1 : 1 + Children.Max(c => c.HierarchyDepth);
    }

    /// <summary>Opaque packed 0xAARRGGBB surface color.</summary>
    public uint Color { get; }
    /// <summary>Local position relative to the parent.</summary>
    public Vector3 Position { get; }
    /// <summary>Local Euler rotation in radians. For nodes imported from glTF it approximates the exact imported rotation, which <see cref="Rotated"/> replaces.</summary>
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
        return new(Mesh, Color, new(x, y, z), Rotation, Size, Name, Children, Material, Geometry, _quaternion);
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
        return new(Mesh, Color, Position, Rotation, new(size), Name, Children, Material, Geometry, _quaternion);
    }

    /// <summary>Returns a copy with absolute positive dimensions, preserving position and rotation.</summary>
    public Shape Scaled(float x, float y, float z)
    {
        RequirePositive(x, nameof(x));
        RequirePositive(y, nameof(y));
        RequirePositive(z, nameof(z));
        return new(Mesh, Color, Position, Rotation, new(x, y, z), Name, Children, Material, Geometry, _quaternion);
    }
    /// <summary>Optional application identifier.</summary>
    public string? Name { get; }
    /// <summary>Immutable child nodes, transformed in this node's coordinate system.</summary>
    public IReadOnlyList<Shape> Children { get; }
    /// <summary>Surface appearance.</summary>
    public Material Material { get; }
    /// <summary>Indexed geometry, or null for untextured cached primitives and groups.</summary>
    public Mesh? Geometry { get; }
    /// <summary>Returns a copy with an application identifier.</summary>
    public Shape Named(string? name) => new(Mesh, Color, Position, Rotation, Size, name, Children, Material, Geometry, _quaternion);
    /// <summary>Returns a copy with a different immutable material.</summary>
    public Shape WithMaterial(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var geometry = Geometry;
        if (material.Texture is not null && geometry is null) geometry = PrimitiveTextures.Get(Mesh);
        if (material.Texture is not null && geometry?.TextureCoordinates is null)
            throw new ArgumentException("Textured shapes require mesh texture coordinates.", nameof(material));
        var triangles = Mesh is PosedGeometry ? Mesh : geometry?.Triangles ?? Mesh;
        return new(triangles, material.Color, Position, Rotation, Size, Name, Children, material, geometry, _quaternion);
    }
    /// <summary>Creates an instance of skinned geometry in its skeleton's rest pose.</summary>
    public static Shape FromSkinnedMesh(SkinnedMesh mesh, Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        material ??= new Material();
        if (material.Texture is not null && mesh.Mesh.TextureCoordinates is null)
            throw new ArgumentException("Textured shapes require mesh texture coordinates.", nameof(material));
        return new(new PosedGeometry(mesh, mesh.Skeleton.RestPose), material.Color, Vector3.Zero, Vector3.Zero, Vector3.One,
            material: material, geometry: mesh.Mesh);
    }
    /// <summary>Skinned geometry of this node, or null.</summary>
    public SkinnedMesh? SkinnedMesh => (Mesh as PosedGeometry)?.Skinned;
    /// <summary>Current pose of this node's skinned geometry, or null.</summary>
    public SkeletonPose? Pose => (Mesh as PosedGeometry)?.Pose;
    /// <summary>Returns a copy in which this node and every descendant skinned by the pose's skeleton use the pose. Throws when none does.</summary>
    public Shape WithPose(SkeletonPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        var matched = false;
        var posed = Posed(pose, ref matched);
        if (!matched) throw new ArgumentException("No skinned geometry in this shape uses the pose's skeleton.", nameof(pose));
        return posed;
    }
    private Shape Posed(SkeletonPose pose, ref bool matched)
    {
        Shape[]? children = null;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i].Posed(pose, ref matched);
            if (ReferenceEquals(child, Children[i])) continue;
            children ??= Children.ToArray();
            children[i] = child;
        }
        var mesh = Mesh;
        if (Mesh is PosedGeometry posed && ReferenceEquals(posed.Skinned.Skeleton, pose.Skeleton))
        {
            matched = true;
            if (!ReferenceEquals(posed.Pose, pose)) mesh = new PosedGeometry(posed.Skinned, pose, posed.MorphWeights);
        }
        if (children is null && ReferenceEquals(mesh, Mesh)) return this;
        return new(mesh, Color, Position, Rotation, Size, Name, children is null ? Children : Array.AsReadOnly(children), Material, Geometry, _quaternion);
    }
    /// <summary>Current morph target weights of this node's skinned geometry, or null when it has none set.</summary>
    public IReadOnlyList<float>? MorphWeights => (Mesh as PosedGeometry)?.MorphWeights is { } weights ? Array.AsReadOnly(weights) : null;
    /// <summary>Returns a copy in which every node using <paramref name="mesh"/> blends its morph targets with <paramref name="weights"/>
    /// (one per target, each in [0,1]). Throws when no node uses the mesh.</summary>
    public Shape WithMorphWeights(SkinnedMesh mesh, ReadOnlySpan<float> weights) => WithMorphWeights(mesh, weights, required: true);
    internal Shape WithMorphWeights(SkinnedMesh mesh, ReadOnlySpan<float> weights, bool required)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (weights.Length != mesh.MorphTargets.Count) throw new ArgumentException("Supply one weight per morph target.", nameof(weights));
        foreach (var w in weights) Validation.UnitInterval(w, nameof(weights));
        var copy = weights.ToArray();
        var matched = false;
        var result = Morphed(mesh, copy.All(w => w == 0) ? null : copy, ref matched);
        if (required && !matched) throw new ArgumentException("No node in this shape uses the mesh.", nameof(mesh));
        return result;
    }
    private Shape Morphed(SkinnedMesh target, float[]? weights, ref bool matched)
    {
        Shape[]? children = null;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i].Morphed(target, weights, ref matched);
            if (ReferenceEquals(child, Children[i])) continue;
            children ??= Children.ToArray();
            children[i] = child;
        }
        var mesh = Mesh;
        if (Mesh is PosedGeometry posed && ReferenceEquals(posed.Skinned, target))
        {
            matched = true;
            // Unchanged weights keep the node, so a steady face costs nothing downstream.
            var same = posed.MorphWeights is null ? weights is null : weights is not null && posed.MorphWeights.AsSpan().SequenceEqual(weights);
            if (!same) mesh = new PosedGeometry(posed.Skinned, posed.Pose, weights);
        }
        if (children is null && ReferenceEquals(mesh, Mesh)) return this;
        return new(mesh, Color, Position, Rotation, Size, Name, children is null ? Children : Array.AsReadOnly(children), Material, Geometry, _quaternion);
    }
    // Lighting normals: skinned shapes use posed normals.
    internal IReadOnlyList<Vector3>? ShadingNormals => Mesh is PosedGeometry posed ? posed.Normals : Geometry?.Normals;
    /// <summary>Creates an instance sharing an immutable indexed mesh.</summary>
    public static Shape FromMesh(Mesh mesh, Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        material ??= new Material();
        if (material.Texture is not null && mesh.TextureCoordinates is null)
            throw new ArgumentException("Textured shapes require mesh texture coordinates.", nameof(material));
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
    // Keep imported rotations exact, including at Euler singularities. Public rotation edits replace them.
    internal Shape WithLocalTransform(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        var m = Matrix4x4.CreateFromQuaternion(rotation);
        var pitch = MathF.Asin(Math.Clamp(-m.M32, -1, 1));
        var angles = MathF.Abs(m.M32) < .999999f
            ? new Vector3(pitch, MathF.Atan2(m.M31, m.M33), MathF.Atan2(m.M12, m.M22))
            : new Vector3(pitch, MathF.Atan2(-m.M13, m.M11), 0);
        return new(Mesh, Color, translation, angles, scale, Name, Children, Material, Geometry, rotation);
    }
    internal Shape WithChildren(Shape[] children) => new(Mesh, Color, Position, Rotation, Size, Name,
        Array.AsReadOnly(children), Material, Geometry, _quaternion);
    internal int HierarchyDepth { get; }
    internal Matrix4x4 Transform => Matrix4x4.CreateScale(Size) * (_quaternion is { } q ? Matrix4x4.CreateFromQuaternion(q) : Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z)) * Matrix4x4.CreateTranslation(Position);
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

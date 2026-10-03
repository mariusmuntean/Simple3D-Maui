using System.Numerics;

namespace Simple3D.Core;

/// <summary>A mutable scene. Mutate and render on the same thread; notifications are synchronous.</summary>
public sealed class Scene
{
    /// <summary>Maximum node visits during bounds calculation or rendering, including empty groups and repeated instances.</summary>
    public const int MaximumNodes = 100_000;
    private readonly List<Shape> _shapes = new();
    private readonly List<WorldLabel> _labels = new();

    /// <summary>Creates an empty scene.</summary>
    public Scene()
    {
        Shapes = _shapes.AsReadOnly();
        Labels = _labels.AsReadOnly();
    }

    /// <summary>Top-level immutable nodes.</summary>
    public IReadOnlyList<Shape> Shapes { get; }
    /// <summary>World anchored overlay labels.</summary>
    public IReadOnlyList<WorldLabel> Labels { get; }
    /// <summary>Raised once after each successful mutation; replacements provide <see cref="ShapeReplacementEventArgs"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Appends a node and notifies observers.</summary>
    public Scene Add(Shape shape)
    {
        _shapes.Add(shape ?? throw new ArgumentNullException(nameof(shape)));
        Notify();
        return this;
    }

    /// <summary>Removes the first matching top-level node.</summary>
    public bool Remove(Shape shape)
    {
        var result = _shapes.Remove(shape);
        if (result) Notify();
        return result;
    }

    /// <summary>Replaces the first matching top-level node. Returns false when absent or unchanged.</summary>
    public bool Replace(Shape oldShape, Shape newShape)
    {
        ArgumentNullException.ThrowIfNull(newShape);
        var index = _shapes.IndexOf(oldShape);
        if (index < 0 || ReferenceEquals(oldShape, newShape)) return false;
        _shapes[index] = newShape;
        Changed?.Invoke(this, new ShapeReplacementEventArgs(oldShape, newShape));
        return true;
    }

    /// <summary>Adds a label and notifies observers.</summary>
    public Scene AddLabel(WorldLabel label)
    {
        _labels.Add(label ?? throw new ArgumentNullException(nameof(label)));
        Notify();
        return this;
    }

    /// <summary>Removes the first matching label.</summary>
    public bool RemoveLabel(WorldLabel label)
    {
        var result = _labels.Remove(label);
        if (result) Notify();
        return result;
    }

    /// <summary>Removes all nodes and labels.</summary>
    public Scene Clear()
    {
        if (_shapes.Count + _labels.Count > 0)
        {
            _shapes.Clear();
            _labels.Clear();
            Notify();
        }
        return this;
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    internal IEnumerable<(Shape Shape, Matrix4x4 Transform)> Flatten()
    {
        var visited = 0;
        foreach (var shape in _shapes)
            foreach (var node in Walk(shape, Matrix4x4.Identity)) yield return node;

        IEnumerable<(Shape, Matrix4x4)> Walk(Shape shape, Matrix4x4 parent)
        {
            if (++visited > MaximumNodes) throw new ArgumentException("Scene exceeds node visit budget.");
            var transform = shape.Transform * parent;
            if (shape.Mesh.Count > 0) yield return (shape, transform);
            foreach (var child in shape.Children)
                foreach (var node in Walk(child, transform)) yield return node;
        }
    }

    /// <summary>World axis-aligned bounds of geometry, or null for an empty scene. Labels are excluded. Overflowing transforms or excessive node or triangle visits throw.</summary>
    public Bounds3? GetBounds()
    {
        var nodes = Flatten().ToArray();
        long triangleCount = 0;
        foreach (var (shape, _) in nodes)
        {
            triangleCount += shape.TriangleCount;
            if (triangleCount > DepthRenderer.MaximumTriangles)
                throw new ArgumentException("Scene exceeds triangle budget.", nameof(Shapes));
        }
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var any = false;
        foreach (var (shape, transform) in nodes)
            foreach (var triangle in shape.Mesh)
            {
                Include(Vector3.Transform(triangle.A, transform));
                Include(Vector3.Transform(triangle.B, transform));
                Include(Vector3.Transform(triangle.C, transform));
            }
        return any ? new(min, max) : null;

        void Include(Vector3 point)
        {
            Validation.Vector(point, "transformedVertex");
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
            any = true;
        }
    }
}

/// <summary>A replaced scene node. Corresponding child positions represent the same parts in the replacement tree.</summary>
public sealed class ShapeReplacementEventArgs : EventArgs
{
    /// <summary>The previous immutable node.</summary>
    public Shape OldShape { get; }
    /// <summary>The replacement immutable node.</summary>
    public Shape NewShape { get; }
    internal ShapeReplacementEventArgs(Shape oldShape, Shape newShape) => (OldShape, NewShape) = (oldShape, newShape);
}

/// <summary>World-space axis-aligned bounds.</summary>
/// <param name="Min">Minimum coordinates.</param>
/// <param name="Max">Maximum coordinates.</param>
public readonly record struct Bounds3(Vector3 Min, Vector3 Max)
{
    /// <summary>Center of the bounds.</summary>
    public Vector3 Center => Min * .5f + Max * .5f;
    /// <summary>Dimensions of the bounds.</summary>
    public Vector3 Size => Max - Min;
}

/// <summary>Immutable world anchor for an overlay label; labels do not affect picking or depth.</summary>
public sealed class WorldLabel
{
    /// <summary>Text to display.</summary>
    public string Text { get; }
    /// <summary>Anchor in world coordinates.</summary>
    public Vector3 Position { get; }
    /// <summary>Opaque packed 0xAARRGGBB text color.</summary>
    public uint Color { get; }

    /// <summary>Creates a label with a finite world anchor.</summary>
    public WorldLabel(string text, Vector3 position, uint color = 0xFF202738)
    {
        ArgumentNullException.ThrowIfNull(text);
        Validation.Vector(position, nameof(position));
        if ((color >> 24) != 255) throw new ArgumentOutOfRangeException(nameof(color));
        Text = text;
        Position = position;
        Color = color;
    }
}

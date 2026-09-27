namespace Simple3D.Core;

/// <summary>A scene composed of independent colored shapes. Mutate on the UI thread.</summary>
public sealed class Scene
{
    private readonly List<Shape> _shapes = new();
    public Scene() => Shapes = _shapes.AsReadOnly();
    public IReadOnlyList<Shape> Shapes { get; }
    public Scene Add(Shape shape) { _shapes.Add(shape ?? throw new ArgumentNullException(nameof(shape))); return this; }
    public Scene Clear() { _shapes.Clear(); return this; }
}

using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>A posable humanoid character for the Mirror stage.</summary>
/// <param name="Shape">The character in its rest pose, for example a loaded VRM model.</param>
/// <param name="Skeleton">The skeleton that poses it.</param>
/// <param name="HumanoidBones">VRM humanoid bone name → joint index.</param>
/// <param name="FacesPositiveZ">Whether the model looks along +Z in its own space (VRM 1.0 does; VRM 0.x looks along -Z).</param>
/// <param name="Height">Height on the stage in world units.</param>
public sealed record MirrorCharacter(Shape Shape, Skeleton Skeleton, IReadOnlyDictionary<string, int> HumanoidBones, bool FacesPositiveZ, float Height = 1.6f)
{
    /// <summary>Applies facial expression intensities (VRM preset names such as "a", "joy", "blink_l", each in [0,1]) to a
    /// posed character; null when the model has no expressions.</summary>
    public Func<Shape, IReadOnlyDictionary<string, float>, Shape>? Express { get; init; }
}

/// <summary>A night-time pond built from library primitives, with characters standing side by side on a mossy island, facing the camera.</summary>
public sealed class MirrorStage
{
    private const float IslandTop = 0;
    private readonly Shape[] _placed;
    private readonly List<(Shape Shape, Vector3 Home, float Phase)> _fireflies = new();

    /// <summary>The scene to display.</summary>
    public Scene Scene { get; } = new() { AmbientLight = .46f, Light = new DirectionalLight(new(-.35f, -.75f, -.55f), 0xFFFFF1DC, .7f) };
    /// <summary>A camera framing the characters' whole bodies and the island.</summary>
    public Camera Camera { get; }
    /// <summary>The characters, in stage order from left to right.</summary>
    public IReadOnlyList<MirrorCharacter> Characters { get; }
    /// <summary>Per character: rotation from its skeleton space to view space, for <see cref="HumanoidRetargeter"/>.</summary>
    public IReadOnlyList<Quaternion> CharacterToView { get; }

    /// <summary>Places characters side by side, normalizing their heights and turning them toward the camera.</summary>
    public MirrorStage(IReadOnlyList<MirrorCharacter> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count is 0 or > 3) throw new ArgumentException("Place one to three characters.", nameof(characters));
        Characters = characters;
        _placed = new Shape[characters.Count];
        var toView = new Quaternion[characters.Count];
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            var bounds = new Scene().Add(character.Shape).GetBounds() ?? throw new ArgumentException("A character has no geometry.", nameof(characters));
            var scale = character.Height / bounds.Size.Y;
            var yaw = character.FacesPositiveZ ? 0 : MathF.PI;
            toView[i] = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            // Feet centered on their spot; spread along X, the outer ones a little back so stretched arms rarely meet.
            var spot = characters.Count == 1 ? Vector3.Zero : new Vector3((i - (characters.Count - 1) / 2f) * 1.1f, 0, -.25f * MathF.Abs(i - (characters.Count - 1) / 2f));
            var feet = Vector3.Transform(new Vector3(bounds.Center.X, bounds.Min.Y, bounds.Center.Z) * scale, toView[i]);
            _placed[i] = Shape.Group(character.Shape).Named("Character").Scaled(scale).Rotated(0, yaw, 0)
                .At(spot.X - feet.X, IslandTop - feet.Y, spot.Z - feet.Z);
        }
        CharacterToView = Array.AsReadOnly(toView);
        var tallest = characters.Max(c => c.Height);
        Camera = new(characters.Count == 1 ? 4.6f : 5.6f, 0, .12f) { Target = new(0, tallest * .52f, 0) };
        BuildPond();
    }

    /// <summary>Shows or hides the characters.</summary>
    public void ShowCharacters(bool visible)
    {
        foreach (var placed in _placed)
        {
            var present = Scene.Shapes.Contains(placed);
            if (visible && !present) Scene.Add(placed);
            else if (!visible && present) Scene.Remove(placed);
        }
    }

    /// <summary>Replaces character <paramref name="index"/> with <paramref name="change"/> of its current shape (shown or not).</summary>
    public void Update(int index, Func<Shape, Shape> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var current = _placed[index];
        var next = change(current);
        if (ReferenceEquals(next, current)) return;
        Scene.Replace(current, next);
        _placed[index] = next;
    }

    /// <summary>Animates the ambient scenery: drifting fireflies and bobbing lily pads.</summary>
    public void Animate(float seconds)
    {
        for (var i = 0; i < _fireflies.Count; i++)
        {
            var (shape, home, phase) = _fireflies[i];
            var t = seconds * .6f + phase;
            var next = shape.At(home.X + .25f * MathF.Sin(t * 1.3f), home.Y + .18f * MathF.Sin(t * 2.1f), home.Z + .2f * MathF.Cos(t));
            Scene.Replace(shape, next);
            _fireflies[i] = (next, home, phase);
        }
    }

    private void BuildPond()
    {
        Scene.Add(Shape.Cylinder(0xFF1E5A78).Named("Pond").Scaled(9, .05f, 7).At(0, -.32f, -1));
        Scene.Add(Shape.Cylinder(0xFF5C8F3E).Named("Island").Scaled(2.4f, .3f, 1.9f).At(0, IslandTop - .15f, 0));
        Scene.Add(Shape.Cylinder(0xFF6EA84A).Named("Moss").Scaled(2.1f, .04f, 1.6f).At(0, IslandTop - .005f, 0));
        foreach (var (x, z, size, angle) in new[] { (-2.1f, .9f, .55f, .4f), (1.9f, 1.3f, .45f, 1.7f), (2.6f, -.2f, .62f, 2.9f), (-2.8f, -.6f, .5f, .9f), (-1.2f, 1.9f, .35f, 2.2f) })
            Scene.Add(LilyPad(size).Rotated(0, angle, 0).At(x, -.28f, z));
        Scene.Add(Mushroom(0xFFE0533D, .42f).At(-.95f, IslandTop, -.35f));
        Scene.Add(Mushroom(0xFFF2A541, .28f).At(-.65f, IslandTop, .45f));
        Scene.Add(Mushroom(0xFFE0533D, .22f).At(.85f, IslandTop, .5f));
        Scene.Add(Tree(1.25f).At(1.55f, IslandTop, -1.05f));
        Scene.Add(Tree(.95f).At(-1.6f, IslandTop, -1.2f));
        foreach (var (x, z, s) in new[] { (1.15f, -.15f, .28f), (-1.25f, .15f, .2f), (.45f, -.9f, .18f) })
            Scene.Add(Shape.Sphere(0xFF8A93A3).Named("Rock").Scaled(s * 1.4f, s * .8f, s).At(x, IslandTop + s * .25f, z));
        foreach (var (x, z, h) in new[] { (-2.3f, -.2f, .9f), (-2.45f, .05f, .7f), (2.25f, .55f, .8f), (2.4f, .3f, 1.05f) })
            Scene.Add(Shape.Cylinder(0xFF7FA650).Named("Reed").Scaled(.045f, h, .045f).At(x, -.3f + h / 2, z));
        var random = new Random(7);
        for (var i = 0; i < 9; i++)
        {
            var home = new Vector3(random.NextSingle() * 5 - 2.5f, .5f + random.NextSingle() * 1.6f, random.NextSingle() * 2.4f - 1.6f);
            var firefly = Shape.Sphere().WithMaterial(new Material(0xFFFFE680, lit: false)).Named("Firefly").Scaled(.06f).At(home.X, home.Y, home.Z);
            Scene.Add(firefly);
            _fireflies.Add((firefly, home, i * 1.7f));
        }
    }

    private static Shape LilyPad(float size) => Shape.Group(
        Shape.Cylinder(0xFF3E8C4A).Named("Lily pad").Scaled(size, .03f, size),
        Shape.Sphere(0xFFF6B8D1).Named("Lily flower").Scaled(size * .28f, size * .18f, size * .28f).At(size * .18f, .05f, 0));

    private static Shape Mushroom(uint cap, float size) => Shape.Group(
        Shape.Cylinder(0xFFF1E6D0).Named("Mushroom stem").Scaled(size * .32f, size * .7f, size * .32f).At(0, size * .35f, 0),
        Shape.Sphere(cap).Named("Mushroom cap").Scaled(size, size * .55f, size).At(0, size * .72f, 0),
        Shape.Sphere(0xFFFFFFFF).Named("Mushroom spot").Scaled(size * .16f).At(size * .25f, size * .93f, size * .22f),
        Shape.Sphere(0xFFFFFFFF).Named("Mushroom spot").Scaled(size * .12f).At(-size * .28f, size * .9f, -size * .1f));

    private static Shape Tree(float height) => Shape.Group(
        Shape.Cylinder(0xFF7A5434).Named("Trunk").Scaled(height * .12f, height * .5f, height * .12f).At(0, height * .25f, 0),
        Shape.Sphere(0xFF3F7F45).Named("Foliage").Scaled(height * .62f).At(0, height * .68f, 0),
        Shape.Sphere(0xFF4C9152).Named("Foliage").Scaled(height * .44f).At(height * .16f, height * .98f, height * .05f));
}

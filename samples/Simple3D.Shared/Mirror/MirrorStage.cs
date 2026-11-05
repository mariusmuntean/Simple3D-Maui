using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>A night-time pond built from library primitives, with a character standing on a mossy island facing the camera.</summary>
public sealed class MirrorStage
{
    /// <summary>The character's height in world units after normalization.</summary>
    public const float CharacterHeight = 1.6f;
    private const float IslandTop = 0;
    private readonly Shape _character;
    private Shape _placedCharacter;
    private readonly List<(Shape Shape, Vector3 Home, float Phase)> _fireflies = new();

    /// <summary>The scene to display.</summary>
    public Scene Scene { get; } = new() { AmbientLight = .46f, Light = new DirectionalLight(new(-.35f, -.75f, -.55f), 0xFFFFF1DC, .7f) };
    /// <summary>A camera framing the character's whole body and the island.</summary>
    public Camera Camera { get; } = new(4.6f, 0, .12f) { Target = new(0, CharacterHeight * .52f, 0) };
    /// <summary>Rotation from the character's skeleton space to view space, for <see cref="HumanoidRetargeter"/>.</summary>
    public Quaternion CharacterToView { get; }

    /// <summary>Places a character, normalizing its height and turning it toward the camera.</summary>
    /// <param name="character">The posable character (for example a loaded VRM model) in its rest pose.</param>
    /// <param name="facesPositiveZ">Whether the model looks along +Z in its own space (VRM 1.0 does; VRM 0.x looks along -Z).</param>
    public MirrorStage(Shape character, bool facesPositiveZ)
    {
        ArgumentNullException.ThrowIfNull(character);
        var bounds = new Scene().Add(character).GetBounds() ?? throw new ArgumentException("The character has no geometry.", nameof(character));
        var scale = CharacterHeight / bounds.Size.Y;
        var yaw = facesPositiveZ ? 0 : MathF.PI;
        CharacterToView = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        // Center the feet on the island; the retargeter keeps them grounded from here.
        var feet = Vector3.Transform(new Vector3(bounds.Center.X, bounds.Min.Y, bounds.Center.Z) * scale, CharacterToView);
        _character = Shape.Group(character).Named("Character").Scaled(scale).Rotated(0, yaw, 0).At(-feet.X, IslandTop - feet.Y, -feet.Z);
        _placedCharacter = _character;
        BuildPond();
    }

    /// <summary>Shows or hides the character.</summary>
    public void ShowCharacter(bool visible)
    {
        var present = Scene.Shapes.Contains(_placedCharacter);
        if (visible && !present) Scene.Add(_placedCharacter);
        else if (!visible && present) Scene.Remove(_placedCharacter);
    }

    /// <summary>Applies a pose to the character (shown or not).</summary>
    public void Pose(SkeletonPose pose)
    {
        var next = _placedCharacter.WithPose(pose);
        if (!Scene.Replace(_placedCharacter, next) && Scene.Shapes.Contains(_placedCharacter)) return;
        _placedCharacter = next;
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

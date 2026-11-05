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
    /// <summary>Radius of the character's torso in skeleton units, which limbs must stay outside; 0 disables the check.</summary>
    public float TorsoRadius { get; init; }

    /// <summary>Describes a loaded VRM character: its first skeleton and humanoid bones, which way it faces (from its
    /// toes, so VRM 0.x and 1.0 both work) and how thick its torso is (from the vertices the torso bones move).</summary>
    public static MirrorCharacter FromVrm(GltfModel model, Shape shape, float height = 1.6f)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shape);
        var skeleton = model.Skeletons[0];
        var bones = model.HumanoidBones;
        var rest = skeleton.RestPose.WorldTransforms;
        var facesPositiveZ = !(bones.TryGetValue("leftFoot", out var foot) && bones.TryGetValue("leftToes", out var toes)) ||
            rest[toes].Translation.Z > rest[foot].Translation.Z;
        return new(shape, skeleton, bones, facesPositiveZ, height) { TorsoRadius = EstimateTorsoRadius(shape, skeleton, bones) };
    }

    // Distance from the spine axis that most torso vertices lie within, in the rest pose.
    private static float EstimateTorsoRadius(Shape shape, Skeleton skeleton, IReadOnlyDictionary<string, int> bones)
    {
        var torso = new[] { "hips", "spine", "chest", "upperChest" }.Where(bones.ContainsKey).Select(b => bones[b]).ToHashSet();
        if (!bones.TryGetValue("hips", out var hips) || !bones.TryGetValue(bones.ContainsKey("neck") ? "neck" : "head", out var top)) return 0;
        var rest = skeleton.RestPose.WorldTransforms;
        Vector3 a = rest[hips].Translation, b = rest[top].Translation;
        var distances = new List<float>();
        foreach (var mesh in Leaves(shape).Select(l => l.SkinnedMesh).OfType<SkinnedMesh>().Where(m => ReferenceEquals(m.Skeleton, skeleton)))
            for (var v = 0; v < mesh.Mesh.Vertices.Count; v++)
            {
                var (joints, weights) = mesh.Influences[v];
                if (weights.X < .5f || !torso.Contains(joints.X)) continue;
                var p = mesh.Mesh.Vertices[v];
                var t = Math.Clamp(Vector3.Dot(p - a, b - a) / (b - a).LengthSquared(), 0, 1);
                distances.Add(Vector3.Distance(p, a + (b - a) * t));
            }
        if (distances.Count == 0) return 0;
        distances.Sort();
        return distances[(int)(distances.Count * .8f)];
        static IEnumerable<Shape> Leaves(Shape s) => s.Children.Count == 0 ? [s] : s.Children.SelectMany(Leaves);
    }

    /// <summary>Applies facial expression intensities (VRM preset names such as "a", "joy", "blink_l", each in [0,1]) to a
    /// posed character; null when the model has no expressions.</summary>
    public Func<Shape, IReadOnlyDictionary<string, float>, Shape>? Express { get; init; }
}

/// <summary>A night-time pond built from library primitives, with a character standing on a mossy island facing the camera.</summary>
public sealed class MirrorStage
{
    private const float IslandTop = 0;
    private Shape _placed;
    private readonly List<(Shape Shape, Vector3 Home, float Phase)> _fireflies = new();

    /// <summary>The scene to display.</summary>
    public Scene Scene { get; } = new() { AmbientLight = .46f, Light = new DirectionalLight(new(-.35f, -.75f, -.55f), 0xFFFFF1DC, .7f) };
    /// <summary>A camera framing the character's whole body and the island.</summary>
    public Camera Camera { get; }
    /// <summary>The character.</summary>
    public MirrorCharacter Character { get; }
    /// <summary>Rotation from the character's skeleton space to view space, for <see cref="HumanoidRetargeter"/>.</summary>
    public Quaternion CharacterToView { get; }

    /// <summary>Places the character, normalizing its height and turning it toward the camera.</summary>
    public MirrorStage(MirrorCharacter character)
    {
        Character = character ?? throw new ArgumentNullException(nameof(character));
        var bounds = new Scene().Add(character.Shape).GetBounds() ?? throw new ArgumentException("The character has no geometry.", nameof(character));
        var scale = character.Height / bounds.Size.Y;
        var yaw = character.FacesPositiveZ ? 0 : MathF.PI;
        CharacterToView = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        // Center the feet on the island; the retargeter keeps them grounded from here.
        var feet = Vector3.Transform(new Vector3(bounds.Center.X, bounds.Min.Y, bounds.Center.Z) * scale, CharacterToView);
        _placed = Shape.Group(character.Shape).Named("Character").Scaled(scale).Rotated(0, yaw, 0).At(-feet.X, IslandTop - feet.Y, -feet.Z);
        Camera = new(4.6f, 0, .12f) { Target = new(0, character.Height * .52f, 0) };
        BuildPond();
    }

    /// <summary>Shows or hides the character.</summary>
    public void ShowCharacter(bool visible)
    {
        var present = Scene.Shapes.Contains(_placed);
        if (visible && !present) Scene.Add(_placed);
        else if (!visible && present) Scene.Remove(_placed);
    }

    /// <summary>Replaces the character with <paramref name="change"/> of its current shape (shown or not).</summary>
    public void Update(Func<Shape, Shape> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(_placed);
        if (ReferenceEquals(next, _placed)) return;
        Scene.Replace(_placed, next);
        _placed = next;
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

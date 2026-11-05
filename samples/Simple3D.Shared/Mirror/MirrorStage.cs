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
        return new(shape, skeleton, bones, facesPositiveZ, height)
        {
            TorsoRadius = EstimateTorsoRadius(shape, skeleton, bones),
            Express = model.Expressions.Count == 0 ? null : model.ApplyExpressions,
        };
    }

    // Distance from the spine axis that most torso vertices lie beyond, in the rest pose.
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
        // The core, not the widest part (bellies, sleeves): limbs may graze the outside, but must not cross the middle.
        return distances[(int)(distances.Count * .6f)];
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
    private const float LeaveSeconds = .35f, EnterDelay = .18f, EnterSeconds = .55f, SparkleSeconds = .8f;
    private static readonly uint[] SparkleColors = [0xFFFFE680, 0xFFFF9EC7, 0xFF9EE7FF, 0xFFB8FF9E];
    private readonly List<(Shape Shape, Vector3 Home, float Phase)> _fireflies = new();
    // The posed character, and the group around it that the swap animation scales and spins about the feet.
    private Shape _body, _shown;
    private Shape? _leaving;
    private float? _swapStart;
    private float _now;
    private Vector3 _cameraFrom, _cameraTo;
    private readonly List<(Shape Shape, Vector3 Direction)> _sparkles = new();
    private bool _visible;

    /// <summary>The scene to display.</summary>
    public Scene Scene { get; } = new() { AmbientLight = .46f, Light = new DirectionalLight(new(-.35f, -.75f, -.55f), 0xFFFFF1DC, .7f) };
    /// <summary>A camera framing the character's whole body and the island.</summary>
    public Camera Camera { get; private set; }
    /// <summary>The character.</summary>
    public MirrorCharacter Character { get; private set; }
    /// <summary>Rotation from the character's skeleton space to view space, for <see cref="HumanoidRetargeter"/>.</summary>
    public Quaternion CharacterToView { get; private set; }
    /// <summary>The character as currently posed, without the stage's placement.</summary>
    public Shape CharacterShape => _body;
    /// <summary>Whether a character swap is animating; drive <see cref="Animate"/> at the display rate meanwhile.</summary>
    public bool IsSwapping => _swapStart is not null;

    /// <summary>Places the character, normalizing its height and turning it toward the camera.</summary>
    public MirrorStage(MirrorCharacter character)
    {
        Character = character ?? throw new ArgumentNullException(nameof(character));
        (_body, var toView) = Place(character);
        CharacterToView = toView;
        _shown = Shape.Group(_body);
        Camera = new(4.6f, 0, .12f) { Target = CameraTarget(character) };
        BuildPond();
    }

    /// <summary>Frames the whole body, outstretched arms and some island for the viewport.</summary>
    public void Frame(float aspect)
    {
        if (!float.IsFinite(aspect) || aspect <= 0) return;
        // Leave Target to the swap animation; portrait needs extra distance for the arms.
        var halfExtent = Character.Height * .75f * MathF.Max(1, 1 / aspect);
        var distance = halfExtent / MathF.Tan(Camera.FieldOfView / 2) + Character.Height * .25f;
        Camera = new(distance, 0, .12f) { Target = Camera.Target };
    }

    // Normalizes the height, turns the model toward the camera and stands its feet at the origin, the island's center.
    private static (Shape Placed, Quaternion ToView) Place(MirrorCharacter character)
    {
        var bounds = new Scene().Add(character.Shape).GetBounds() ?? throw new ArgumentException("The character has no geometry.", nameof(character));
        var scale = character.Height / bounds.Size.Y;
        var yaw = character.FacesPositiveZ ? 0 : MathF.PI;
        var toView = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var feet = Vector3.Transform(new Vector3(bounds.Center.X, bounds.Min.Y, bounds.Center.Z) * scale, toView);
        return (Shape.Group(character.Shape).Named("Character").Scaled(scale).Rotated(0, yaw, 0).At(-feet.X, IslandTop - feet.Y, -feet.Z), toView);
    }

    private static Vector3 CameraTarget(MirrorCharacter character) => new(0, character.Height * .52f, 0);

    /// <summary>Shows or hides the character.</summary>
    public void ShowCharacter(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;
        if (visible) Scene.Add(_shown);
        else
        {
            Scene.Remove(_shown);
            FinishSwap();
        }
    }

    /// <summary>Replaces the character with <paramref name="change"/> of its current shape (shown or not).</summary>
    public void Update(Func<Shape, Shape> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(_body);
        if (ReferenceEquals(next, _body)) return;
        _body = next;
        Show(Entering());
    }

    /// <summary>Replaces the character. While it is shown, the old one spins away in a puff of sparkles and the new one
    /// pops up in its place; otherwise the swap is immediate. Load the model before calling: this only rearranges the scene.</summary>
    public void Swap(MirrorCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var (placed, toView) = Place(character);
        FinishSwap();
        Character = character;
        CharacterToView = toView;
        _cameraFrom = Camera.Target;
        _cameraTo = CameraTarget(character);
        if (!_visible)
        {
            _body = placed;
            _shown = Shape.Group(_body);
            Camera.Target = _cameraTo;
            return;
        }
        // The old character keeps its last pose while it leaves; the new one starts nearly invisible, so its first
        // GPU upload happens before it is meant to be seen.
        _leaving = _shown;
        _body = placed;
        _shown = Shape.Group(_body).Scaled(.001f);
        Scene.Add(_shown);
        _swapStart = _now;
        var chest = new Vector3(0, Character.Height * .55f, 0);
        var random = new Random(_sparkles.Count + (int)(_now * 1000));
        for (var i = 0; i < 16; i++)
        {
            var angle = i * MathF.Tau / 16 + random.NextSingle() * .3f;
            var direction = new Vector3(MathF.Cos(angle), .6f * random.NextSingle() - .1f, MathF.Sin(angle) * .5f) * (.7f + .5f * random.NextSingle());
            var sparkle = Shape.Sphere().WithMaterial(new Material(SparkleColors[i % SparkleColors.Length], lit: false)).Scaled(.08f).At(chest.X, chest.Y, chest.Z);
            Scene.Add(sparkle);
            _sparkles.Add((sparkle, direction));
        }
    }

    /// <summary>Animates the ambient scenery (drifting fireflies) and any character swap.</summary>
    public void Animate(float seconds)
    {
        _now = seconds;
        for (var i = 0; i < _fireflies.Count; i++)
        {
            var (shape, home, phase) = _fireflies[i];
            var t = seconds * .6f + phase;
            var next = shape.At(home.X + .25f * MathF.Sin(t * 1.3f), home.Y + .18f * MathF.Sin(t * 2.1f), home.Z + .2f * MathF.Cos(t));
            Scene.Replace(shape, next);
            _fireflies[i] = (next, home, phase);
        }
        if (_swapStart is not { } start) return;
        var elapsed = seconds - start;
        if (_leaving is { } leaving)
        {
            var u = Math.Clamp(elapsed / LeaveSeconds, 0, 1);
            if (u >= 1) { Scene.Remove(leaving); _leaving = null; }
            else
            {
                // Spin up, shrink and sink: ease in, so it starts gently and is gone quickly.
                var next = leaving.Scaled(MathF.Max(.001f, 1 - u * u)).Rotated(0, u * u * MathF.PI, 0).At(0, -.15f * u * u, 0);
                Scene.Replace(leaving, next);
                _leaving = next;
            }
        }
        Show(Entering());
        var c = SmoothStep(Math.Clamp(elapsed / (EnterDelay + EnterSeconds), 0, 1));
        Camera.Target = Vector3.Lerp(_cameraFrom, _cameraTo, c);
        var center = new Vector3(0, Character.Height * .55f, 0);
        var s = Math.Clamp(elapsed / SparkleSeconds, 0, 1);
        for (var i = 0; i < _sparkles.Count; i++)
        {
            var (sparkle, direction) = _sparkles[i];
            var p = center + direction * (1 - (1 - s) * (1 - s));
            var next = sparkle.Scaled(MathF.Max(.001f, .08f * (1 - s))).At(p.X, p.Y + .3f * s, p.Z);
            Scene.Replace(sparkle, next);
            _sparkles[i] = (next, direction);
        }
        if (elapsed >= MathF.Max(EnterDelay + EnterSeconds, SparkleSeconds)) FinishSwap();
    }

    // The entering character's transform at the current time: grows with a little overshoot from a quarter turn away.
    private Shape Entering()
    {
        if (_swapStart is not { } start) return Shape.Group(_body);
        var v = Math.Clamp((_now - start - EnterDelay) / EnterSeconds, 0, 1);
        if (v <= 0) return Shape.Group(_body).Scaled(.001f);
        const float overshoot = 1.7f;
        var back = 1 + (overshoot + 1) * MathF.Pow(v - 1, 3) + overshoot * MathF.Pow(v - 1, 2);
        return Shape.Group(_body).Scaled(MathF.Max(.001f, back)).Rotated(0, -MathF.PI / 2 * MathF.Pow(1 - v, 3), 0);
    }

    private void Show(Shape next)
    {
        if (_visible) Scene.Replace(_shown, next);
        _shown = next;
    }

    private void FinishSwap()
    {
        if (_swapStart is null) return;
        _swapStart = null;
        if (_leaving is { } leaving) Scene.Remove(leaving);
        _leaving = null;
        foreach (var (sparkle, _) in _sparkles) Scene.Remove(sparkle);
        _sparkles.Clear();
        Camera.Target = _cameraTo;
        Show(Shape.Group(_body));
    }

    private static float SmoothStep(float x) => x * x * (3 - 2 * x);

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

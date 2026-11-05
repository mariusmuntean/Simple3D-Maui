using System.Numerics;

namespace Simple3D.Core;

/// <summary>One joint of a <see cref="Skeleton"/>: its parent and rest transform relative to that parent.</summary>
/// <param name="Name">Joint name, unique within its skeleton.</param>
/// <param name="Parent">Index of the parent joint, or -1 for a root. Parents precede their children.</param>
/// <param name="Translation">Rest translation relative to the parent.</param>
/// <param name="Rotation">Rest rotation relative to the parent.</param>
/// <param name="Scale">Rest scale relative to the parent.</param>
/// <param name="InverseBind">Transforms mesh coordinates into this joint's space at bind time.</param>
public readonly record struct Joint(string Name, int Parent, Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4 InverseBind)
{
    internal Matrix4x4 Local(Quaternion rotation) =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(Translation);
}

/// <summary>An immutable joint hierarchy shared by skinned meshes and their poses.</summary>
public sealed class Skeleton
{
    /// <summary>Maximum number of joints; native renderers upload one matrix per joint each frame.</summary>
    public const int MaximumJoints = 128;
    private readonly Dictionary<string, int> _byName;

    /// <summary>Joints ordered so that every parent precedes its children.</summary>
    public IReadOnlyList<Joint> Joints { get; }
    /// <summary>Transform applied above the root joints, for example from nodes that are not joints.</summary>
    public Matrix4x4 RootTransform { get; }
    /// <summary>The pose with every joint at its rest rotation.</summary>
    public SkeletonPose RestPose { get; }

    /// <summary>Copies and validates joints: unique names, parents before children, finite transforms and positive scales.</summary>
    public Skeleton(IEnumerable<Joint> joints, Matrix4x4? rootTransform = null)
    {
        ArgumentNullException.ThrowIfNull(joints);
        var copy = joints.Take(MaximumJoints + 1).ToArray();
        if (copy.Length is 0 or > MaximumJoints) throw new ArgumentException($"A skeleton needs 1 to {MaximumJoints} joints.", nameof(joints));
        _byName = new(StringComparer.Ordinal);
        for (var i = 0; i < copy.Length; i++)
        {
            var joint = copy[i];
            if (string.IsNullOrEmpty(joint.Name) || !_byName.TryAdd(joint.Name, i)) throw new ArgumentException("Joint names must be unique and nonempty.", nameof(joints));
            if (joint.Parent < -1 || joint.Parent >= i) throw new ArgumentException("Joint parents must precede their children.", nameof(joints));
            Validation.Vector(joint.Translation, nameof(joints));
            Validation.Vector(joint.Scale, nameof(joints));
            if (joint.Scale.X <= 0 || joint.Scale.Y <= 0 || joint.Scale.Z <= 0) throw new ArgumentOutOfRangeException(nameof(joints), "Joint scales must be positive.");
            copy[i] = joint with { Rotation = Normalized(joint.Rotation, nameof(joints)) };
            var bind = joint.InverseBind;
            // CPU skinning drops w and GPU skinning keeps it: only affine matrices render the same on both.
            if (!Finite(bind) || MathF.Abs(bind.M14) + MathF.Abs(bind.M24) + MathF.Abs(bind.M34) + MathF.Abs(bind.M44 - 1) > 1e-4f)
                throw new ArgumentOutOfRangeException(nameof(joints), "Inverse bind matrices must be finite and affine.");
        }
        var root = rootTransform ?? Matrix4x4.Identity;
        if (!Finite(root)) throw new ArgumentOutOfRangeException(nameof(rootTransform));
        Joints = Array.AsReadOnly(copy);
        RootTransform = root;
        RestPose = new SkeletonPose(this, copy.Select(j => j.Rotation).ToArray(), Vector3.Zero);
    }

    /// <summary>Returns the index of a named joint, or -1.</summary>
    public int IndexOf(string name) => _byName.TryGetValue(name, out var index) ? index : -1;

    internal static Quaternion Normalized(Quaternion q, string name)
    {
        if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W)) throw new ArgumentOutOfRangeException(name);
        var length = q.Length();
        if (length < 1e-6f) throw new ArgumentOutOfRangeException(name, "Rotations must be nonzero quaternions.");
        return Quaternion.Normalize(q);
    }

    internal static bool Finite(Matrix4x4 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
        float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
        float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
        float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}

/// <summary>An immutable pose: one local rotation per joint plus an offset added to the root joints' rest translation.</summary>
public sealed class SkeletonPose
{
    private readonly Quaternion[] _rotations;
    private Matrix4x4[]? _world, _skinning;
    private IReadOnlyList<Matrix4x4>? _worldView;

    /// <summary>The posed skeleton.</summary>
    public Skeleton Skeleton { get; }
    /// <summary>Local joint rotations, relative to each parent.</summary>
    public IReadOnlyList<Quaternion> Rotations { get; }
    /// <summary>Offset added to the translation of root joints.</summary>
    public Vector3 RootOffset { get; }

    internal SkeletonPose(Skeleton skeleton, Quaternion[] rotations, Vector3 rootOffset)
    {
        Skeleton = skeleton;
        _rotations = rotations;
        Rotations = Array.AsReadOnly(rotations);
        RootOffset = rootOffset;
    }

    /// <summary>Creates a pose from one local rotation per joint. Rotations are copied and normalized.</summary>
    public static SkeletonPose Create(Skeleton skeleton, ReadOnlySpan<Quaternion> rotations, Vector3 rootOffset = default)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (rotations.Length != skeleton.Joints.Count) throw new ArgumentException("Supply one rotation per joint.", nameof(rotations));
        Validation.Vector(rootOffset, nameof(rootOffset));
        var copy = new Quaternion[rotations.Length];
        for (var i = 0; i < copy.Length; i++) copy[i] = Skeleton.Normalized(rotations[i], nameof(rotations));
        return new(skeleton, copy, rootOffset);
    }

    /// <summary>Blends two poses of the same skeleton: each joint rotation is spherically interpolated and the root offset
    /// linearly, so <paramref name="amount"/> 0 gives <paramref name="from"/> and 1 gives <paramref name="to"/>. Use it to
    /// crossfade animations or to ease toward a tracked pose.</summary>
    public static SkeletonPose Blend(SkeletonPose from, SkeletonPose to, float amount)
    {
        ArgumentNullException.ThrowIfNull(from); ArgumentNullException.ThrowIfNull(to);
        if (!ReferenceEquals(from.Skeleton, to.Skeleton)) throw new ArgumentException("Both poses must use the same skeleton.", nameof(to));
        if (!float.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
        amount = Math.Clamp(amount, 0, 1);
        if (amount == 0) return from;
        if (amount == 1) return to;
        var rotations = new Quaternion[from._rotations.Length];
        for (var i = 0; i < rotations.Length; i++) rotations[i] = Quaternion.Normalize(Quaternion.Slerp(from._rotations[i], to._rotations[i], amount));
        return new(from.Skeleton, rotations, Vector3.Lerp(from.RootOffset, to.RootOffset, amount));
    }

    /// <summary>Joint transforms in the skinned mesh's coordinate system, including <see cref="Skeleton.RootTransform"/>.</summary>
    public IReadOnlyList<Matrix4x4> WorldTransforms => _worldView ??= Array.AsReadOnly(_world ??= ComputeWorld());

    /// <summary>Per-joint skinning matrices: inverse bind followed by the posed joint transform.</summary>
    internal Matrix4x4[] SkinningMatrices
    {
        get
        {
            if (_skinning is { } cached) return cached;
            var world = _world ??= ComputeWorld();
            var result = new Matrix4x4[world.Length];
            for (var i = 0; i < result.Length; i++) result[i] = Skeleton.Joints[i].InverseBind * world[i];
            return _skinning = result;
        }
    }

    private Matrix4x4[] ComputeWorld()
    {
        var joints = Skeleton.Joints;
        var world = new Matrix4x4[joints.Count];
        for (var i = 0; i < world.Length; i++)
        {
            var joint = joints[i];
            var local = joint.Local(_rotations[i]);
            if (joint.Parent < 0)
            {
                local.Translation += RootOffset;
                world[i] = local * Skeleton.RootTransform;
            }
            else world[i] = local * world[joint.Parent];
        }
        return world;
    }
}

/// <summary>Up to four joint influences of one vertex. Weights are normalized when the skinned mesh is created.</summary>
/// <param name="Joints">Joint indices, packed as X, Y, Z and W.</param>
/// <param name="Weights">Nonnegative weights matching <paramref name="Joints"/>.</param>
public readonly record struct VertexInfluence(Int4 Joints, Vector4 Weights);

/// <summary>Four joint indices.</summary>
public readonly record struct Int4(int X, int Y, int Z, int W)
{
    internal int this[int i] => i switch { 0 => X, 1 => Y, 2 => Z, _ => W };
}

/// <summary>A blend shape: per-vertex offsets added to bind-pose geometry, scaled by a weight in [0,1].</summary>
public sealed class MorphTarget
{
    /// <summary>Optional name, for example a VRM expression's target.</summary>
    public string? Name { get; }
    /// <summary>Position offset of every vertex.</summary>
    public IReadOnlyList<Vector3> PositionDeltas { get; }
    /// <summary>Optional normal offset of every vertex.</summary>
    public IReadOnlyList<Vector3>? NormalDeltas { get; }
    // Vertices this target moves, so blending skips the untouched majority.
    internal int[] Affected { get; }

    /// <summary>Copies finite offsets; normal offsets, when present, must match the position count.</summary>
    public MorphTarget(string? name, IEnumerable<Vector3> positionDeltas, IEnumerable<Vector3>? normalDeltas = null)
    {
        ArgumentNullException.ThrowIfNull(positionDeltas);
        var positions = positionDeltas.Take(Mesh.MaximumElements + 1).ToArray();
        if (positions.Length > Mesh.MaximumElements) throw new ArgumentException("Too many morph offsets.", nameof(positionDeltas));
        foreach (var d in positions) Validation.Vector(d, nameof(positionDeltas));
        Vector3[]? normals = null;
        if (normalDeltas is not null)
        {
            normals = normalDeltas.Take(positions.Length + 1).ToArray();
            if (normals.Length != positions.Length) throw new ArgumentException("Supply one normal offset per position offset.", nameof(normalDeltas));
            foreach (var d in normals) Validation.Vector(d, nameof(normalDeltas));
        }
        Name = name;
        PositionDeltas = Array.AsReadOnly(positions);
        NormalDeltas = normals is null ? null : Array.AsReadOnly(normals);
        Affected = Enumerable.Range(0, positions.Length).Where(i => positions[i] != Vector3.Zero || (normals?[i] ?? Vector3.Zero) != Vector3.Zero).ToArray();
    }
}

/// <summary>Immutable bind-pose geometry deformed by a skeleton.</summary>
public sealed class SkinnedMesh
{
    /// <summary>Geometry in its bind pose.</summary>
    public Mesh Mesh { get; }
    /// <summary>The skeleton that deforms the mesh.</summary>
    public Skeleton Skeleton { get; }
    /// <summary>One influence per mesh vertex.</summary>
    public IReadOnlyList<VertexInfluence> Influences { get; }
    /// <summary>Blend shapes applied before skinning; empty when the mesh has none.</summary>
    public IReadOnlyList<MorphTarget> MorphTargets { get; }
    /// <summary>Maximum number of morph targets per mesh.</summary>
    public const int MaximumMorphTargets = 64;
    internal Vector4[] Weights { get; }
    internal Int4[] JointIndices { get; }
    // Per joint: bind-space bounds of the vertices it influences, for conservative posed bounds.
    internal (Vector3 Min, Vector3 Max)[] JointBounds { get; }

    /// <summary>Validates one influence per vertex: joint indices inside the skeleton and a positive weight sum.
    /// Morph targets, if any, need one offset per vertex.</summary>
    public SkinnedMesh(Mesh mesh, Skeleton skeleton, IEnumerable<VertexInfluence> influences, IEnumerable<MorphTarget>? morphTargets = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(influences);
        var copy = influences.Take(mesh.Vertices.Count + 1).ToArray();
        if (copy.Length != mesh.Vertices.Count) throw new ArgumentException("Supply one influence per vertex.", nameof(influences));
        var targets = morphTargets?.Take(MaximumMorphTargets + 1).ToArray() ?? [];
        if (targets.Length > MaximumMorphTargets) throw new ArgumentException($"At most {MaximumMorphTargets} morph targets.", nameof(morphTargets));
        if (targets.Any(t => t is null || t.PositionDeltas.Count != mesh.Vertices.Count))
            throw new ArgumentException("Each morph target needs one offset per vertex.", nameof(morphTargets));
        // With weights in [0,1], a vertex stays inside its bind position plus every target's negative or positive offsets.
        var low = mesh.Vertices.ToArray(); var high = mesh.Vertices.ToArray();
        foreach (var target in targets)
            foreach (var v in target.Affected)
            {
                var d = target.PositionDeltas[v];
                low[v] += Vector3.Min(d, Vector3.Zero); high[v] += Vector3.Max(d, Vector3.Zero);
            }
        MorphTargets = Array.AsReadOnly(targets);
        var count = skeleton.Joints.Count;
        Weights = new Vector4[copy.Length];
        JointIndices = new Int4[copy.Length];
        JointBounds = Enumerable.Repeat((new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity)), count).ToArray();
        for (var v = 0; v < copy.Length; v++)
        {
            var (joints, weights) = copy[v];
            if (!float.IsFinite(weights.X) || !float.IsFinite(weights.Y) || !float.IsFinite(weights.Z) || !float.IsFinite(weights.W) ||
                weights.X < 0 || weights.Y < 0 || weights.Z < 0 || weights.W < 0)
                throw new ArgumentOutOfRangeException(nameof(influences), "Weights must be finite and nonnegative.");
            // Scale by the largest weight first so huge finite weights cannot overflow the sum.
            var largest = MathF.Max(MathF.Max(weights.X, weights.Y), MathF.Max(weights.Z, weights.W));
            if (!(largest > 0)) throw new ArgumentOutOfRangeException(nameof(influences), "Each vertex needs a positive weight.");
            weights /= largest;
            weights /= weights.X + weights.Y + weights.Z + weights.W;
            for (var k = 0; k < 4; k++)
            {
                var weight = k switch { 0 => weights.X, 1 => weights.Y, 2 => weights.Z, _ => weights.W };
                var joint = joints[k];
                // Unused slots may hold any index (glTF commonly repeats 0); only weighted slots must be valid.
                if (weight == 0) { joints = Set(joints, k, 0); continue; }
                if ((uint)joint >= (uint)count) throw new ArgumentOutOfRangeException(nameof(influences), "Joint index outside skeleton.");
                ref var bounds = ref JointBounds[joint];
                bounds = (Vector3.Min(bounds.Item1, low[v]), Vector3.Max(bounds.Item2, high[v]));
            }
            copy[v] = new(joints, weights);
            Weights[v] = weights;
            JointIndices[v] = joints;
        }
        Mesh = mesh;
        Skeleton = skeleton;
        Influences = Array.AsReadOnly(copy);
    }

    private static Int4 Set(Int4 value, int index, int joint) => index switch
    {
        0 => value with { X = joint }, 1 => value with { Y = joint }, 2 => value with { Z = joint }, _ => value with { W = joint }
    };

    /// <summary>Conservative bounds of the posed mesh: each joint's bind-space vertex bounds transformed by its skinning matrix.</summary>
    internal (Vector3 Min, Vector3 Max) PosedBounds(SkeletonPose pose)
    {
        var matrices = pose.SkinningMatrices;
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var j = 0; j < JointBounds.Length; j++)
        {
            var (lo, hi) = JointBounds[j];
            if (lo.X > hi.X) continue;
            for (var c = 0; c < 8; c++)
            {
                var p = Vector3.Transform(new Vector3((c & 1) == 0 ? lo.X : hi.X, (c & 2) == 0 ? lo.Y : hi.Y, (c & 4) == 0 ? lo.Z : hi.Z), matrices[j]);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        }
        return (min, max);
    }

    /// <summary>CPU skinning of positions and, when present, normals.</summary>
    internal (Vector3[] Positions, Vector3[]? Normals) Deform(SkeletonPose pose, float[]? morphWeights)
    {
        var matrices = pose.SkinningMatrices;
        var source = Mesh.Vertices;
        var sourceNormals = Mesh.Normals;
        Vector3[]? morphed = null, morphedNormals = null;
        if (morphWeights is not null)
            for (var t = 0; t < MorphTargets.Count; t++)
            {
                var weight = morphWeights[t];
                if (weight == 0) continue;
                var target = MorphTargets[t];
                morphed ??= source.ToArray();
                if (sourceNormals is not null && target.NormalDeltas is not null) morphedNormals ??= sourceNormals.ToArray();
                foreach (var v in target.Affected)
                {
                    morphed[v] += target.PositionDeltas[v] * weight;
                    if (morphedNormals is not null && target.NormalDeltas is not null) morphedNormals[v] += target.NormalDeltas[v] * weight;
                }
            }
        IReadOnlyList<Vector3> basePositions = morphed ?? source;
        var baseNormals = (IReadOnlyList<Vector3>?)morphedNormals ?? sourceNormals;
        var positions = new Vector3[source.Count];
        var normals = baseNormals is null ? null : new Vector3[source.Count];
        for (var v = 0; v < positions.Length; v++)
        {
            var m = Blend(matrices, JointIndices[v], Weights[v]);
            positions[v] = Vector3.Transform(basePositions[v], m);
            if (normals is not null)
            {
                var n = Vector3.TransformNormal(baseNormals![v], m);
                var length = n.Length();
                normals[v] = length > 0 && float.IsFinite(length) ? n / length : sourceNormals![v];
            }
        }
        return (positions, normals);
    }

    private static Matrix4x4 Blend(Matrix4x4[] matrices, Int4 joints, Vector4 weights)
    {
        var m = matrices[joints.X] * weights.X;
        if (weights.Y > 0) m += matrices[joints.Y] * weights.Y;
        if (weights.Z > 0) m += matrices[joints.Z] * weights.Z;
        if (weights.W > 0) m += matrices[joints.W] * weights.W;
        return m;
    }
}

// Lazily skinned geometry of one posed shape. Count never triggers skinning, so budgets stay cheap.
internal sealed class PosedGeometry(SkinnedMesh skinned, SkeletonPose pose, float[]? morphWeights = null) : IReadOnlyList<Triangle3>
{
    private sealed record Result(Vector3[] Positions, Vector3[]? Normals);
    // A reference published atomically: concurrent readers (say, picking during a software render) never see a torn value.
    private Result? _deformed;
    internal SkinnedMesh Skinned { get; } = skinned;
    internal SkeletonPose Pose { get; } = pose;
    /// <summary>One weight per morph target, or null when all are zero.</summary>
    internal float[]? MorphWeights { get; } = morphWeights;
    private Result Deformed
    {
        get
        {
            if (Volatile.Read(ref _deformed) is { } cached) return cached;
            var (positions, normals) = Skinned.Deform(Pose, MorphWeights);
            return Interlocked.CompareExchange(ref _deformed, new(positions, normals), null) ?? _deformed!;
        }
    }
    internal IReadOnlyList<Vector3>? Normals => Skinned.Mesh.Normals is null ? null : Deformed.Normals;
    public int Count => Skinned.Mesh.TriangleCount;
    public Triangle3 this[int index]
    {
        get
        {
            var p = Deformed.Positions;
            var i = Skinned.Mesh.Indices;
            return new(p[i[index * 3]], p[i[index * 3 + 1]], p[i[index * 3 + 2]]);
        }
    }
    public IEnumerator<Triangle3> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

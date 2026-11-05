using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>Poses a humanoid skeleton so its torso and limbs point where a person's landmarks do.</summary>
/// <remarks>
/// Works in view space (X right, Y up, Z toward the camera), with the character facing the camera.
/// Each limb bone receives the smallest rotation that turns it toward the target direction, so bones
/// never twist. Detectors report 2D landmarks; depth comes from foreshortening against the longest
/// length each segment recently had, signed by how people usually move (arms and thighs toward the
/// camera, shins away). The feet stay on the ground and the body follows the person sideways.
/// </remarks>
public sealed class HumanoidRetargeter
{
    // VRM humanoid bone names (VRM 0.x and 1.0 share them).
    private static readonly (string Bone, string Child, BodyJoint From, BodyJoint To, float DepthSign)[] Limbs =
    [
        ("leftUpperArm", "leftLowerArm", BodyJoint.LeftShoulder, BodyJoint.LeftElbow, 1),
        ("leftLowerArm", "leftHand", BodyJoint.LeftElbow, BodyJoint.LeftWrist, 1),
        ("rightUpperArm", "rightLowerArm", BodyJoint.RightShoulder, BodyJoint.RightElbow, 1),
        ("rightLowerArm", "rightHand", BodyJoint.RightElbow, BodyJoint.RightWrist, 1),
        ("leftUpperLeg", "leftLowerLeg", BodyJoint.LeftHip, BodyJoint.LeftKnee, 1),
        ("leftLowerLeg", "leftFoot", BodyJoint.LeftKnee, BodyJoint.LeftAnkle, -1),
        ("rightUpperLeg", "rightLowerLeg", BodyJoint.RightHip, BodyJoint.RightKnee, 1),
        ("rightLowerLeg", "rightFoot", BodyJoint.RightKnee, BodyJoint.RightAnkle, -1),
    ];
    private const float DepthGain = .8f;
    private const float LengthMemory = .995f;

    private readonly Skeleton _skeleton;
    private readonly Quaternion _toView, _fromView;
    private readonly Quaternion _rootRotation;
    private readonly Quaternion[] _restWorld;
    private readonly Vector3[] _restPositions;
    private readonly int _hips;
    private readonly int[] _limbOfJoint;
    private readonly (int Joint, Vector3 LocalDirection, BodyJoint From, BodyJoint To, float DepthSign)[] _limbs;
    private readonly int[] _feet;
    private readonly float _restFloor;
    private readonly float[] _segmentLength;
    private readonly Quaternion[] _local, _world, _held;
    private readonly Vector2[] _points = new Vector2[PoseFrame.JointCount];

    /// <summary>How far, in skeleton units, the character moves sideways when the person reaches the image edge.</summary>
    public float SidewaysRange { get; init; } = .6f;

    /// <summary>Landmarks below this confidence are treated as hidden.</summary>
    public float MinimumConfidence { get; init; } = .3f;

    /// <summary>Prepares retargeting for a skeleton with VRM humanoid bone names.</summary>
    /// <param name="skeleton">The character's skeleton.</param>
    /// <param name="humanoidBones">VRM humanoid bone name → joint index; needs hips, arms, legs and feet.</param>
    /// <param name="toView">Rotation from skeleton space to view space, i.e. the character's orientation in a camera-facing frame.</param>
    public HumanoidRetargeter(Skeleton skeleton, IReadOnlyDictionary<string, int> humanoidBones, Quaternion toView)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        ArgumentNullException.ThrowIfNull(humanoidBones);
        _toView = Quaternion.Normalize(toView);
        _fromView = Quaternion.Conjugate(_toView);
        int Bone(string name) => humanoidBones.TryGetValue(name, out var index) && (uint)index < (uint)skeleton.Joints.Count
            ? index : throw new ArgumentException($"Humanoid bone '{name}' is missing.", nameof(humanoidBones));

        Matrix4x4.Decompose(skeleton.RootTransform, out _, out _rootRotation, out _);
        var restWorld = skeleton.RestPose.WorldTransforms;
        _restWorld = new Quaternion[restWorld.Count];
        _restPositions = new Vector3[restWorld.Count];
        for (var i = 0; i < restWorld.Count; i++)
        {
            Matrix4x4.Decompose(restWorld[i], out _, out _restWorld[i], out _restPositions[i]);
        }
        _hips = Bone("hips");
        _limbs = Limbs.Select(limb =>
        {
            var joint = Bone(limb.Bone);
            var child = Bone(limb.Child);
            // The bone's rest direction expressed in its own frame, so it can be re-evaluated under any parent rotation.
            var restDirection = Vector3.Normalize(_restPositions[child] - _restPositions[joint]);
            return (joint, Vector3.Transform(restDirection, Quaternion.Conjugate(_restWorld[joint])), limb.From, limb.To, limb.DepthSign);
        }).ToArray();
        _limbOfJoint = Enumerable.Repeat(-1, restWorld.Count).ToArray();
        for (var i = 0; i < _limbs.Length; i++) _limbOfJoint[_limbs[i].Joint] = i;
        _feet = new[] { "leftFoot", "rightFoot", "leftToes", "rightToes" }
            .Where(humanoidBones.ContainsKey).Select(Bone).ToArray();
        _restFloor = _feet.Min(f => ViewY(_restPositions[f]));
        _segmentLength = new float[_limbs.Length];
        _local = skeleton.RestPose.Rotations.ToArray();
        _held = skeleton.RestPose.Rotations.ToArray();
        _world = new Quaternion[_local.Length];
    }

    /// <summary>Forgets calibrated segment lengths, for example when a different person appears.</summary>
    public void Reset()
    {
        Array.Clear(_segmentLength);
        _skeleton.RestPose.Rotations.ToArray().CopyTo(_held, 0);
    }

    /// <summary>Computes a pose from landmarks of the upright, unmirrored image.</summary>
    /// <param name="landmarks">One landmark per <see cref="BodyJoint"/>.</param>
    /// <param name="imageAspect">Image width / height.</param>
    /// <param name="mirrored">True when the person sees a mirrored preview; the character then mirrors them.</param>
    public SkeletonPose Retarget(IReadOnlyList<Landmark> landmarks, float imageAspect, bool mirrored)
    {
        ArgumentNullException.ThrowIfNull(landmarks);
        if (landmarks.Count != PoseFrame.JointCount) throw new ArgumentException("Supply one landmark per body joint.", nameof(landmarks));
        // View-space points in image heights. A mirrored preview flips X and swaps sides, so the
        // character moves the limb on the same side of the screen as the person's reflection.
        var points = _points;
        for (var i = 0; i < points.Length; i++)
        {
            var source = landmarks[mirrored ? (int)Opposite((BodyJoint)i) : i];
            var x = (source.X - .5f) * imageAspect;
            points[i] = new(mirrored ? -x : x, .5f - source.Y);
        }
        Vector2 P(BodyJoint joint) => points[(int)joint];

        var hipCenter = (P(BodyJoint.LeftHip) + P(BodyJoint.RightHip)) / 2;
        var shoulderCenter = (P(BodyJoint.LeftShoulder) + P(BodyJoint.RightShoulder)) / 2;
        var up = Direction(shoulderCenter - hipCenter, Vector2.UnitY);
        // The character's left is screen right when it faces the camera.
        var side = P(BodyJoint.LeftHip) - P(BodyJoint.RightHip) + P(BodyJoint.LeftShoulder) - P(BodyJoint.RightShoulder);
        var left = Direction(side - Vector2.Dot(side, up) * up, new Vector2(up.Y, -up.X));
        var torso = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            left.X, left.Y, 0, 0, up.X, up.Y, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
        var torsoSkeleton = _fromView * torso * _toView;

        var joints = _skeleton.Joints;
        for (var j = 0; j < joints.Count; j++)
        {
            var parent = joints[j].Parent < 0 ? _rootRotation : _world[joints[j].Parent];
            if (j == _hips)
            {
                _world[j] = torsoSkeleton * _restWorld[j];
                _local[j] = Quaternion.Normalize(Quaternion.Conjugate(parent) * _world[j]);
                continue;
            }
            _local[j] = _skeleton.RestPose.Rotations[j];
            _world[j] = parent * _local[j];
            var limb = _limbOfJoint[j];
            if (limb < 0) continue;
            var (_, localDirection, from, to, depthSign) = _limbs[limb];
            // A hidden limb holds its last rotation (the rest pose after a reset) rather than following stale landmarks.
            if (landmarks[(int)from].Confidence < MinimumConfidence || landmarks[(int)to].Confidence < MinimumConfidence)
            {
                _local[j] = _held[j];
                _world[j] = parent * _local[j];
                continue;
            }
            var target = Target(limb, P(to) - P(from), depthSign);
            var current = Vector3.Transform(localDirection, _world[j]);
            _world[j] = Quaternion.Normalize(FromTo(current, Vector3.Transform(target, _fromView)) * _world[j]);
            _local[j] = _held[j] = Quaternion.Normalize(Quaternion.Conjugate(parent) * _world[j]);
        }

        // Ground the lowest foot, and follow the person across the image within a bounded stage range.
        var pose = SkeletonPose.Create(_skeleton, _local);
        var floor = _feet.Min(f => ViewY(pose.WorldTransforms[f].Translation));
        var sideways = Math.Clamp(hipCenter.X / (imageAspect / 2), -1, 1) * SidewaysRange;
        var offset = Vector3.Transform(new Vector3(sideways, _restFloor - floor, 0), _fromView);
        // RootOffset is applied before RootTransform; undo its rotation and scale.
        if (Matrix4x4.Invert(_skeleton.RootTransform with { Translation = Vector3.Zero }, out var inverseRoot))
            offset = Vector3.Transform(offset, inverseRoot);
        return SkeletonPose.Create(_skeleton, _local, offset);
    }

    private Vector3 Target(int limb, Vector2 segment, float depthSign)
    {
        var length = segment.Length();
        _segmentLength[limb] = MathF.Max(_segmentLength[limb] * LengthMemory, length);
        var full = _segmentLength[limb];
        var depth = full > length ? MathF.Sqrt(full * full - length * length) * DepthGain * depthSign : 0;
        var target = new Vector3(segment, depth);
        return target.LengthSquared() > 1e-10f ? Vector3.Normalize(target) : -Vector3.UnitY;
    }

    private float ViewY(Vector3 skeletonPoint) => Vector3.Transform(skeletonPoint, _toView).Y;

    private static BodyJoint Opposite(BodyJoint joint) => joint switch
    {
        BodyJoint.Nose => BodyJoint.Nose,
        // Left/right pairs are adjacent, left first.
        _ => (int)joint % 2 == 1 ? joint + 1 : joint - 1
    };

    private static Vector2 Direction(Vector2 value, Vector2 fallback) =>
        value.LengthSquared() > 1e-10f ? Vector2.Normalize(value) : fallback;

    /// <summary>Smallest rotation taking unit vector <paramref name="from"/> onto unit vector <paramref name="to"/>.</summary>
    internal static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from); to = Vector3.Normalize(to);
        var dot = Vector3.Dot(from, to);
        if (dot > .999999f) return Quaternion.Identity;
        if (dot < -.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, from);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, from);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1 + dot));
    }

}

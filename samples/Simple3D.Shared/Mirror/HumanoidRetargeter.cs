using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>Poses a humanoid skeleton so its torso and limbs point where a person's landmarks do.</summary>
/// <remarks>
/// Works in view space (X right, Y up, Z toward the camera), with the character facing the camera.
/// Each limb bone receives the smallest rotation that turns it toward the target direction, so bones
/// never twist. Detectors report 2D landmarks; depth comes from foreshortening against the longest
/// length each segment recently had, signed by how people usually move (arms and thighs in front of
/// the body, shins behind). The body turns about the vertical axis to follow a person who faces away. The feet stay on the ground and the body follows the person sideways.
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
    private const float FacingSmoothing = .35f;
    private const float PelvisShare = .25f;
    private const float BowDeadZone = .3f; // radians of apparent bend (about 17°) treated as noise
    private float _torsoFull;
    private readonly float[] _spineShare, _headShare;
    private float _facing, _frontWidth, _turnSign = 1, _torsoLength;

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
        // Spine bones up to the chest share the bend progressively; neck and head share the head's turn.
        _spineShare = new float[restWorld.Count];
        _headShare = new float[restWorld.Count];
        var spine = new[] { "spine", "chest", "upperChest" }.Where(humanoidBones.ContainsKey).Select(Bone).ToArray();
        for (var i = 0; i < spine.Length; i++) _spineShare[spine[i]] = (i + 1f) / spine.Length;
        if (humanoidBones.TryGetValue("neck", out var neck)) _headShare[neck] = .4f;
        if (humanoidBones.TryGetValue("head", out var headBone)) _headShare[headBone] = 1;
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

    /// <summary>Forgets only the calibrated segment lengths, for example after a camera zoom changed the person's size in the image.</summary>
    public void ForgetLengths() { Array.Clear(_segmentLength); _frontWidth = 0; _torsoLength = 0; _torsoFull = 0; }

    /// <summary>Forgets calibrated segment lengths, for example when a different person appears.</summary>
    public void Reset()
    {
        ForgetLengths();
        _facing = 0; _turnSign = 1;
        _skeleton.RestPose.Rotations.ToArray().CopyTo(_held, 0);
    }

    /// <summary>Computes a pose from landmarks of the upright, unmirrored image.</summary>
    /// <param name="landmarks">One landmark per <see cref="BodyJoint"/>.</param>
    /// <param name="imageAspect">Image width / height.</param>
    /// <param name="mirrored">True when the person sees a mirrored preview; the character then mirrors them.</param>
    /// <param name="face">Optional head angles; without them the head stays aligned with the chest.</param>
    public SkeletonPose Retarget(IReadOnlyList<Landmark> landmarks, float imageAspect, bool mirrored, FaceFrame? face = null)
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

        var shoulderCenter = (P(BodyJoint.LeftShoulder) + P(BodyJoint.RightShoulder)) / 2;
        var shoulders = P(BodyJoint.LeftShoulder) - P(BodyJoint.RightShoulder);
        Vector2 hipCenter, up, side;
        var hipsVisible = landmarks[(int)BodyJoint.LeftHip].Confidence >= MinimumConfidence && landmarks[(int)BodyJoint.RightHip].Confidence >= MinimumConfidence;
        var bow = 0f;
        if (hipsVisible)
        {
            hipCenter = (P(BodyJoint.LeftHip) + P(BodyJoint.RightHip)) / 2;
            up = Direction(shoulderCenter - hipCenter, Vector2.UnitY);
            _torsoLength = Vector2.Distance(shoulderCenter, hipCenter);
            side = P(BodyJoint.LeftHip) - P(BodyJoint.RightHip) + shoulders;
            // Bowing toward or away from the camera shortens the torso in the image; people bow forward far more
            // often than they arch back, so a shorter torso bends forward. Small changes are noise, not a bow.
            _torsoFull = MathF.Max(_torsoFull * LengthMemory, _torsoLength);
            var angle = MathF.Acos(Math.Clamp(_torsoLength / _torsoFull, 0, 1));
            bow = MathF.Max(0, angle - BowDeadZone) * MathF.PI / 2 / (MathF.PI / 2 - BowDeadZone);
        }
        else
        {
            // Waist-up framing: the spine is perpendicular to the shoulder line (upright, whichever way the
            // person faces) and as long as last seen, or about one and a half shoulder widths.
            up = Direction(new Vector2(-shoulders.Y, shoulders.X), Vector2.UnitY);
            if (up.Y < 0) up = -up;
            if (_torsoLength <= 0) _torsoLength = 1.5f * shoulders.Length();
            hipCenter = shoulderCenter - up * _torsoLength;
            side = 2 * shoulders;
        }
        var facing = Facing(landmarks, P(BodyJoint.Nose) - shoulderCenter, _torsoLength, side);
        // The upper body turns about the vertical axis, bows about its own side-to-side axis, then leans within the image
        // plane along the spine. The pelvis takes a share; the spine bones split the rest so the back curves.
        var lean = MathF.Atan2(-up.X, up.Y);
        Quaternion Body(float share) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lean * share) *
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, facing) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, bow * share);
        var pelvis = _fromView * Body(PelvisShare) * _toView;
        var chest = _fromView * Body(1) * _toView;
        var spineBend = chest * Quaternion.Conjugate(pelvis);
        // Limbs usually reach in front of the body: toward the camera when facing it, away when turned around.
        var forward = MathF.Cos(facing);
        // Head angles relative to the chest, mirrored like the body; only meaningful while the face is toward the camera.
        var head = Quaternion.Identity;
        if (face is not null && forward > 0)
        {
            var yaw = (mirrored ? -face.Yaw : face.Yaw) - facing;
            var roll = mirrored ? -face.Roll : face.Roll;
            head = _fromView * Quaternion.CreateFromAxisAngle(Vector3.UnitY, Math.Clamp(yaw, -1.2f, 1.2f)) *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, Math.Clamp(-face.Pitch, -.8f, .8f)) *
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Math.Clamp(roll, -.8f, .8f)) * _toView;
        }

        var joints = _skeleton.Joints;
        for (var j = 0; j < joints.Count; j++)
        {
            var parent = joints[j].Parent < 0 ? _rootRotation : _world[joints[j].Parent];
            if (j == _hips || _spineShare[j] > 0 || _headShare[j] > 0)
            {
                // Rotations applied on top of the rest pose, in skeleton space: the pelvis's own, the spine's growing
                // share of the bend above it, and the head's share of its turn above the chest.
                var bend = j == _hips ? Quaternion.Identity : Quaternion.Slerp(Quaternion.Identity, spineBend, _spineShare[j]);
                var turn = Quaternion.Slerp(Quaternion.Identity, head, _headShare[j]);
                var delta = _headShare[j] > 0 ? Quaternion.Normalize(chest) : Quaternion.Normalize(bend * pelvis);
                _world[j] = Quaternion.Normalize(turn * delta * _restWorld[j]);
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
            var target = Target(limb, P(to) - P(from), depthSign * forward);
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

    // The body's turn about the vertical axis, 0 when facing the camera and ±π when showing its back.
    // Detectors label joints anatomically, so a person seen from behind has their left side on the image
    // left: the signed left-to-right width over its front-on maximum gives the cosine. The nose, visible
    // only from the front or side, tells which way the person turned; seen from behind, the last turn holds.
    private float Facing(IReadOnlyList<Landmark> landmarks, Vector2 noseOffset, float torsoLength, Vector2 side)
    {
        if (torsoLength < 1e-4f) return _facing;
        var ratio = side.X / torsoLength;
        _frontWidth = MathF.Max(MathF.Max(_frontWidth * LengthMemory, MathF.Abs(ratio)), .2f);
        var cos = Math.Clamp(ratio / _frontWidth, -1, 1);
        // Only while the face is toward the camera: ML Kit estimates a nose position even from behind.
        if (cos > .2f && landmarks[(int)BodyJoint.Nose].Confidence >= MinimumConfidence && MathF.Abs(noseOffset.X) > .02f * torsoLength)
            _turnSign = MathF.Sign(noseOffset.X);
        var target = MathF.Atan2(_turnSign * MathF.Sqrt(1 - cos * cos), cos);
        // Smooth along the shorter way around the circle so noisy side views do not flip the character.
        var delta = MathF.IEEERemainder(target - _facing, 2 * MathF.PI);
        _facing = MathF.IEEERemainder(_facing + delta * FacingSmoothing, 2 * MathF.PI);
        return _facing;
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

using System.IO.Compression;
using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared.Mirror;

internal static class MirrorTests
{
    /// <summary>Apple Vision landmarks of a 33 s clip of one person exercising, with an empty 3 s gap at 14–17 s.</summary>
    internal static IReadOnlyList<PoseFrame> Fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mirror-landmarks.jsonl.gz");
        using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        return PoseRecording.Read(reader);
    }

    // A minimal VRM-named humanoid facing +Z (its left is +X), 1.6 units tall, in an A-pose.
    internal static (Skeleton Skeleton, Dictionary<string, int> Bones) Humanoid()
    {
        var joints = new List<Joint>();
        var bones = new Dictionary<string, int>();
        int Add(string name, int parent, Vector3 offset)
        {
            joints.Add(new(name, parent, offset, Quaternion.Identity, Vector3.One, Matrix4x4.Identity));
            bones[name] = joints.Count - 1;
            return joints.Count - 1;
        }
        var hips = Add("hips", -1, new(0, .9f, 0));
        var chest = Add("chest", hips, new(0, .35f, 0));
        Add("head", chest, new(0, .3f, 0));
        foreach (var (side, x) in new[] { ("left", 1f), ("right", -1f) })
        {
            var upperArm = Add(side + "UpperArm", chest, new(.18f * x, .15f, 0));
            var lowerArm = Add(side + "LowerArm", upperArm, new(.2f * x, -.18f, 0));
            Add(side + "Hand", lowerArm, new(.18f * x, -.16f, 0));
            var upperLeg = Add(side + "UpperLeg", hips, new(.1f * x, -.05f, 0));
            var lowerLeg = Add(side + "LowerLeg", upperLeg, new(0, -.42f, 0));
            Add(side + "Foot", lowerLeg, new(0, -.4f, 0));
        }
        // Inverse bind matrices are irrelevant for retargeting, which only reads joint transforms.
        return (new Skeleton(joints), bones);
    }

    internal static void Run()
    {
        var frames = Fixture();

        // Tracking states follow the clip: acquire quickly, lose during the empty gap, reacquire after it.
        var tracker = new PoseTracker();
        var transitions = new List<(double Time, TrackingState State)>();
        var time = 0.0;
        tracker.StateChanged += state => transitions.Add((time, state));
        foreach (var frame in frames) { time = frame.Timestamp.TotalSeconds; tracker.Update(frame); }
        var summary = string.Join(", ", transitions.Select(t => $"{t.State}@{t.Time:0.00}"));
        Check(transitions.Count == 3, $"expected Tracking, Lost, Tracking; got {summary}");
        Check(transitions[0] is { State: TrackingState.Tracking, Time: < .6 }, $"acquire: {summary}");
        Check(transitions[1] is { State: TrackingState.Lost, Time: >= 14.5 and < 15 }, $"loss in gap: {summary}");
        Check(transitions[2] is { State: TrackingState.Tracking, Time: >= 17 and < 17.5 }, $"reacquire: {summary}");

        // Retargeting reproduces every limb's on-screen direction and keeps the feet on the ground.
        var (skeleton, bones) = Humanoid();
        var retargeter = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity);
        tracker = new PoseTracker();
        var errors = new List<float>();
        var footHeights = new List<float>();
        var hipXs = new List<float>();
        foreach (var frame in frames)
        {
            if (!tracker.Update(frame)) continue;
            var pose = retargeter.Retarget(tracker.Landmarks, tracker.ImageAspect, mirrored: false);
            errors.AddRange(retargeter.LastErrors);
            var world = pose.WorldTransforms;
            footHeights.Add(MathF.Min(world[bones["leftFoot"]].Translation.Y, world[bones["rightFoot"]].Translation.Y));
            hipXs.Add(world[bones["hips"]].Translation.X);
        }
        errors.Sort();
        Check(errors.Count > 700 * 8 && errors[errors.Count / 2] < .5f && errors[(int)(errors.Count * .99)] < 2,
            $"limb direction error median {errors[errors.Count / 2]:0.00}°, p99 {errors[(int)(errors.Count * .99)]:0.00}°");
        var restFloor = MathF.Min(skeleton.RestPose.WorldTransforms[bones["leftFoot"]].Translation.Y, skeleton.RestPose.WorldTransforms[bones["rightFoot"]].Translation.Y);
        Check(footHeights.All(y => MathF.Abs(y - restFloor) < 1e-3f), $"feet must stay grounded, worst {footHeights.Max(y => MathF.Abs(y - restFloor)):0.0000}");
        Check(hipXs.All(x => MathF.Abs(x) < 2), "sideways motion stays bounded");

        // Mirroring: the person's anatomical left wrist (image right) raised above the head.
        var raised = Enumerable.Range(0, PoseFrame.JointCount).Select(i => new Landmark(0, 0, 1)).ToArray();
        void Set(BodyJoint joint, float x, float y) => raised[(int)joint] = new(x, y, 1);
        Set(BodyJoint.Nose, .5f, .15f);
        Set(BodyJoint.LeftShoulder, .58f, .25f); Set(BodyJoint.RightShoulder, .42f, .25f);
        Set(BodyJoint.LeftElbow, .62f, .12f); Set(BodyJoint.LeftWrist, .63f, .02f);
        Set(BodyJoint.RightElbow, .38f, .38f); Set(BodyJoint.RightWrist, .37f, .5f);
        Set(BodyJoint.LeftHip, .55f, .55f); Set(BodyJoint.RightHip, .45f, .55f);
        Set(BodyJoint.LeftKnee, .55f, .75f); Set(BodyJoint.RightKnee, .45f, .75f);
        Set(BodyJoint.LeftAnkle, .55f, .95f); Set(BodyJoint.RightAnkle, .45f, .95f);
        float HandHeight(SkeletonPose pose, string side) => pose.WorldTransforms[bones[side + "Hand"]].Translation.Y;
        var direct = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity).Retarget(raised, 1, mirrored: false);
        Check(HandHeight(direct, "left") > HandHeight(direct, "right") + .3f && direct.WorldTransforms[bones["leftHand"]].Translation.X > 0,
            "unmirrored: the character raises its own left hand, on screen right");
        var mirrored = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity).Retarget(raised, 1, mirrored: true);
        Check(HandHeight(mirrored, "right") > HandHeight(mirrored, "left") + .3f && mirrored.WorldTransforms[bones["rightHand"]].Translation.X < 0,
            "mirrored: the raised hand appears on screen left, like a reflection");

        // A character modeled facing -Z (here through a rotated root) is turned to the camera by toView.
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var awaySkeleton = new Skeleton(skeleton.Joints, Matrix4x4.CreateFromQuaternion(turned));
        var away = new HumanoidRetargeter(awaySkeleton, bones, turned).Retarget(raised, 1, mirrored: false);
        var hand = Vector3.Transform(away.WorldTransforms[bones["leftHand"]].Translation, turned);
        Check(hand.X > 0 && HandHeight(away, "left") > HandHeight(away, "right") + .3f, "facing rotation");
        Console.WriteLine($"PASS mirror tracking ({summary}) and retargeting (median {errors[errors.Count / 2]:0.00}°)");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

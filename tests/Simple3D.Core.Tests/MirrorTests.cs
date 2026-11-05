using System.IO.Compression;
using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared.Mirror;

internal static class MirrorTests
{
    /// <summary>Apple Vision landmarks of a 33 s clip of one person exercising, with an empty 3 s gap at 14–17 s.</summary>
    internal static IReadOnlyList<PoseFrame> Fixture(string name = "mirror-landmarks")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".jsonl.gz");
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

    // Apple Vision face measurements of Mixkit clips 46379 (neutral, wide-open mouth, eyes closed, talking) and 43831
    // (smiling, laughing, surprised), every second frame, back to back.
    private static void Faces()
    {
        var faces = Fixture("face-landmarks").Select(frame => frame.Face).OfType<FaceFrame>().ToArray();
        Check(faces.Length >= 200, $"face fixture needs detected faces, got {faces.Length}");
        foreach (var face in faces)
        {
            Check(new[] { face.Yaw, face.Pitch, face.Roll }.All(a => float.IsFinite(a) && MathF.Abs(a) < MathF.PI), "face angles must be finite radians");
            Check(new[] { face.MouthOpen, face.Smile, face.BlinkLeft, face.BlinkRight }.All(v => v is >= 0 and <= 1), "face expressions must be in [0,1]");
            Check(face.Brows is >= -1 and <= 1, "brows must be in [-1,1]");
        }
        // Each expression spans most of its range across the clips, and neutral moments read close to 0.
        foreach (var (name, values) in new (string, float[])[] { ("mouth", [.. faces.Select(f => f.MouthOpen)]), ("smile", [.. faces.Select(f => f.Smile)]),
            ("brows", [.. faces.Select(f => f.Brows)]), ("blink left", [.. faces.Select(f => f.BlinkLeft)]), ("blink right", [.. faces.Select(f => f.BlinkRight)]) })
        {
            Array.Sort(values);
            float low = values[values.Length / 20], high = values[values.Length * 19 / 20];
            Check(high - low > .6f && MathF.Abs(low) < .7f, $"{name} must vary from about neutral: 5th percentile {low:0.00}, 95th {high:0.00}");
        }
        Check(faces.Count(f => f.BlinkLeft > .7f && f.BlinkRight > .7f) >= 3, "closed eyes must read as blinks on both sides");

        // The geometry ignores face size, position and roll, and maps sides to the person's own eyes.
        static Vector2[] Eye(float x, float opening) => [new(x - .15f, 0), new(x - .05f, opening * .15f), new(x + .05f, opening * .15f),
            new(x + .15f, 0), new(x + .05f, -opening * .15f), new(x - .05f, -opening * .15f)];
        static FaceLandmarks Face(float opening = .35f, float rightEyeOpening = .35f, float gap = 0, float mouthWidth = .7f, float brow = .26f) => new(
            Eye(-.5f, opening), Eye(.5f, rightEyeOpening), [new(-.6f, brow), new(-.4f, brow)], [new(.4f, brow), new(.6f, brow)],
            new(-mouthWidth / 2, -1.1f), new(mouthWidth / 2, -1.1f), [new(-.1f, -1.1f), new(.1f, -1.1f)], [new(-.1f, -1.1f - gap), new(.1f, -1.1f - gap)]);
        static FaceLandmarks Move(FaceLandmarks f, float angle, float scale, Vector2 offset)
        {
            var m = Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offset);
            Vector2[] T(Vector2[] p) => [.. p.Select(v => Vector2.Transform(v, m))];
            return new(T(f.LeftEye), T(f.RightEye), T(f.LeftBrow), T(f.RightBrow), Vector2.Transform(f.LeftMouthCorner, m),
                Vector2.Transform(f.RightMouthCorner, m), T(f.UpperInnerLip), T(f.LowerInnerLip));
        }
        var neutral = FaceGeometry.Measure(Face(), 0, 0, 0);
        Check(neutral is { MouthOpen: 0, Smile: 0, BlinkLeft: 0, BlinkRight: 0 } && MathF.Abs(neutral.Brows) < .01f, $"neutral face: {neutral}");
        var moved = FaceGeometry.Measure(Move(Face(gap: .3f, mouthWidth: .9f, brow: .31f), .4f, 250, new(640, 360)), 0, 0, 0);
        var still = FaceGeometry.Measure(Face(gap: .3f, mouthWidth: .9f, brow: .31f), 0, 0, 0);
        Check(MathF.Abs(moved.MouthOpen - still.MouthOpen) + MathF.Abs(moved.Smile - still.Smile) + MathF.Abs(moved.Brows - still.Brows) < .01f
            && still is { MouthOpen: > .6f, Smile: > .5f, Brows: > .4f }, $"rolled, scaled face: {moved} vs {still}");
        var wink = FaceGeometry.Measure(Face(rightEyeOpening: .05f), 0, 0, 0);
        Check(wink is { BlinkLeft: 1, BlinkRight: 0 }, $"the eye on the image's right is the person's left: {wink}");
        Console.WriteLine($"PASS mirror faces: {faces.Length} recorded faces span every expression; geometry ignores size and roll");
    }

    internal static void Run()
    {
        Faces();
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
            var world = pose.WorldTransforms;
            // Independently of the retargeter's internals: each visible limb of the posed skeleton points where the person's does.
            foreach (var (bone, child, from, to) in new[] { ("leftUpperArm", "leftLowerArm", BodyJoint.LeftShoulder, BodyJoint.LeftElbow),
                ("rightLowerArm", "rightHand", BodyJoint.RightElbow, BodyJoint.RightWrist), ("leftUpperLeg", "leftLowerLeg", BodyJoint.LeftHip, BodyJoint.LeftKnee),
                ("rightLowerLeg", "rightFoot", BodyJoint.RightKnee, BodyJoint.RightAnkle) })
            {
                var (a, b) = (tracker.Landmarks[(int)from], tracker.Landmarks[(int)to]);
                if (a.Confidence < .3f || b.Confidence < .3f) continue;
                var limb = world[bones[child]].Translation - world[bones[bone]].Translation;
                var person = new Vector2((b.X - a.X) * tracker.ImageAspect, a.Y - b.Y);
                errors.Add(MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(new(limb.X, limb.Y)), Vector2.Normalize(person)), -1, 1)) * 180 / MathF.PI);
            }
            footHeights.Add(MathF.Min(world[bones["leftFoot"]].Translation.Y, world[bones["rightFoot"]].Translation.Y));
            hipXs.Add(world[bones["hips"]].Translation.X);
        }
        errors.Sort();
        Check(errors.Count > 700 * 3 && errors[errors.Count / 2] < .5f && errors[(int)(errors.Count * .99)] < 2,
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

        // Bowing toward the camera: once the full torso length has been seen, a shorter torso bends the chest
        // forward (toward the camera, +Z) progressively along the spine, the pelvis only a little.
        var bowing = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity);
        bowing.Retarget(raised, 1, mirrored: false);
        var bent = raised.ToArray();
        foreach (var joint in new[] { BodyJoint.Nose, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftElbow, BodyJoint.RightElbow, BodyJoint.LeftWrist, BodyJoint.RightWrist })
            bent[(int)joint] = bent[(int)joint] with { Y = .55f - (.55f - bent[(int)joint].Y) * .6f };
        var bowPose = bowing.Retarget(bent, 1, mirrored: false).WorldTransforms;
        Vector3 Up(string bone) => Vector3.TransformNormal(Vector3.UnitY, bowPose[bones[bone]]);
        Check(Up("chest").Z > .5f && Up("hips").Z is > 0 and < .4f, $"bow: chest up {Up("chest")}, pelvis up {Up("hips")}");

        // Arms across the body: a forearm drawn over the chest stays outside the torso, in front when the hand is seen,
        // behind when the detector barely sees it over the torso (hidden behind the body).
        var across = raised.ToArray();
        across[(int)BodyJoint.LeftElbow] = new(.6f, .38f, 1);
        across[(int)BodyJoint.LeftWrist] = new(.47f, .36f, 1);
        float WristZ(Landmark[] lm)
        {
            var solid = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity) { TorsoRadius = .12f };
            // Switching to behind needs a few consistent frames, so a single misdetection cannot flip the arm.
            for (var i = 0; i < 3; i++) solid.Retarget(lm, 1, mirrored: false);
            var world = solid.Retarget(lm, 1, mirrored: false).WorldTransforms;
            var (hand, hips, chest) = (world[bones["leftHand"]].Translation, world[bones["hips"]].Translation, world[bones["chest"]].Translation);
            var t = Math.Clamp(Vector3.Dot(hand - hips, chest - hips) / (chest - hips).LengthSquared(), 0, 1);
            Check(Vector3.Distance(hand, hips + (chest - hips) * t) >= .119f, $"hand inside the torso: {hand}");
            return hand.Z;
        }
        Check(WristZ(across) > 0, "a visible hand across the chest is in front of the body");
        across[(int)BodyJoint.LeftWrist] = across[(int)BodyJoint.LeftWrist] with { Confidence = .35f };
        Check(WristZ(across) < 0, "a barely seen hand over the torso is behind the body");

        // Smoothness at a 120 Hz display with 25 Hz detector frames: easing must remove the steps without lagging behind.
        var (eased, raw, lag) = Smoothness(frames, skeleton, bones);
        Check(eased.P99 < 10 && eased.Max < 25 && eased.P99 < raw.P99 * .5f && lag < 8,
            $"display-rate easing: eased step p99 {eased.P99:0.0}° max {eased.Max:0.0}°, raw p99 {raw.P99:0.0}° max {raw.Max:0.0}°, median lag {lag:0.0}°");
        Console.WriteLine($"PASS mirror smoothness at 120 Hz: largest limb step per frame p99 {eased.P99:0.0}° (raw {raw.P99:0.0}°), max {eased.Max:0.0}° (raw {raw.Max:0.0}°), median lag {lag:0.0}°");

        // A character modeled facing -Z (here through a rotated root) is turned to the camera by toView.
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var awaySkeleton = new Skeleton(skeleton.Joints, Matrix4x4.CreateFromQuaternion(turned));
        var away = new HumanoidRetargeter(awaySkeleton, bones, turned).Retarget(raised, 1, mirrored: false);
        var hand = Vector3.Transform(away.WorldTransforms[bones["leftHand"]].Translation, turned);
        Check(hand.X > 0 && HandHeight(away, "left") > HandHeight(away, "right") + .3f, "facing rotation");
        // Waist-up framing: the same clip with hips and legs hidden still tracks, the arms follow and the legs stand at rest.
        BodyJoint[] lower = [BodyJoint.LeftHip, BodyJoint.RightHip, BodyJoint.LeftKnee, BodyJoint.RightKnee, BodyJoint.LeftAnkle, BodyJoint.RightAnkle];
        var upperTracker = new PoseTracker();
        var upper = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity);
        var upperErrors = new List<float>();
        var restLeg = skeleton.RestPose.WorldTransforms[bones["leftFoot"]].Translation - skeleton.RestPose.WorldTransforms[bones["leftUpperLeg"]].Translation;
        foreach (var frame in frames)
        {
            var cropped = frame.Landmarks is { } all ? frame with { Landmarks = all.Select((p, i) => lower.Contains((BodyJoint)i) ? p with { Confidence = 0 } : p).ToArray() } : frame;
            if (!upperTracker.Update(cropped)) continue;
            var world = upper.Retarget(upperTracker.Landmarks, upperTracker.ImageAspect, mirrored: false).WorldTransforms;
            var (a, b) = (upperTracker.Landmarks[(int)BodyJoint.LeftShoulder], upperTracker.Landmarks[(int)BodyJoint.LeftElbow]);
            if (b.Confidence < .3f) continue;
            var arm = world[bones["leftLowerArm"]].Translation - world[bones["leftUpperArm"]].Translation;
            var person = new Vector2((b.X - a.X) * upperTracker.ImageAspect, a.Y - b.Y);
            upperErrors.Add(MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(new(arm.X, arm.Y)), Vector2.Normalize(person)), -1, 1)) * 180 / MathF.PI);
            var leg = world[bones["leftFoot"]].Translation - world[bones["leftUpperLeg"]].Translation;
            Check(float.IsFinite(leg.X) && Vector3.Distance(Vector3.Normalize(leg), Vector3.Normalize(Vector3.Transform(restLeg, Quaternion.CreateFromRotationMatrix(world[bones["hips"]])))) < .05f,
                "hidden legs keep their rest pose relative to the hips");
        }
        upperErrors.Sort();
        Check(upperTracker.State == TrackingState.Tracking && upperErrors.Count > 600 && upperErrors[upperErrors.Count / 2] < 1,
            $"waist-up tracking: {upperErrors.Count} arm samples, median {(upperErrors.Count > 0 ? upperErrors[upperErrors.Count / 2] : -1):0.0}°");

        // Turning around: a dancer (Apple Vision landmarks, Mixkit clip 43650) spins between facing the camera and showing her back.
        var spinner = new HumanoidRetargeter(skeleton, bones, Quaternion.Identity);
        var spin = new PoseTracker();
        int backFrames = 0, backRight = 0, frontFrames = 0, frontRight = 0;
        foreach (var frame in Fixture("turning-landmarks"))
        {
            if (!spin.Update(frame)) continue;
            var forward = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromRotationMatrix(spinner.Retarget(spin.Landmarks, spin.ImageAspect, mirrored: false).WorldTransforms[bones["hips"]]));
            var lm = frame.Landmarks!;
            var width = lm[(int)BodyJoint.LeftShoulder].X - lm[(int)BodyJoint.RightShoulder].X + lm[(int)BodyJoint.LeftHip].X - lm[(int)BodyJoint.RightHip].X;
            // Clear cases only: her left side well to the image left with the face hidden, or a wide frontal view.
            if (width < -.1f && lm[(int)BodyJoint.Nose].Confidence < .3f) { backFrames++; if (forward.Z < -.3f) backRight++; }
            else if (width > .15f && lm[(int)BodyJoint.Nose].Confidence > .8f) { frontFrames++; if (forward.Z > .5f) frontRight++; }
        }
        Check(backFrames > 20 && backRight >= backFrames * .8 && frontFrames > 20 && frontRight >= frontFrames * .8,
            $"facing: back {backRight}/{backFrames}, front {frontRight}/{frontFrames}");
        Console.WriteLine($"PASS mirror waist-up framing: arms follow ({upperErrors.Count} samples), legs stand");
        Console.WriteLine($"PASS mirror turning: character faces away in {backRight}/{backFrames} back views and toward the camera in {frontRight}/{frontFrames} front views");
        Console.WriteLine($"PASS mirror tracking ({summary}) and retargeting (median {errors[errors.Count / 2]:0.00}°)");
    }

    // Replays the clip at its 25 Hz through a full MirrorSession, presenting at 120 Hz, and measures the largest per-frame
    // rotation change of any arm or leg joint, with easing and without (the target pose itself), plus the eased lag.
    private static ((float P99, float Max) Eased, (float P99, float Max) Raw, float Lag) Smoothness(IReadOnlyList<PoseFrame> frames, Skeleton skeleton, Dictionary<string, int> bones)
    {
        var mesh = new Mesh([new(0, 0, 0), new(.2f, 0, 0), new(0, 1.6f, 0)], [0, 1, 2]);
        var shape = Shape.FromSkinnedMesh(new SkinnedMesh(mesh, skeleton, Enumerable.Repeat(new VertexInfluence(new(bones["hips"], 0, 0, 0), Vector4.UnitX), 3)));
        var session = new MirrorSession(new MirrorCharacter(shape, skeleton, bones, FacesPositiveZ: true));
        var limbs = new[] { "leftUpperArm", "leftLowerArm", "rightUpperArm", "rightLowerArm", "leftUpperLeg", "leftLowerLeg", "rightUpperLeg", "rightLowerLeg" }.Select(b => bones[b]).ToArray();
        Quaternion[]? previousShown = null, previousTarget = null;
        List<float> eased = [], raw = [], lag = [];
        var next = 0;
        for (var tick = 0; tick < frames[^1].Timestamp.TotalSeconds * 120; tick++)
        {
            var now = TimeSpan.FromSeconds(tick / 120d);
            var applied = false;
            while (next < frames.Count && frames[next].Timestamp <= now) applied |= session.Apply(frames[next++], mirrored: false);
            session.Present(TimeSpan.FromSeconds(1 / 120d));
            // Losing and finding the person legitimately snaps the pose; measure only continuous tracking.
            if (session.Tracker.State != TrackingState.Tracking) { previousShown = previousTarget = null; continue; }
            var shown = Leaf(session).Pose!.Rotations.ToArray();
            var target = session.TargetPose!.Rotations.ToArray();
            if (previousShown is not null) eased.Add(limbs.Max(j => Degrees(previousShown[j], shown[j])));
            if (previousTarget is not null) raw.Add(limbs.Max(j => Degrees(previousTarget[j], target[j])));
            lag.Add(limbs.Average(j => Degrees(shown[j], target[j])));
            (previousShown, previousTarget) = (shown, target);
        }
        (float, float) Stats(List<float> v) { v.Sort(); return (v[(int)(v.Count * .99)], v[^1]); }
        lag.Sort();
        return (Stats(eased), Stats(raw), lag[lag.Count / 2]);
        static float Degrees(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0, 1)) * 180 / MathF.PI;
        static Shape Leaf(MirrorSession s) => Find(s.Stage.CharacterShape)!;
        static Shape? Find(Shape s) => s.Pose is not null ? s : s.Children.Select(Find).FirstOrDefault(c => c is not null);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

/// <summary>End to end on real faces: Apple Vision fixtures of three Mixkit clips drive every bundled character, with its own
/// face table, through a full MirrorSession. The character's mesh must visibly follow the person: the mouth opens by a share of
/// the head height, each eye closes, a wink closes one eye, and the head turns, nods and tilts.</summary>
internal static class MirrorFaceTests
{
    /// <summary>How far, as a share of head height, the mouth vertices must move while the person's mouth is wide open.
    /// Juanita's own "a" moves them 4.2% and only parts her lips; 6% is the least that reads as an open mouth on every model
    /// at Mirror's face close-up (Juanita's "a"+"o", 8.4%, is a clear round mouth). The tables reach 8.7–22% on the fixture.</summary>
    internal const float MouthOpenShare = .06f;
    /// <summary>The share of an eye's height a blink must close.</summary>
    internal const float BlinkClosure = .6f;

    internal static void Run()
    {
        foreach (var bundled in MirrorModels.All) Run(bundled);
    }

    private static void Run(MirrorModel bundled)
    {
        using var asset = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", bundled.File));
        var model = GltfReader.Read(asset);
        var character = MirrorCharacter.FromVrm(model, model.CreateShape(), bundled.Face);
        var face = character.Face;
        var ((headMin, headMax), _) = character.HeadBounds()!.Value;
        var headHeight = headMax.Y - headMin.Y;
        var name = bundled.Name;
        // The vertices each part of the face moves, from the expressions the table drives.
        var mouth = Moved(model, face.Mouth.Keys, headHeight);
        var (leftEye, rightEye) = face.SeparateEyes
            ? (Moved(model, ["blink_l"], headHeight), Moved(model, ["blink_r"], headHeight))
            : Split(Moved(model, ["blink"], headHeight));
        Check(mouth.Count > 0 && leftEye.Count > 0 && rightEye.Count > 0, $"{name}: no mouth or eye shapes");

        // 46379: neutral 0–3.3 s, mouth wide open 3.6–7.9 s, blinks while talking 8.4–12 s (head and shoulders, body visible).
        var talk = Replay(character, "face-46379");
        float Mean(IEnumerable<Sample> s, Func<Sample, float> v) => s.Any() ? s.Average(v) : float.NaN;
        var neutral = talk.Where(s => s.Time is > 1 and < 3.3).ToList();
        var open = talk.Where(s => s.Time is > 4 and < 7.6).ToList();
        float Mouth(Sample s) => Displacement(s, mouth) / headHeight;
        Check(Mean(open, Mouth) >= MouthOpenShare && Mean(neutral, Mouth) < MouthOpenShare / 3,
            $"{name}: mouth moves {Mean(open, Mouth):P1} of head height open (need {MouthOpenShare:P0}), {Mean(neutral, Mouth):P1} neutral");
        // Her blinks must close each of the character's eyes, and its eyes must follow hers.
        float leftTravel = Travel(model, face.SeparateEyes ? "blink_l" : "blink", leftEye), rightTravel = Travel(model, face.SeparateEyes ? "blink_r" : "blink", rightEye);
        float Left(Sample s) => Closure(s, leftEye, leftTravel);
        float Right(Sample s) => Closure(s, rightEye, rightTravel);
        float Eyes(Sample s) => (Left(s) + Right(s)) / 2;
        Check(talk.Max(Left) >= BlinkClosure && talk.Max(Right) >= BlinkClosure && Mean(neutral, Eyes) < .15f,
            $"{name}: blinks close the eyes {talk.Max(Left):P0} and {talk.Max(Right):P0} (need {BlinkClosure:P0}), neutral {Mean(neutral, Eyes):P0}");
        Check(Correlation(talk, s => s.FaceBlink, Eyes) > .7f, $"{name}: eyes follow blinks: r={Correlation(talk, s => s.FaceBlink, Eyes):0.00}");

        // 11519: face only (no body detected); a wink with her right eye at 4.1–4.5 s; head tilts.
        var girl = Replay(character, "face-11519");
        Check(girl.Count > 200, $"{name}: face-only framing must track: {girl.Count} frames");
        var wink = girl.Where(s => s.Time is > 4.15 and < 4.45).ToList();
        if (face.SeparateEyes)
            Check(Mean(wink, Right) > Mean(wink, Left) + .3f, $"{name}: wink closes the right eye {Mean(wink, Right):P0}, left {Mean(wink, Left):P0}");
        Check(Correlation(girl, s => s.FaceRoll, s => s.HeadRoll) > .8f, $"{name}: head tilt follows: r={Correlation(girl, s => s.FaceRoll, s => s.HeadRoll):0.00}");
        // Bangs over her brows must not read as a frown.
        float Frown(Sample s) => face.Frown.Keys.Where(model.Expressions.ContainsKey).Select(e => model.Expressions[e].Binds)
            .SelectMany(b => b).Select(b => s.Weights.TryGetValue(b.Mesh, out var w) ? w[b.Target] : 0).DefaultIfEmpty().Max();
        Check(Mean(girl, Frown) < .2f, $"{name}: bangs over the brows must not read as a frown: {Mean(girl, Frown):0.00}");

        // 43829: laughing, head turned and nodding.
        var laugh = Replay(character, "face-43829");
        Check(Correlation(laugh, s => s.FaceYaw, s => s.HeadYaw) > .7f && Correlation(laugh, s => s.FacePitch, s => s.HeadPitch) > .7f,
            $"{name}: head turn and nod follow: yaw r={Correlation(laugh, s => s.FaceYaw, s => s.HeadYaw):0.00}, pitch r={Correlation(laugh, s => s.FacePitch, s => s.HeadPitch):0.00}");
        Console.WriteLine($"PASS mirror face end to end ({name}): mouth {Mean(open, Mouth):P1} of head height open vs {Mean(neutral, Mouth):P1}, " +
            $"blinks close {talk.Max(Left):P0}/{talk.Max(Right):P0}, r={Correlation(talk, s => s.FaceBlink, Eyes):0.00}{(face.SeparateEyes ? $", wink {Mean(wink, Right):P0} vs {Mean(wink, Left):P0}" : "")}, " +
            $"tilt r={Correlation(girl, s => s.FaceRoll, s => s.HeadRoll):0.00}, turn r={Correlation(laugh, s => s.FaceYaw, s => s.HeadYaw):0.00}, nod r={Correlation(laugh, s => s.FacePitch, s => s.HeadPitch):0.00}");
    }

    // Vertices (per mesh) that the named expressions move by more than 0.5% of the head height at full intensity.
    private static Dictionary<SkinnedMesh, int[]> Moved(GltfModel model, IEnumerable<string> expressions, float headHeight)
    {
        var moved = new Dictionary<SkinnedMesh, HashSet<int>>();
        foreach (var name in expressions)
        {
            if (!model.Expressions.TryGetValue(name, out var expression)) continue;
            foreach (var (mesh, target, weight) in expression.Binds)
                for (var v = 0; v < mesh.Mesh.Vertices.Count; v++)
                    if ((mesh.MorphTargets[target].PositionDeltas[v] * weight).Length() > headHeight * .005f)
                        (moved.TryGetValue(mesh, out var set) ? set : moved[mesh] = []).Add(v);
        }
        return moved.ToDictionary(m => m.Key, m => m.Value.ToArray());
    }

    // A both-eyes blink split into the character's left (+X) and right eye.
    private static (Dictionary<SkinnedMesh, int[]>, Dictionary<SkinnedMesh, int[]>) Split(Dictionary<SkinnedMesh, int[]> eyes) =>
        (eyes.ToDictionary(e => e.Key, e => e.Value.Where(v => e.Key.Mesh.Vertices[v].X > 0).ToArray()),
         eyes.ToDictionary(e => e.Key, e => e.Value.Where(v => e.Key.Mesh.Vertices[v].X <= 0).ToArray()));

    private static Vector3 Offset(Sample s, SkinnedMesh mesh, int v)
    {
        var offset = Vector3.Zero;
        if (s.Weights.TryGetValue(mesh, out var weights))
            for (var t = 0; t < weights.Length; t++)
                if (weights[t] != 0) offset += mesh.MorphTargets[t].PositionDeltas[v] * weights[t];
        return offset;
    }

    // The largest distance any of the vertices moved.
    private static float Displacement(Sample s, Dictionary<SkinnedMesh, int[]> vertices) =>
        vertices.SelectMany(m => m.Value.Select(v => Offset(s, m.Key, v).Length())).DefaultIfEmpty().Max();

    // How far an eye closed: the eyelid's travel over its travel when the model's blink is fully on, which closes the eye
    // (see the grids in docs/site/images/mirror-faces-*.png). A model whose blink only half-closes fails visually, not here.
    private static float Closure(Sample s, Dictionary<SkinnedMesh, int[]> eye, float full) =>
        eye.SelectMany(m => m.Value.Select(v => MathF.Abs(Offset(s, m.Key, v).Y))).DefaultIfEmpty().Max() / full;

    // The eyelid's vertical travel with the eye's blink expression fully on.
    private static float Travel(GltfModel model, string expression, Dictionary<SkinnedMesh, int[]> eye) =>
        eye.SelectMany(m => m.Value.Select(v => MathF.Abs(model.Expressions[expression].Binds.Where(b => b.Mesh == m.Key)
            .Aggregate(0f, (y, b) => y + m.Key.MorphTargets[b.Target].PositionDeltas[v].Y * b.Weight)))).Max();

    private sealed record Sample(double Time, Dictionary<SkinnedMesh, float[]> Weights, float FaceBlink, float FaceYaw, float FacePitch, float FaceRoll, float HeadYaw, float HeadPitch, float HeadRoll);

    private static List<Sample> Replay(MirrorCharacter character, string fixture)
    {
        var session = new MirrorSession(character);
        var bones = character.HumanoidBones;
        var samples = new List<Sample>();
        foreach (var frame in MirrorTests.Fixture(fixture))
        {
            session.Apply(frame, mirrored: false);
            for (var i = 0; i < 4; i++) session.Present(TimeSpan.FromSeconds(1 / 100d));
            if (session.Tracker.State != TrackingState.Tracking || session.Tracker.Face is not { } face) continue;
            var leaves = Leaves(session.Stage.CharacterShape).Where(l => l.Pose is not null).ToList();
            var world = leaves[0].Pose!.WorldTransforms;
            // Head orientation relative to the chest, in view space (the character faces the camera).
            var toView = session.Stage.CharacterToView;
            var head = Quaternion.CreateFromRotationMatrix(world[bones["head"]]);
            var chest = Quaternion.CreateFromRotationMatrix(world[bones[bones.ContainsKey("upperChest") ? "upperChest" : "chest"]]);
            var relative = toView * Quaternion.Conjugate(chest) * head * Quaternion.Conjugate(toView);
            var forward = Vector3.Transform(Vector3.UnitZ, relative);
            var up = Vector3.Transform(Vector3.UnitY, relative);
            var weights = new Dictionary<SkinnedMesh, float[]>();
            foreach (var leaf in leaves)
                if (leaf.SkinnedMesh is { } mesh && leaf.MorphWeights is { } w) weights[mesh] = w.ToArray();
            samples.Add(new(frame.Timestamp.TotalSeconds, weights, (face.BlinkLeft + face.BlinkRight) / 2, face.Yaw, face.Pitch, face.Roll,
                MathF.Atan2(forward.X, forward.Z), MathF.Asin(Math.Clamp(forward.Y, -1, 1)), MathF.Atan2(-up.X, up.Y)));
        }
        return samples;
        static IEnumerable<Shape> Leaves(Shape s) => s.Children.Count == 0 ? [s] : s.Children.SelectMany(Leaves);
    }

    private static float Correlation(IReadOnlyList<Sample> s, Func<Sample, float> a, Func<Sample, float> b)
    {
        double ma = s.Average(a), mb = s.Average(b), ab = 0, aa = 0, bb = 0;
        foreach (var x in s) { var da = a(x) - ma; var db = b(x) - mb; ab += da * db; aa += da * da; bb += db * db; }
        return (float)(ab / Math.Sqrt(aa * bb + 1e-12));
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

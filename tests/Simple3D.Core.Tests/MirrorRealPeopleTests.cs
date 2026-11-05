using System.Numerics;
using Simple3D.Core;
using Simple3D.Shared.Mirror;
using static MirrorFaceTests;

internal static class MirrorRealPeopleTests
{
    internal static void Run()
    {
        foreach (var bundled in MirrorModels.All) Face(bundled);
        var (_, cat) = Load(MirrorModels.All.Single(m => m.File == "cat.vrm"));
        Head(cat);
        Body(cat);
        Upper(cat);
    }

    private static void Face(MirrorModel bundled)
    {
        var (model, character) = Load(bundled);
        var samples = Replay(character, "face-nasa-menon", useFrameTiming: true);
        Check(samples.Count > 0, $"real face ({bundled.Name}): no tracking");
        var start = samples[0].Time - MirrorTests.Fixture("face-nasa-menon")[0].Timestamp.TotalSeconds;
        Check(start <= 1, $"real face ({bundled.Name}): tracking starts at {start:0.00}s");
        Console.WriteLine($"PASS mirror real face tracking ({bundled.Name}): {start:0.00}s, {samples.Count} frames");

        var ((min, max), _) = character.HeadBounds()!.Value;
        var height = max.Y - min.Y;
        var mouth = Moved(model, character.Face.Mouth.Keys, height);
        Check(mouth.Count > 0, $"real face ({bundled.Name}): no mouth vertices");
        float Mouth(Sample s) => Displacement(s, mouth) / height;
        var open = samples.Where(s => s.Frame.Face!.MouthOpen > .6f).ToArray();
        var closed = samples.Where(s => s.Frame.Face!.MouthOpen < .1f).ToArray();
        Check(open.Length > 0 && closed.Length > 0, "real face: needs open and closed mouth frames");
        var expression = character.Face.Mouth.MaxBy(e => e.Value).Key;
        var bind = model.Expressions[expression].Binds.MaxBy(b => b.Weight);
        float MouthWeight(Sample s) => s.Weights.TryGetValue(bind.Mesh, out var weights) ? weights[bind.Target] : 0;
        var correlation = Correlation(samples, s => s.Frame.Face!.MouthOpen, MouthWeight);
        var openMean = open.Average(Mouth);
        var closedMean = closed.Average(Mouth);
        // Measured raw opening vs shown mouth weight r=0.905 on all four models (627 tracked frames each).
        // Expression weights isolate opening from smile-driven vertex displacement; retain at least r=0.85.
        Check(correlation >= .85f && openMean >= MouthOpenShare && closedMean < MouthOpenShare / 2,
            $"real face ({bundled.Name}): mouth r={correlation:0.000}, open {openMean:P2}, closed {closedMean:P2} ({open.Length}/{closed.Length} frames)");
        Console.WriteLine($"PASS mirror real face mouth ({bundled.Name}): r={correlation:0.000}, {samples.Count} frames, open {openMean:P1}, closed {closedMean:P1}");

        if (character.Face.SeparateEyes || model.Expressions.ContainsKey("blink"))
        {
            var (left, right) = character.Face.SeparateEyes
                ? (Moved(model, ["blink_l"], height), Moved(model, ["blink_r"], height))
                : Split(Moved(model, ["blink"], height));
            Check(left.Values.Sum(v => v.Length) > 0 && right.Values.Sum(v => v.Length) > 0, "real face: no eyelid vertices");
            var leftTravel = Travel(model, character.Face.SeparateEyes ? "blink_l" : "blink", left);
            var rightTravel = Travel(model, character.Face.SeparateEyes ? "blink_r" : "blink", right);
            var blinks = samples.Where(s => s.Frame.Face is { BlinkLeft: > .7f, BlinkRight: > .7f }).ToArray();
            Check(blinks.Length > 0, "real face: needs bilateral blink frames");
            var leftClosure = blinks.Max(s => Closure(s, left, leftTravel));
            var rightClosure = blinks.Max(s => Closure(s, right, rightTravel));
            Check(leftClosure >= BlinkClosure && rightClosure >= BlinkClosure,
                $"real face ({bundled.Name}): blinks {leftClosure:P1}/{rightClosure:P1}");
            Console.WriteLine($"PASS mirror real face blinks ({bundled.Name}): {leftClosure:P0}/{rightClosure:P0}, {blinks.Length} frames");
        }
        var yaw = Correlation(samples, s => s.Frame.Face!.Yaw, s => s.HeadYaw);
        Check(yaw >= .8f, $"real face ({bundled.Name}): yaw r={yaw:0.000}");
        Console.WriteLine($"PASS mirror real face yaw ({bundled.Name}): r={yaw:0.000}");
    }

    private static void Head(MirrorCharacter cat)
    {
        var samples = Replay(cat, "head-niosh-sudha", useFrameTiming: true);
        Check(samples.Count > 0, "real head: no tracked faces");
        var roll = Correlation(samples, s => s.Frame.Face!.Roll, s => s.HeadRoll);
        var yaw = Correlation(samples, s => s.Frame.Face!.Yaw, s => s.HeadYaw);
        Check(roll >= .8f && yaw >= .8f, $"real head: roll r={roll:0.000}, yaw r={yaw:0.000}");
        Console.WriteLine($"PASS mirror real head: roll r={roll:0.000}, yaw r={yaw:0.000}, {samples.Count} frames");
    }

    private sealed record BodySample(PoseFrame Frame, Matrix4x4[] World);

    private static List<BodySample> ReplayBody(MirrorCharacter character, string fixture, out float tracking)
    {
        var session = new MirrorSession(character);
        var frames = MirrorTests.Fixture(fixture);
        var samples = new List<BodySample>();
        int eligible = 0, tracked = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            session.Apply(frame, mirrored: false);
            PresentFrame(session, frames, i);
            if (frame.Timestamp - frames[0].Timestamp < TimeSpan.FromSeconds(1)) continue;
            eligible++;
            if (session.Tracker.State != TrackingState.Tracking) continue;
            tracked++;
            var leaf = Leaves(session.Stage.CharacterShape).First(s => s.Pose is not null);
            samples.Add(new(frame, leaf.Pose!.WorldTransforms.ToArray()));
        }
        tracking = eligible > 0 ? (float)tracked / eligible : 0;
        return samples;
    }

    private static bool Visible(PoseFrame frame, params BodyJoint[] joints) =>
        joints.All(j => frame[j].Confidence >= .3f);

    private static void Body(MirrorCharacter cat)
    {
        var samples = ReplayBody(cat, "body-cdc-squat", out var tracking);
        Check(tracking >= .95f, $"real squat: tracking {tracking:P1}");
        Console.WriteLine($"PASS mirror real squat tracking: {tracking:P1}, {samples.Count} frames");
        var bones = cat.HumanoidBones;
        var visible = samples.Where(s => Visible(s.Frame, BodyJoint.LeftHip, BodyJoint.RightHip,
            BodyJoint.LeftKnee, BodyJoint.RightKnee, BodyJoint.LeftAnkle, BodyJoint.RightAnkle)).ToArray();
        Check(visible.Length > 0, "real squat: no visible hips and legs");
        float Hip(BodySample s) => (s.Frame[BodyJoint.LeftHip].Y + s.Frame[BodyJoint.RightHip].Y) / 2;
        var correlation = Correlation(visible, Hip, s => s.World[bones["hips"]].Translation.Y);
        Check(correlation <= -.7f, $"real squat: hip image y vs character world y r={correlation:0.000}");
        Console.WriteLine($"PASS mirror real squat hips: r={correlation:0.000}, {visible.Length} frames");

        float Knee(BodySample s, string side)
        {
            var upper = s.World[bones[side + "UpperLeg"]].Translation;
            var knee = s.World[bones[side + "LowerLeg"]].Translation;
            var foot = s.World[bones[side + "Foot"]].Translation;
            return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(knee - upper), Vector3.Normalize(foot - knee)), -1, 1)) * 180 / MathF.PI;
        }
        // Image hip-height deciles select the deepest and highest poses independently of the character.
        var ordered = visible.OrderBy(Hip).ToArray();
        var count = Math.Max(1, ordered.Length / 10);
        var high = ordered.Take(count).SelectMany(s => new[] { Knee(s, "left"), Knee(s, "right") }).ToArray();
        var deep = ordered.TakeLast(count).SelectMany(s => new[] { Knee(s, "left"), Knee(s, "right") }).ToArray();
        Check(deep.Min() > 25 && high.Max() < 15,
            $"real squat: deep knees {deep.Min():0.0}–{deep.Max():0.0}°, high {high.Min():0.0}–{high.Max():0.0}°");
        Console.WriteLine($"PASS mirror real squat knees: deep {deep.Min():0.0}–{deep.Max():0.0}°, high {high.Min():0.0}–{high.Max():0.0}°, {count} frames each");
    }

    private static void Upper(MirrorCharacter cat)
    {
        var samples = ReplayBody(cat, "upper-cdc-press", out _);
        var bones = cat.HumanoidBones;
        int raised = 0, raisedRight = 0, lowered = 0, loweredRight = 0;
        foreach (var s in samples)
        {
            var f = s.Frame;
            if (!Visible(f, BodyJoint.Nose, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftWrist, BodyJoint.RightWrist)) continue;
            var head = s.World[bones["head"]].Translation.Y;
            var left = s.World[bones["leftHand"]].Translation.Y;
            var right = s.World[bones["rightHand"]].Translation.Y;
            if (f[BodyJoint.LeftWrist].Y < f[BodyJoint.Nose].Y && f[BodyJoint.RightWrist].Y < f[BodyJoint.Nose].Y)
            {
                raised++;
                if (left > head && right > head) raisedRight++;
            }
            else if (f[BodyJoint.LeftWrist].Y > f[BodyJoint.LeftShoulder].Y && f[BodyJoint.RightWrist].Y > f[BodyJoint.RightShoulder].Y)
            {
                lowered++;
                if (left < head && right < head) loweredRight++;
            }
        }
        Check(raised > 0 && lowered > 0 && raisedRight >= raised * .9f && loweredRight >= lowered * .9f,
            $"real press: raised {raisedRight}/{raised}, lowered {loweredRight}/{lowered}");
        Console.WriteLine($"PASS mirror real press: raised {raisedRight}/{raised} ({(float)raisedRight / raised:P1}), lowered {loweredRight}/{lowered} ({(float)loweredRight / lowered:P1})");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

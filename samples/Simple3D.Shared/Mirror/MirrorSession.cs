using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>Turns pose frames into posed, expressive characters on the pond stage. Use on one thread (the UI thread in an app).</summary>
public sealed class MirrorSession
{
    private readonly HumanoidRetargeter[] _retargeters;

    /// <summary>The stage holding the scene and camera.</summary>
    public MirrorStage Stage { get; }
    /// <summary>Tracking state and smoothed landmarks.</summary>
    public PoseTracker Tracker { get; } = new();
    /// <summary>Raised when tracking starts, is lost or resumes.</summary>
    public event Action<TrackingState>? StateChanged;

    /// <summary>Creates a session in which every character copies the same person.</summary>
    public MirrorSession(IReadOnlyList<MirrorCharacter> characters)
    {
        Stage = new MirrorStage(characters);
        // Side by side, each character follows the person sideways only a little, so they keep their spots.
        var sideways = characters.Count == 1 ? .6f : .25f;
        _retargeters = characters.Select((c, i) => new HumanoidRetargeter(c.Skeleton, c.HumanoidBones, Stage.CharacterToView[i]) { SidewaysRange = sideways }).ToArray();
        Tracker.StateChanged += state =>
        {
            // A new person may have different proportions; the characters appear once someone is tracked.
            if (state == TrackingState.Tracking) foreach (var r in _retargeters) r.Reset();
            Stage.ShowCharacters(state != TrackingState.Searching);
            StateChanged?.Invoke(state);
        };
    }

    /// <summary>Applies one analyzed frame. Returns true when the characters changed.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="mirrored">Whether the person sees a mirrored preview.</param>
    public bool Apply(PoseFrame frame, bool mirrored)
    {
        if (!Tracker.Update(frame)) return false;
        var face = Tracker.Face;
        var expressions = face is null ? Neutral : Expressions(face, mirrored);
        for (var i = 0; i < _retargeters.Length; i++)
        {
            var pose = _retargeters[i].Retarget(Tracker.Landmarks, Tracker.ImageAspect, mirrored, face);
            var express = Stage.Characters[i].Express;
            Stage.Update(i, shape => express is null ? shape.WithPose(pose) : express(shape.WithPose(pose), expressions));
        }
        return true;
    }

    private static readonly IReadOnlyDictionary<string, float> Neutral = new Dictionary<string, float>();

    /// <summary>Maps face measurements to VRM expression intensities. A mirrored preview swaps the eyes, like the body.</summary>
    public static IReadOnlyDictionary<string, float> Expressions(FaceFrame face, bool mirrored)
    {
        ArgumentNullException.ThrowIfNull(face);
        var (left, right) = mirrored ? (face.BlinkRight, face.BlinkLeft) : (face.BlinkLeft, face.BlinkRight);
        return new Dictionary<string, float>
        {
            ["a"] = face.MouthOpen,
            ["joy"] = face.Smile,
            ["angry"] = Math.Clamp(-face.Brows, 0, 1),
            ["surprised"] = Math.Clamp(face.Brows, 0, 1),
            // Models without separate eyes have "blink"; models with both use the per-eye presets.
            ["blink_l"] = left, ["blink_r"] = right, ["blink"] = MathF.Min(left, right),
        };
    }

    /// <summary>Forgets the person's limb lengths, for example after the camera zoom changed their size in the image.</summary>
    public void Recalibrate() { foreach (var r in _retargeters) r.ForgetLengths(); }

    /// <summary>Forgets the person and hides the characters.</summary>
    public void Reset() => Tracker.Reset();
}

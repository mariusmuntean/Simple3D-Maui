using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>Turns pose frames into a posed, expressive character on the pond stage. Use on one thread (the UI thread in an app).</summary>
public sealed class MirrorSession
{
    private HumanoidRetargeter _retargeter;

    /// <summary>The stage holding the scene and camera.</summary>
    public MirrorStage Stage { get; }
    /// <summary>Tracking state and smoothed landmarks.</summary>
    public PoseTracker Tracker { get; } = new();
    /// <summary>Raised when tracking starts, is lost or resumes.</summary>
    public event Action<TrackingState>? StateChanged;

    /// <summary>Creates a session in which the character copies the tracked person.</summary>
    public MirrorSession(MirrorCharacter character)
    {
        Stage = new MirrorStage(character);
        _retargeter = new HumanoidRetargeter(character.Skeleton, character.HumanoidBones, Stage.CharacterToView) { TorsoRadius = character.TorsoRadius };
        Tracker.StateChanged += state =>
        {
            // A new person may have different proportions; the character appears once someone is tracked.
            if (state == TrackingState.Tracking) _retargeter.Reset();
            Stage.ShowCharacter(state != TrackingState.Searching);
            StateChanged?.Invoke(state);
        };
    }

    /// <summary>Applies one analyzed frame. Returns true when the character changed.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="mirrored">Whether the person sees a mirrored preview.</param>
    public bool Apply(PoseFrame frame, bool mirrored)
    {
        if (!Tracker.Update(frame)) return false;
        var face = Tracker.Face;
        var expressions = face is null ? Neutral : Expressions(face, mirrored);
        var pose = _retargeter.Retarget(Tracker.Landmarks, Tracker.ImageAspect, mirrored, face);
        var express = Stage.Character.Express;
        Stage.Update(shape => express is null ? shape.WithPose(pose) : express(shape.WithPose(pose), expressions));
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

    /// <summary>Switches to another character, animated when one is on stage. Tracking carries on, so the new character
    /// copies the person from its first frame.</summary>
    public void SwitchCharacter(MirrorCharacter character)
    {
        Stage.Swap(character);
        _retargeter = new HumanoidRetargeter(character.Skeleton, character.HumanoidBones, Stage.CharacterToView) { TorsoRadius = character.TorsoRadius };
    }

    /// <summary>Forgets the person's limb lengths, for example after the camera zoom changed their size in the image.</summary>
    public void Recalibrate() => _retargeter.ForgetLengths();

    /// <summary>Forgets the person and hides the character.</summary>
    public void Reset() => Tracker.Reset();
}

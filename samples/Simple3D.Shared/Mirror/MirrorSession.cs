using System.Numerics;
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
        var tracking = Tracker.State == TrackingState.Tracking;
        if (!Tracker.Update(frame)) return false;
        var face = Tracker.Face;
        _target = _retargeter.Retarget(Tracker.Landmarks, Tracker.ImageAspect, mirrored, face);
        _targetExpressions = face is null ? Neutral : Stage.Character.Face.Expressions(face, mirrored);
        // A fresh acquisition snaps; while tracking, Present eases toward each new target.
        if (!tracking || _shown is null) Show(_target, _targetExpressions);
        return true;
    }

    /// <summary>Advances the shown pose toward the latest target. Call once per displayed frame with the time since the
    /// previous one: the detector delivers poses at camera rate (often 25–30 Hz), and easing between them at the display's
    /// rate turns those steps into smooth motion, independent of the frame rate.</summary>
    public void Present(TimeSpan elapsed)
    {
        if (_target is not { } target || _shown is not { } shown) return;
        var dt = (float)elapsed.TotalSeconds;
        // Exponential easing: frame-rate independent; a long gap (backgrounded app) snaps to the target.
        var t = dt is > 0 and < .5f ? 1 - MathF.Exp(-dt / FollowSeconds) : 1;
        if (t >= 1) { Show(target, _targetExpressions); return; }
        var rotations = _rotations ??= new Quaternion[target.Rotations.Count];
        for (var i = 0; i < rotations.Length; i++) rotations[i] = Quaternion.Slerp(shown.Rotations[i], target.Rotations[i], t);
        var offset = Vector3.Lerp(shown.RootOffset, target.RootOffset, t);
        var expressions = new Dictionary<string, float>(_targetExpressions.Count);
        foreach (var (name, value) in _targetExpressions)
            expressions[name] = _shownExpressions.TryGetValue(name, out var current) ? current + (value - current) * t : value;
        foreach (var (name, value) in _shownExpressions)
            if (!_targetExpressions.ContainsKey(name)) expressions[name] = value * (1 - t);
        Show(SkeletonPose.Create(target.Skeleton, rotations, offset), expressions);
    }

    private void Show(SkeletonPose pose, IReadOnlyDictionary<string, float> expressions)
    {
        _shown = pose;
        _shownExpressions = expressions;
        var express = Stage.Character.Express;
        Stage.Update(shape => express is null ? shape.WithPose(pose) : express(shape.WithPose(pose), expressions));
    }

    /// <summary>The latest tracked pose, which the shown pose eases toward; null before tracking starts.</summary>
    public SkeletonPose? TargetPose => _target;

    /// <summary>How quickly the shown pose catches up with the tracked one: about 63% of the way per this many seconds.</summary>
    public const float FollowSeconds = .05f;
    private SkeletonPose? _target, _shown;
    private IReadOnlyDictionary<string, float> _targetExpressions = Neutral, _shownExpressions = Neutral;
    private Quaternion[]? _rotations;

    private static readonly IReadOnlyDictionary<string, float> Neutral = new Dictionary<string, float>();

    /// <summary>Switches to another character, animated when one is on stage. Tracking carries on, so the new character
    /// copies the person from its first frame.</summary>
    public void SwitchCharacter(MirrorCharacter character)
    {
        Stage.Swap(character);
        // Poses belong to the old skeleton; the next tracked frame snaps the new character into place.
        _target = _shown = null;
        _rotations = null;
        _retargeter = new HumanoidRetargeter(character.Skeleton, character.HumanoidBones, Stage.CharacterToView) { TorsoRadius = character.TorsoRadius };
    }

    /// <summary>Forgets the person's limb lengths, for example after the camera zoom changed their size in the image.</summary>
    public void Recalibrate() => _retargeter.ForgetLengths();

    /// <summary>Forgets the person and hides the character.</summary>
    public void Reset() => Tracker.Reset();
}

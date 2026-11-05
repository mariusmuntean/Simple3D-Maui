using Simple3D.Core;

namespace Simple3D.Shared.Mirror;

/// <summary>Turns pose frames into the posed character on the pond stage. Use on one thread (the UI thread in an app).</summary>
public sealed class MirrorSession
{
    private readonly HumanoidRetargeter _retargeter;

    /// <summary>The stage holding the scene and camera.</summary>
    public MirrorStage Stage { get; }
    /// <summary>Tracking state and smoothed landmarks.</summary>
    public PoseTracker Tracker { get; } = new();
    /// <summary>Raised when tracking starts, is lost or resumes.</summary>
    public event Action<TrackingState>? StateChanged;

    /// <summary>Creates a session for a humanoid character.</summary>
    /// <param name="character">The character in its rest pose.</param>
    /// <param name="skeleton">The skeleton that poses the character.</param>
    /// <param name="humanoidBones">VRM humanoid bone name → joint index.</param>
    /// <param name="facesPositiveZ">Whether the model looks along +Z (VRM 1.0) rather than -Z (VRM 0.x).</param>
    public MirrorSession(Shape character, Skeleton skeleton, IReadOnlyDictionary<string, int> humanoidBones, bool facesPositiveZ)
    {
        Stage = new MirrorStage(character, facesPositiveZ);
        _retargeter = new HumanoidRetargeter(skeleton, humanoidBones, Stage.CharacterToView);
        Tracker.StateChanged += state =>
        {
            // A new person may have different proportions; the character appears once someone is tracked.
            if (state == TrackingState.Tracking) _retargeter.Reset();
            Stage.ShowCharacter(state != TrackingState.Searching);
            StateChanged?.Invoke(state);
        };
    }

    /// <summary>Applies one analyzed frame. Returns true when the character's pose changed.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="mirrored">Whether the person sees a mirrored preview.</param>
    public bool Apply(PoseFrame frame, bool mirrored)
    {
        if (!Tracker.Update(frame)) return false;
        Stage.Pose(_retargeter.Retarget(Tracker.Landmarks, Tracker.ImageAspect, mirrored));
        return true;
    }

    /// <summary>Forgets the person and hides the character.</summary>
    public void Reset() => Tracker.Reset();
}

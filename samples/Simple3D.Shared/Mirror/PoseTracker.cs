using System.Numerics;
namespace Simple3D.Shared.Mirror;

/// <summary>Whether a person is being followed.</summary>
public enum TrackingState
{
    /// <summary>Waiting for a whole body to appear.</summary>
    Searching,
    /// <summary>A person is in frame and drives the character.</summary>
    Tracking,
    /// <summary>The tracked person left the frame; the character holds its last pose.</summary>
    Lost
}

/// <summary>Turns detector frames into stable tracking states and smoothed landmarks. Not thread safe; feed frames in order.</summary>
/// <remarks>Acquiring needs a face, or shoulders plus hips or elbows, for several consecutive frames, so a passer-by's arm
/// does not start tracking. While tracking, visible shoulders or a face are enough to continue, so a hand leaving the
/// frame does not interrupt it.</remarks>
public sealed class PoseTracker
{
    /// <summary>Consecutive whole-body frames needed to start or resume tracking.</summary>
    public int AcquireFrames { get; init; } = 4;
    /// <summary>Time without a visible torso before tracking is lost.</summary>
    public TimeSpan LoseAfter { get; init; } = TimeSpan.FromSeconds(.6);
    /// <summary>Minimum landmark confidence counted as visible.</summary>
    public float MinimumConfidence { get; init; } = .3f;

    private readonly OneEuroFilter[] _filters = Enumerable.Range(0, PoseFrame.JointCount * 2).Select(_ => new OneEuroFilter()).ToArray();
    private readonly Landmark[] _smoothed = new Landmark[PoseFrame.JointCount];
    private readonly bool[] _visible = new bool[PoseFrame.JointCount];
    private readonly int[] _jumps = new int[PoseFrame.JointCount];
    private const float HideBelow = .2f, TrustedConfidence = .6f, JumpLimit = .35f;
    private float? _smileBaseline;
    private float _browBaseline;
    private double? _lastFace;
    private int _acquireFrames;
    private TimeSpan _lastTorso;

    /// <summary>The current state.</summary>
    public TrackingState State { get; private set; } = TrackingState.Searching;
    /// <summary>Raised when <see cref="State"/> changes, after the frame that caused it was applied.</summary>
    public event Action<TrackingState>? StateChanged;
    /// <summary>Smoothed landmarks of the most recent frame with visible shoulders; joints below the confidence threshold keep their last value.</summary>
    public IReadOnlyList<Landmark> Landmarks => _smoothed;
    /// <summary>Smoothed face measurements of the most recent frame, or null when it had no face.</summary>
    public FaceFrame? Face { get; private set; }
    private readonly OneEuroFilter[] _faceFilters = Enumerable.Range(0, 8).Select(_ => new OneEuroFilter()).ToArray();
    /// <summary>Aspect ratio (width / height) of the analyzed images.</summary>
    public float ImageAspect { get; private set; } = 1;

    /// <summary>Applies one frame. Returns true when <see cref="Landmarks"/> changed and should drive the character.</summary>
    public bool Update(PoseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var acquirable = CanAcquire(frame);
        var torso = acquirable || HasShoulders(frame) || frame.Face is not null;
        _acquireFrames = acquirable ? _acquireFrames + 1 : 0;
        if (torso) _lastTorso = frame.Timestamp;
        var next = State switch
        {
            TrackingState.Tracking when !torso && frame.Timestamp - _lastTorso >= LoseAfter => TrackingState.Lost,
            not TrackingState.Tracking when _acquireFrames >= AcquireFrames => TrackingState.Tracking,
            _ => State
        };
        var updated = false;
        if (torso && (State == TrackingState.Tracking || next == TrackingState.Tracking))
        {
            // Reset smoothing on (re)acquisition so the first pose snaps instead of sliding from a stale one.
            if (State != TrackingState.Tracking)
            {
                foreach (var filter in _filters.Concat(_faceFilters)) filter.Reset();
                Array.Clear(_visible);
                _smileBaseline = null; _lastFace = null;
            }
            Smooth(frame);
            updated = true;
        }
        if (next != State)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
        return updated;
    }

    /// <summary>Returns to <see cref="TrackingState.Searching"/>, for example when the camera restarts.</summary>
    public void Reset()
    {
        _acquireFrames = 0;
        foreach (var filter in _filters) filter.Reset();
        if (State == TrackingState.Searching) return;
        State = TrackingState.Searching;
        StateChanged?.Invoke(State);
    }

    /// <summary>Whether enough of a person is in frame to start: a face, or both shoulders plus both hips or both elbows.
    /// A whole body, the waist up, the torso and arms, or just head and face all qualify; whatever is out of frame
    /// keeps its last pose (legs standing, for a start).</summary>
    public bool CanAcquire(PoseFrame frame) => frame.Face is not null || HasShoulders(frame) &&
        (Visible(frame, BodyJoint.LeftHip, BodyJoint.RightHip) || Visible(frame, BodyJoint.LeftElbow, BodyJoint.RightElbow));

    private bool HasShoulders(PoseFrame frame) => frame.Landmarks is not null && Visible(frame, BodyJoint.LeftShoulder, BodyJoint.RightShoulder);

    private bool Visible(PoseFrame frame, params ReadOnlySpan<BodyJoint> joints)
    {
        foreach (var joint in joints)
            if (!(frame[joint].Confidence >= MinimumConfidence)) return false;
        return true;
    }

    private void Smooth(PoseFrame frame)
    {
        ImageAspect = frame.ImageHeight > 0 ? (float)frame.ImageWidth / frame.ImageHeight : 1;
        var seconds = frame.Timestamp.TotalSeconds;
        if (frame.Face is not { } face)
        {
            // A face that disappears (for example when the person turns away) relaxes the expression instead of freezing it.
            if (Face is not null) foreach (var filter in _faceFilters) filter.Reset();
            Face = null;
        }
        else
        {
            float F(int i, float value) => _faceFilters[i].Filter(value, seconds);
            // Neutral brow height and mouth width differ between people (and bangs hide brows), so both are measured
            // against this person's own neutral: the smile baseline drops quickly to their least smiling value and rises
            // very slowly, so a held smile still counts; the brow baseline follows slowly both ways.
            var dt = _lastFace is { } last ? Math.Clamp((float)(seconds - last), 0, .5f) : 0;
            _lastFace = seconds;
            if (_smileBaseline is not { } smileBase) { _smileBaseline = face.Smile; _browBaseline = face.Brows; }
            else
            {
                _smileBaseline = smileBase + (face.Smile - smileBase) * (1 - MathF.Exp(-dt / (face.Smile < smileBase ? 1f : 30f)));
                _browBaseline += (face.Brows - _browBaseline) * (1 - MathF.Exp(-dt / 10f));
            }
            var smile = Math.Clamp((face.Smile - _smileBaseline!.Value) / MathF.Max(.25f, 1 - _smileBaseline.Value), 0, 1);
            var brows = Math.Clamp(face.Brows - _browBaseline, -1, 1);
            Face = new(F(0, face.Yaw), F(1, face.Pitch), F(2, face.Roll), F(3, face.MouthOpen), F(4, smile), F(5, brows), F(6, face.BlinkLeft), F(7, face.BlinkRight));
        }
        // Typical body size in this frame, to judge whether a joint moved implausibly far.
        var scale = BodyScale(frame);
        for (var i = 0; i < PoseFrame.JointCount; i++)
        {
            var landmark = frame.Landmarks?[i] ?? default;
            var finite = float.IsFinite(landmark.X) && float.IsFinite(landmark.Y);
            var wasVisible = _visible[i];
            // Hysteresis: a joint hovering around the threshold must not flicker between hidden and visible.
            var visible = finite && landmark.Confidence >= (wasVisible ? HideBelow : MinimumConfidence);
            if (visible && wasVisible && landmark.Confidence < TrustedConfidence &&
                Vector2.Distance(new(landmark.X, landmark.Y), new(_smoothed[i].X, _smoothed[i].Y)) > JumpLimit * scale && ++_jumps[i] < 2)
                continue; // An uncertain joint leaping across the body is usually a misdetection; believe it only if it persists.
            _jumps[i] = 0;
            _visible[i] = visible;
            if (!visible)
            {
                // Hidden joints keep their last position and report zero confidence.
                _smoothed[i] = _smoothed[i] with { Confidence = 0 };
                continue;
            }
            if (!wasVisible) { _filters[i * 2].Reset(); _filters[i * 2 + 1].Reset(); }
            var x = _filters[i * 2].Filter(landmark.X, seconds);
            var y = _filters[i * 2 + 1].Filter(landmark.Y, seconds);
            // Uncertain positions move the result less.
            var weight = wasVisible ? Math.Clamp(landmark.Confidence / TrustedConfidence, .35f, 1) : 1;
            _smoothed[i] = new(_smoothed[i].X + (x - _smoothed[i].X) * weight, _smoothed[i].Y + (y - _smoothed[i].Y) * weight,
                MathF.Max(landmark.Confidence, MinimumConfidence));
        }
    }

    private float BodyScale(PoseFrame frame)
    {
        if (frame.Landmarks is not { } lm) return .25f;
        Vector2 At(BodyJoint j) => new(lm[(int)j].X, lm[(int)j].Y);
        var shoulders = (At(BodyJoint.LeftShoulder) + At(BodyJoint.RightShoulder)) / 2;
        if (HasShoulders(frame) && Visible(frame, BodyJoint.LeftHip, BodyJoint.RightHip))
            return Vector2.Distance(shoulders, (At(BodyJoint.LeftHip) + At(BodyJoint.RightHip)) / 2);
        return HasShoulders(frame) ? 1.5f * Vector2.Distance(At(BodyJoint.LeftShoulder), At(BodyJoint.RightShoulder)) : .25f;
    }
}

/// <summary>One Euro filter (Casiez et al. 2012): little smoothing during fast motion, strong smoothing when still.</summary>
public sealed class OneEuroFilter
{
    /// <summary>Cutoff frequency in Hz when still; lower removes more jitter.</summary>
    public double MinimumCutoff { get; init; } = 1.7;
    /// <summary>How quickly the cutoff rises with speed; higher reduces lag.</summary>
    public double SpeedCoefficient { get; init; } = 2.5;
    private const double DerivativeCutoff = 1;
    private double? _time;
    private double _value, _derivative;

    /// <summary>Filters one sample taken at <paramref name="seconds"/>.</summary>
    public float Filter(float value, double seconds)
    {
        // First sample, or time went backwards (a looping video): restart from the sample.
        if (_time is not { } previous || seconds < previous)
        {
            (_value, _derivative, _time) = (value, 0, seconds);
            return value;
        }
        if (seconds == previous) return (float)_value;
        var elapsed = seconds - previous;
        _time = seconds;
        _derivative = Smooth(_derivative, (value - _value) / elapsed, DerivativeCutoff, elapsed);
        _value = Smooth(_value, value, MinimumCutoff + SpeedCoefficient * Math.Abs(_derivative), elapsed);
        return (float)_value;
    }

    /// <summary>Forgets history; the next sample passes through unchanged.</summary>
    public void Reset() => _time = null;

    private static double Smooth(double previous, double value, double cutoff, double elapsed)
    {
        var tau = 1 / (2 * Math.PI * cutoff);
        var alpha = 1 / (1 + tau / elapsed);
        return previous + alpha * (value - previous);
    }
}

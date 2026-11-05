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
/// <remarks>Acquiring needs a whole body for several consecutive frames, so a passer-by's arm does not start tracking.
/// While tracking, a visible torso is enough to continue, so a hand leaving the frame does not interrupt it.</remarks>
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
    private int _wholeBodyFrames;
    private TimeSpan _lastTorso;

    /// <summary>The current state.</summary>
    public TrackingState State { get; private set; } = TrackingState.Searching;
    /// <summary>Raised when <see cref="State"/> changes, after the frame that caused it was applied.</summary>
    public event Action<TrackingState>? StateChanged;
    /// <summary>Smoothed landmarks of the most recent frame with a visible torso; joints below the confidence threshold keep their last value.</summary>
    public IReadOnlyList<Landmark> Landmarks => _smoothed;
    /// <summary>Aspect ratio (width / height) of the analyzed images.</summary>
    public float ImageAspect { get; private set; } = 1;

    /// <summary>Applies one frame. Returns true when <see cref="Landmarks"/> changed and should drive the character.</summary>
    public bool Update(PoseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var wholeBody = IsWholeBody(frame);
        var torso = wholeBody || HasTorso(frame);
        _wholeBodyFrames = wholeBody ? _wholeBodyFrames + 1 : 0;
        if (torso) _lastTorso = frame.Timestamp;
        var next = State switch
        {
            TrackingState.Tracking when !torso && frame.Timestamp - _lastTorso >= LoseAfter => TrackingState.Lost,
            not TrackingState.Tracking when _wholeBodyFrames >= AcquireFrames => TrackingState.Tracking,
            _ => State
        };
        var updated = false;
        if (torso && (State == TrackingState.Tracking || next == TrackingState.Tracking))
        {
            // Reset smoothing on (re)acquisition so the first pose snaps instead of sliding from a stale one.
            if (State != TrackingState.Tracking) foreach (var filter in _filters) filter.Reset();
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
        _wholeBodyFrames = 0;
        foreach (var filter in _filters) filter.Reset();
        if (State == TrackingState.Searching) return;
        State = TrackingState.Searching;
        StateChanged?.Invoke(State);
    }

    /// <summary>Whether shoulders, hips, knees and ankles are all visible: the whole body is in frame.</summary>
    public bool IsWholeBody(PoseFrame frame) => frame.Landmarks is not null &&
        Visible(frame, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftHip, BodyJoint.RightHip,
            BodyJoint.LeftKnee, BodyJoint.RightKnee, BodyJoint.LeftAnkle, BodyJoint.RightAnkle);

    private bool HasTorso(PoseFrame frame) => frame.Landmarks is not null &&
        Visible(frame, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftHip, BodyJoint.RightHip);

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
        for (var i = 0; i < PoseFrame.JointCount; i++)
        {
            var landmark = frame.Landmarks![i];
            if (!(landmark.Confidence >= MinimumConfidence) || !float.IsFinite(landmark.X) || !float.IsFinite(landmark.Y))
            {
                // Hidden joints keep their last position but report the low confidence.
                _smoothed[i] = _smoothed[i] with { Confidence = landmark.Confidence };
                continue;
            }
            _smoothed[i] = new(_filters[i * 2].Filter(landmark.X, seconds), _filters[i * 2 + 1].Filter(landmark.Y, seconds), landmark.Confidence);
        }
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

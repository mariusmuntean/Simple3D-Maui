namespace Simple3D.Shared.Mirror;

/// <summary>Body landmarks reported by every supported detector (Apple Vision and Google ML Kit).</summary>
public enum BodyJoint
{
    Nose, LeftShoulder, RightShoulder, LeftElbow, RightElbow, LeftWrist, RightWrist,
    LeftHip, RightHip, LeftKnee, RightKnee, LeftAnkle, RightAnkle
}

/// <summary>A detected landmark in normalized coordinates of the upright, unmirrored image: origin top left, X right, Y down, both in [0,1].</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
/// <param name="Confidence">Detector confidence in [0,1]; 0 when the joint was not found.</param>
public readonly record struct Landmark(float X, float Y, float Confidence);

/// <summary>One analyzed image. <see cref="Landmarks"/> is indexed by <see cref="BodyJoint"/>, or null when no person was found.</summary>
/// <param name="Timestamp">Capture time of the image, monotonic within one source.</param>
/// <param name="ImageWidth">Upright image width in pixels.</param>
/// <param name="ImageHeight">Upright image height in pixels.</param>
/// <param name="Landmarks">One landmark per <see cref="BodyJoint"/>, or null.</param>
public sealed record PoseFrame(TimeSpan Timestamp, int ImageWidth, int ImageHeight, Landmark[]? Landmarks)
{
    /// <summary>Number of landmarks per frame.</summary>
    public const int JointCount = 13;
    /// <summary>The landmark of one joint.</summary>
    public Landmark this[BodyJoint joint] => Landmarks is { } values ? values[(int)joint] : default;
}

/// <summary>A camera or video that produces pose frames on a background thread.</summary>
public interface IPoseSource : IAsyncDisposable
{
    /// <summary>Raised for every analyzed image, on a background thread. Frames arrive in timestamp order.</summary>
    event Action<PoseFrame>? FrameAnalyzed;
    /// <summary>True when the preview is shown mirrored (front camera), so the character should mirror the person.</summary>
    bool IsMirrored { get; }
    /// <summary>Starts capture and analysis. Requires camera permission for camera sources.</summary>
    Task StartAsync();
    /// <summary>Stops capture; may be restarted.</summary>
    Task StopAsync();
}

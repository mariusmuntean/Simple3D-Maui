using Simple3D.Shared.Mirror;

namespace Simple3D.Demo.Mirror;

/// <summary>Sample diagnostics without changing the portable pose-source contract.</summary>
public abstract class PoseSource : IPoseSource
{
    public event Action<PoseFrame>? FrameAnalyzed;
    public event Action<Exception>? Failed;
    public event Action<double>? DetectionMeasured;
    public Task Completion { get; protected set; } = Task.CompletedTask;
    public abstract bool IsMirrored { get; }
    public abstract Task StartAsync();
    public abstract Task StopAsync();
    public abstract ValueTask DisposeAsync();
    /// <summary>Largest zoom factor the running camera supports; 1 for sources without zoom.</summary>
    public virtual float MaximumZoom => 1;
    /// <summary>Camera zoom (crops what the detector sees too), clamped to [1, <see cref="MaximumZoom"/>]. Set on the UI thread.</summary>
    public virtual float Zoom { get => 1; set { } }

    protected void Publish(PoseFrame frame, double detectMilliseconds)
    {
        DetectionMeasured?.Invoke(detectMilliseconds);
        FrameAnalyzed?.Invoke(frame);
    }

    protected void ReportError(Exception error) => Failed?.Invoke(error);
}

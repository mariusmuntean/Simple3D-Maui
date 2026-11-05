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

    protected void Publish(PoseFrame frame, double detectMilliseconds)
    {
        DetectionMeasured?.Invoke(detectMilliseconds);
        FrameAnalyzed?.Invoke(frame);
    }

    protected void ReportError(Exception error) => Failed?.Invoke(error);
}

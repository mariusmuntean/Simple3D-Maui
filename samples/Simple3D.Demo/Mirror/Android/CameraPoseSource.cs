#if ANDROID
using System.Diagnostics;
using Android.Runtime;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using Xamarin.Google.MLKit.Vision.Common;
using Size = Android.Util.Size;

namespace Simple3D.Demo.Mirror;

/// <summary>CameraX front camera analyzed by ML Kit; KEEP_ONLY_LATEST drops frames while one is being analyzed.</summary>
/// <summary>A camera analyzed by ML Kit; prefers the front or back camera and falls back to any camera.</summary>
public sealed class CameraPoseSource(bool preferFront = true) : PoseSource
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private ProcessCameraProvider? provider;
    private ImageAnalysis? analysis;
    private IExecutorService? executor;
    private MlKitPoseDetector? detector;
    private Task inFlight = Task.CompletedTask;
    private long firstTimestamp = -1, lastTicks = -1;
    private bool mirrored = true;

    public override bool IsMirrored => mirrored;
    /// <summary>The bound preview use case, for the preview handler; null when stopped. Changes on the UI thread.</summary>
    internal Preview? Preview { get; private set; }
    internal event Action? PreviewChanged;

    public override async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (analysis is not null) return;
            try { await StartCoreAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                await StopCoreAsync().ConfigureAwait(false);
                // Java exceptions from CameraX (no camera, camera in use) become a reported, catchable failure.
                var failure = error as InvalidOperationException ?? new InvalidOperationException($"The camera could not be started: {error.Message}", error);
                ReportError(failure);
                throw failure;
            }
        }
        finally { lifecycle.Release(); }
    }

    private async Task StartCoreAsync()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity hosts the camera.");
        if (activity is not ILifecycleOwner owner) throw new InvalidOperationException("The activity is not a lifecycle owner.");
        // ListenableFuture.get blocks until CameraX has enumerated the cameras; keep it off the UI thread.
        var future = ProcessCameraProvider.GetInstance(activity);
        provider = (await Task.Run(() => future.Get()).ConfigureAwait(false)).JavaCast<ProcessCameraProvider>()
            ?? throw new InvalidOperationException("CameraX is unavailable.");
        var (selector, front) = SelectCamera(provider, preferFront) ?? throw new InvalidOperationException("This device has no camera.");
        mirrored = front;
        detector = new MlKitPoseDetector(stream: true);
        executor = Executors.NewSingleThreadExecutor()!;
        firstTimestamp = lastTicks = -1;
        // ML Kit's pose model downsamples anyway; 640×480 keeps conversion and detection cheap.
        var resolution = new ResolutionSelector.Builder()
            .SetResolutionStrategy(new ResolutionStrategy(new Size(640, 480), ResolutionStrategy.FallbackRuleClosestHigherThenLower))!
            .Build()!;
        var preview = new Preview.Builder().Build()!;
        analysis = new ImageAnalysis.Builder()
            .SetResolutionSelector(resolution)!
            .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)!
            .Build()!;
        analysis.SetAnalyzer(executor, new Analyzer(this));
        UseCase[] useCases = [preview, analysis];
        var bound = provider;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            bound.BindToLifecycle(owner, selector, useCases);
            Preview = preview;
            PreviewChanged?.Invoke();
        }).ConfigureAwait(false);
    }

    private static (CameraSelector Selector, bool Front)? SelectCamera(ProcessCameraProvider provider, bool preferFront)
    {
        (int Lens, bool Front)[] order = preferFront
            ? [(CameraSelector.LensFacingFront, true), (CameraSelector.LensFacingBack, false), (CameraSelector.LensFacingExternal, false)]
            : [(CameraSelector.LensFacingBack, false), (CameraSelector.LensFacingExternal, false), (CameraSelector.LensFacingFront, true)];
        foreach (var (lens, front) in order)
        {
            var selector = new CameraSelector.Builder().RequireLensFacing(lens)!.Build()!;
            try { if (provider.HasCamera(selector)) return (selector, front); }
            catch (Java.Lang.Exception) { /* CameraInfoUnavailableException: try the next lens. */ }
            selector.Dispose();
        }
        return null;
    }

    private void Analyze(IImageProxy proxy)
    {
        // CameraX delivers the next image only after this one is closed, so detection runs one at a time.
        inFlight = AnalyzeAsync(proxy);
    }

    private async Task AnalyzeAsync(IImageProxy proxy)
    {
        try
        {
            using var info = proxy.ImageInfo!;
            var rotation = info.RotationDegrees;
            var (width, height) = rotation is 90 or 270 ? (proxy.Height, proxy.Width) : (proxy.Width, proxy.Height);
            if (firstTimestamp < 0) firstTimestamp = info.Timestamp;
            // Sensor timestamps are nanoseconds; guard monotonicity because some HALs repeat them.
            var ticks = Math.Max((info.Timestamp - firstTimestamp) / 100, lastTicks + 1);
            lastTicks = ticks;
            using var media = proxy.Image ?? throw new InvalidOperationException("The camera returned no image.");
            using var input = InputImage.FromMediaImage(media, rotation);
            var start = Stopwatch.GetTimestamp();
            var frame = await detector!.DetectAsync(input, width, height, TimeSpan.FromTicks(ticks)).ConfigureAwait(false);
            Publish(frame, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception error) { ReportError(error); }
        finally { proxy.Close(); }
    }

    public override async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        var (bound, preview, stopped) = (provider, Preview, analysis);
        provider = null; analysis = null;
        if (bound is not null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Preview = null;
                PreviewChanged?.Invoke();
                stopped?.ClearAnalyzer();
                var useCases = new UseCase?[] { preview, stopped }.OfType<UseCase>().ToArray();
                if (useCases.Length > 0) bound.Unbind(useCases);
            }).ConfigureAwait(false);
        }
        // Drain the analyzer thread, then the last detection, before closing the detector it uses.
        if (executor is { } pool)
        {
            pool.Shutdown();
            await Task.Run(() => pool.AwaitTermination(5, TimeUnit.Seconds!)).ConfigureAwait(false);
            pool.Dispose();
            executor = null;
        }
        try { await inFlight.ConfigureAwait(false); } catch { /* Reported by AnalyzeAsync. */ }
        detector?.Dispose();
        detector = null;
        preview?.Dispose(); stopped?.Dispose(); bound?.Dispose();
    }

    public override async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed class Analyzer(CameraPoseSource owner) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        public void Analyze(IImageProxy? image) { if (image is not null) owner.Analyze(image); }
    }
}
#endif

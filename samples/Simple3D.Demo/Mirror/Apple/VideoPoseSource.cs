#if IOS || MACCATALYST
using System.Diagnostics;
using AVFoundation;
using CoreGraphics;
using CoreVideo;
using Foundation;

namespace Simple3D.Demo.Mirror;

public sealed class VideoPoseSource(string path, bool loop = false) : PoseSource, IApplePosePreview
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? cancellation;
    private bool disposed;
    public override bool IsMirrored => false;
    public event Action<CGImage>? ImageReady;

    public override async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Completion.IsCompleted) return;
            if (!File.Exists(path))
            {
                var error = new FileNotFoundException("The pose video could not be found.", path);
                ReportError(error);
                throw error;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            Completion = Task.Run(() => PlayAsync(cancellation.Token));
        }
        finally { lifecycle.Release(); }
    }

    private async Task PlayAsync(CancellationToken token)
    {
        try
        {
            using var detector = new ApplePoseDetector();
            var timestampOffset = TimeSpan.Zero;
            do
            {
                using var url = NSUrl.FromFilename(path);
                using var asset = AVAsset.FromUrl(url);
                var tracks = asset.GetTracks(AVMediaTypes.Video);
                try
                {
                    var track = tracks.FirstOrDefault()
                        ?? throw new InvalidOperationException("The selected file has no video track.");
                    using var reader = new AVAssetReader(asset, out var creationError);
                    if (creationError is not null)
                    {
                        using (creationError)
                            throw new InvalidOperationException(creationError.LocalizedDescription);
                    }
                    using var output = new AVAssetReaderTrackOutput(track,
                        new AVVideoSettingsUncompressed { PixelFormatType = CVPixelFormatType.CV32BGRA });
                    output.AlwaysCopiesSampleData = false;
                    if (!reader.CanAddOutput(output))
                        throw new InvalidOperationException("The video format cannot be decoded.");
                    reader.AddOutput(output);
                    if (!reader.StartReading())
                        throw new InvalidOperationException($"Video decoding failed: {reader.Error?.LocalizedDescription}");
                    using var cancelReader = token.Register(reader.CancelReading);
                    var clock = Stopwatch.StartNew();
                    double? firstPts = null;
                    var lastTimestamp = TimeSpan.Zero;
                    var frameDuration = TimeSpan.FromSeconds(1 / Math.Max(1, track.NominalFrameRate));
                    // AV track transforms use top-left image coordinates; Core Image uses bottom-left.
                    var transform = ToCoreImageTransform(track.PreferredTransform, track.NaturalSize);
                    while (!token.IsCancellationRequested)
                    {
                        using var sample = output.CopyNextSampleBuffer();
                        if (sample is null) break;
                        var pts = sample.PresentationTimeStamp.Seconds;
                        if (!double.IsFinite(pts)) throw new InvalidOperationException("The video contains an invalid timestamp.");
                        firstPts ??= pts;
                        var timestamp = TimeSpan.FromSeconds(Math.Max(0, pts - firstPts.Value));
                        if (timestamp < lastTimestamp)
                            throw new InvalidOperationException("The video timestamps are not ordered.");
                        var delay = timestamp - clock.Elapsed;
                        if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        using var pool = new NSAutoreleasePool();
                        using var pixels = sample.GetImageBuffer() as CVPixelBuffer
                            ?? throw new InvalidOperationException("The video decoder returned no image.");
                        using var image = detector.CreateImage(pixels, transform);
                        var start = Stopwatch.GetTimestamp();
                        var frame = detector.Detect(image, timestampOffset + timestamp);
                        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        token.ThrowIfCancellationRequested();
                        ImageReady?.Invoke(image);
                        Publish(frame, elapsed);
                        lastTimestamp = timestamp;
                    }
                    token.ThrowIfCancellationRequested();
                    if (reader.Status == AVAssetReaderStatus.Failed)
                        throw new InvalidOperationException($"Video decoding failed: {reader.Error?.LocalizedDescription}");
                    if (firstPts is null) throw new InvalidOperationException("The video contains no decodable frames.");
                    timestampOffset += lastTimestamp + frameDuration;
                }
                finally { foreach (var track in tracks) track.Dispose(); }
            } while (loop && !token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            ReportError(error);
            throw;
        }
    }

    private static CGAffineTransform ToCoreImageTransform(CGAffineTransform transform, CGSize size)
    {
        // Translation is immaterial because rendering uses the transformed extent.
        return new CGAffineTransform(transform.A, -transform.B, -transform.C, transform.D, 0, 0);
    }

    public override async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        cancellation?.Cancel();
        try { await Completion.ConfigureAwait(false); }
        catch { /* Playback failures are exposed by Failed and Completion. */ }
        cancellation?.Dispose();
        cancellation = null;
    }

    public override async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
            ImageReady = null;
        }
        finally { lifecycle.Release(); }
    }
}
#endif

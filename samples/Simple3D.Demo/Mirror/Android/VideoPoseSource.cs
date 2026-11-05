#if ANDROID
using System.Diagnostics;
using Android.Graphics;
using Android.Media;
using Simple3D.Shared.Mirror;
using Xamarin.Google.MLKit.Vision.Common;

namespace Simple3D.Demo.Mirror;

/// <summary>Plays an MP4 in real time and analyzes its frames, skipping frames that are already late. With a recording, its landmarks replace detection.</summary>
public sealed class VideoPoseSource(string path, bool loop = false, IReadOnlyList<PoseFrame>? recording = null) : PoseSource, IAndroidPosePreview
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? cancellation;
    private bool disposed;
    public override bool IsMirrored => false;
    public event Action<Bitmap>? ImageReady;

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
            // Stream mode tracks the person across consecutive frames; on the test clip single-image mode found a
            // full body in about half as many frames and often locked onto background clutter.
            using var detector = recording is null ? new MlKitPoseDetector(stream: true) : null;
            var timestampOffset = TimeSpan.Zero;
            do
            {
                using var reader = new FrameReader(path);
                var clock = Stopwatch.StartNew();
                for (var index = 0; index < reader.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var timestamp = reader.Time(index);
                    // Decode every frame (the retriever is fast only for sequential indices) but analyze only frames
                    // that are still current, so playback stays in real time when detection is slower than the video.
                    var bitmap = reader.Read(index);
                    if (index + 1 < reader.Count && reader.Time(index + 1) <= clock.Elapsed)
                    {
                        bitmap.Recycle();
                        bitmap.Dispose();
                        continue;
                    }
                    try
                    {
                        var delay = timestamp - clock.Elapsed;
                        if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
                        var start = Stopwatch.GetTimestamp();
                        var frame = detector is null
                            ? PoseRecording.Nearest(recording!, timestamp) with { Timestamp = timestampOffset + timestamp }
                            : await DetectAsync(detector, bitmap, timestampOffset + timestamp).ConfigureAwait(false);
                        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        token.ThrowIfCancellationRequested();
                        ImageReady?.Invoke(bitmap);
                        Publish(frame, elapsed, detector?.LastFaceMilliseconds ?? 0);
                    }
                    finally
                    {
                        // Preview consumers copied the bitmap synchronously; nothing else references it.
                        bitmap.Recycle();
                        bitmap.Dispose();
                    }
                }
                timestampOffset += reader.Time(reader.Count - 1) + reader.FrameDuration;
            } while (loop && !token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            ReportError(error);
            throw;
        }
    }

    private static async Task<PoseFrame> DetectAsync(MlKitPoseDetector detector, Bitmap bitmap, TimeSpan timestamp)
    {
        using var input = InputImage.FromBitmap(bitmap, 0);
        return await detector.DetectAsync(input, bitmap.Width, bitmap.Height, timestamp).ConfigureAwait(false);
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

    /// <summary>
    /// Random-access frames from MediaMetadataRetriever (upright, ARGB_8888), with presentation times from MediaExtractor.
    /// GetFrameAtIndex measured 16–35 fps on the emulator for this 640×360 H.264 clip, comfortably above real-time analysis,
    /// and lets late frames be skipped without decoding them through MediaCodec.
    /// </summary>
    private sealed class FrameReader : IDisposable
    {
        private readonly MediaMetadataRetriever retriever = new();
        private readonly long[] times;

        public FrameReader(string path)
        {
            try
            {
                retriever.SetDataSource(path);
                times = ReadPresentationTimes(path);
                if (OperatingSystem.IsAndroidVersionAtLeast(28) &&
                    int.TryParse(retriever.ExtractMetadata(MetadataKey.VideoFrameCount), out var count) && count < times.Length)
                    times = times[..count];
                if (times.Length == 0) throw new InvalidOperationException("The video contains no decodable frames.");
                FrameDuration = times.Length > 1 ? TimeSpan.FromTicks((times[^1] - times[0]) * 10 / (times.Length - 1)) : TimeSpan.FromSeconds(1 / 30.0);
            }
            catch
            {
                retriever.Dispose();
                throw;
            }
        }

        public int Count => times.Length;
        public TimeSpan FrameDuration { get; }
        public TimeSpan Time(int index) => TimeSpan.FromTicks((times[index] - times[0]) * 10);

        /// <summary>Decodes one frame into a new bitmap owned by the caller.</summary>
        public Bitmap Read(int index) => (OperatingSystem.IsAndroidVersionAtLeast(28)
                ? retriever.GetFrameAtIndex(index)
                : retriever.GetFrameAtTime(times[index], Option.Closest))
            ?? throw new InvalidOperationException($"Video frame {index} could not be decoded.");

        // Sample times come in decode order; sorted, they enumerate the frames in presentation (index) order.
        private static long[] ReadPresentationTimes(string path)
        {
            using var extractor = new MediaExtractor();
            extractor.SetDataSource(path);
            var track = -1;
            for (var i = 0; i < extractor.TrackCount && track < 0; i++)
            {
                using var format = extractor.GetTrackFormat(i);
                if (format.GetString(MediaFormat.KeyMime)?.StartsWith("video/", StringComparison.Ordinal) == true) track = i;
            }
            if (track < 0) throw new InvalidOperationException("The selected file has no video track.");
            extractor.SelectTrack(track);
            var times = new List<long>();
            for (long time; (time = extractor.SampleTime) >= 0; extractor.Advance()) times.Add(time);
            extractor.Release();
            times.Sort();
            return [.. times];
        }

        public void Dispose()
        {
            retriever.Release();
            retriever.Dispose();
        }
    }
}
#endif

#if (DEBUG || NATIVE_RENDER_PROBE) && (IOS || MACCATALYST || ANDROID)
using Simple3D.Shared.Mirror;

namespace Simple3D.Demo.Mirror;

// A diagnostic host also exercises preview attachment, layout and disposal.
internal sealed class PoseProbePage : ContentPage
{
    static string? VideoPath => Environment.GetEnvironmentVariable("SIMPLE3D_POSE_PROBE")
        ?? Environment.GetEnvironmentVariable("SIMCTL_CHILD_SIMPLE3D_POSE_PROBE");
    static bool CameraRequested => Environment.GetEnvironmentVariable("SIMPLE3D_CAMERA_PROBE") == "1";
    internal static bool IsRequested => !string.IsNullOrWhiteSpace(VideoPath) || CameraRequested;
    readonly CameraPreview preview = new();
    bool started;

    public PoseProbePage() => Content = preview;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (started) return;
        started = true;
        _ = RunAsync();
    }

    async Task RunAsync()
    {
        int exitCode = 1;
        try
        {
            if (CameraRequested) await RunCameraAsync();
            else await RunVideoAsync();
            exitCode = 0;
        }
        catch (Exception error)
        {
            Write($"{(CameraRequested ? "CAMERA" : "POSE")}_PROBE_FAIL {error.GetType().Name}: {error.Message.Replace('\n', ' ')}");
        }
        finally
        {
            preview.Source = null;
            // Detach before disconnecting so no later layout pass measures a handler without a platform view.
            Content = null;
            preview.Handler?.DisconnectHandler();
        }
        Environment.Exit(exitCode);
    }

    async Task RunVideoAsync()
    {
        var path = VideoPath!;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("Probe needs a readable absolute MP4 path.", path);
        await using var source = new VideoPoseSource(path, loop: false);
        preview.Source = source;
        var times = new List<double>();
        var faceTimes = new List<double>();
        int frames = 0, detected = 0, faces = 0;
        TimeSpan previous = TimeSpan.MinValue;
        Exception? failure = null;
        source.Failed += error => failure = error;
        source.DetectionMeasured += value => times.Add(value);
        source.FaceDetectionMeasured += value => faceTimes.Add(value);
        source.FrameAnalyzed += frame =>
        {
            if (frame.Timestamp <= previous || frame.ImageWidth <= 0 || frame.ImageHeight <= 0)
                throw new InvalidDataException("Invalid image dimensions or non-increasing presentation timestamp.");
            previous = frame.Timestamp;
            var points = frame.Landmarks;
            if (points is not null && (points.Length != PoseFrame.JointCount || points.Any(p =>
                !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Confidence) ||
                p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1 || p.Confidence < 0 || p.Confidence > 1)))
                throw new InvalidDataException("Invalid landmark array.");
            frames++;
            if (points is not null && points.All(p => p.Confidence > .3f)) detected++;
            if (frame.Face is { } face)
            {
                faces++;
                float[] angles = [face.Yaw, face.Pitch, face.Roll], units = [face.MouthOpen, face.Smile, face.BlinkLeft, face.BlinkRight];
                if (angles.Any(a => !float.IsFinite(a) || MathF.Abs(a) > MathF.PI) || units.Any(u => !(u is >= 0 and <= 1)) || !(face.Brows is >= -1 and <= 1))
                    throw new InvalidDataException("Invalid face measurements.");
            }
            Write("POSE_FRAME " + PoseRecording.Format(frame));
        };
        await source.StartAsync();
        await source.Completion.WaitAsync(TimeSpan.FromMinutes(4));
        await source.StopAsync();
        if (failure is not null) throw new InvalidOperationException("Source failed.", failure);
        if (frames == 0 || times.Count != frames) throw new InvalidDataException("No analyzed frames or missing detection timings.");
        if (times.Concat(faceTimes).Any(t => !double.IsFinite(t) || t < 0) || faceTimes.Count != frames) throw new InvalidDataException("Invalid detection timings.");
        times.Sort();
        faceTimes.Sort();
        static double P95(List<double> sorted) => sorted[(int)Math.Ceiling(sorted.Count * .95) - 1];
        // The detection time includes the face detection time.
        Write(FormattableString.Invariant($"POSE_PROBE_PASS frames={frames} detected={detected} faces={faces} mean_detect_ms={times.Average():0.###} p95_detect_ms={P95(times):0.###} mean_face_ms={faceTimes.Average():0.###} p95_face_ms={P95(faceTimes):0.###}"));
    }

    async Task RunCameraAsync()
    {
        Write("CAMERA_PROBE_REQUEST permission");
        var permission = await CameraAccess.RequestAsync().WaitAsync(TimeSpan.FromSeconds(25));
        if (permission != CameraAccessStatus.Granted)
        {
            Write($"CAMERA_PROBE_UNAVAILABLE permission={permission}");
            return;
        }
        await using var source = new CameraPoseSource();
        preview.Source = source;
        var first = new TaskCompletionSource<PoseFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.FrameAnalyzed += frame => first.TrySetResult(frame);
        source.Failed += error => first.TrySetException(error);
        try
        {
            await source.StartAsync();
            var frame = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await source.StopAsync();
            Write($"CAMERA_PROBE_PASS w={frame.ImageWidth} h={frame.ImageHeight} mirrored={source.IsMirrored}");
        }
        catch (Exception error)
        {
            await source.StopAsync();
            Write($"CAMERA_PROBE_UNAVAILABLE {error.GetType().Name}: {error.Message.Replace('\n', ' ')}");
        }
    }

    static void Write(string line)
    {
#if ANDROID
        Android.Util.Log.Info("Simple3D", line);
#else
        Console.WriteLine(line);
#endif
    }
}
#endif

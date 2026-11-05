#if ANDROID
using Android.Graphics;
using Android.Runtime;
using Java.Util.Concurrent;
using Simple3D.Shared.Mirror;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Pose;
using Xamarin.Google.MLKit.Vision.Pose.Defaults;
using GmsTask = Android.Gms.Tasks.Task;

namespace Simple3D.Demo.Mirror;

// Bitmaps are borrowed for the duration of the callback and recycled afterwards; consumers must copy them.
internal interface IAndroidPosePreview
{
    event Action<Bitmap>? ImageReady;
}

/// <summary>ML Kit base pose model. Not reentrant: callers analyze one image at a time.</summary>
internal sealed class MlKitPoseDetector(bool stream) : IDisposable
{
    private static readonly int[] LandmarkTypes =
    [
        PoseLandmark.Nose, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder,
        PoseLandmark.LeftElbow, PoseLandmark.RightElbow, PoseLandmark.LeftWrist, PoseLandmark.RightWrist,
        PoseLandmark.LeftHip, PoseLandmark.RightHip, PoseLandmark.LeftKnee, PoseLandmark.RightKnee,
        PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle
    ];

    private const string BuilderClass = "com/google/mlkit/vision/pose/defaults/PoseDetectorOptions$Builder";
    private readonly IPoseDetector detector = PoseDetection.GetClient(CreateOptions(stream));

    private static PoseDetectorOptionsBase CreateOptions(bool stream)
    {
        // Stream mode tracks the person between frames; single-image mode re-detects every decoded frame.
        // The binding's SetDetectorMode targets an erased (I)Ljava/lang/Object; signature that does not exist; call the real one.
        using var builder = new PoseDetectorOptions.Builder();
        var method = JNIEnv.GetMethodID(builder.Class.Handle, "setDetectorMode", "(I)L" + BuilderClass + ";");
        JNIEnv.DeleteLocalRef(JNIEnv.CallObjectMethod(builder.Handle, method,
            new JValue(stream ? PoseDetectorOptionsBase.StreamMode : PoseDetectorOptionsBase.SingleImageMode)));
        return builder.Build();
    }

    /// <summary>Detects in <paramref name="image"/>, whose upright size is <paramref name="width"/>×<paramref name="height"/>.</summary>
    public async Task<PoseFrame> DetectAsync(InputImage image, int width, int height, TimeSpan timestamp)
    {
        var result = await AwaitAsync(detector.Process(image)).ConfigureAwait(false);
        using var pose = result?.JavaCast<Pose>();
        Landmark[]? landmarks = null;
        // ML Kit returns no landmarks without a person, otherwise all 33 in upright-image pixels.
        if (pose is not null && pose.AllPoseLandmarks.Count > 0)
        {
            landmarks = new Landmark[PoseFrame.JointCount];
            for (var i = 0; i < LandmarkTypes.Length; i++)
            {
                using var landmark = pose.GetPoseLandmark(LandmarkTypes[i]);
                if (landmark is null) continue;
                using var position = landmark.Position;
                landmarks[i] = new Landmark(Math.Clamp(position.X / width, 0, 1), Math.Clamp(position.Y / height, 0, 1),
                    Math.Clamp(landmark.InFrameLikelihood, 0, 1));
            }
        }
        return new PoseFrame(timestamp, width, height, landmarks);
    }

    // Listeners run on ML Kit's completion thread rather than the (possibly busy) UI thread.
    private static Task<Java.Lang.Object?> AwaitAsync(GmsTask task)
    {
        var completion = new TaskCompletionSource<Java.Lang.Object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        task.AddOnCompleteListener(DirectExecutor.Instance, new CompletionListener(completion));
        return completion.Task;
    }

    public void Dispose()
    {
        detector.Close();
        detector.Dispose();
    }

    private sealed class CompletionListener(TaskCompletionSource<Java.Lang.Object?> completion)
        : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(GmsTask task)
        {
            if (task.IsCanceled) completion.TrySetCanceled();
            else if (task.IsSuccessful) completion.TrySetResult(task.Result);
            else completion.TrySetException((Exception?)task.Exception ?? new InvalidOperationException("Pose detection failed."));
        }
    }

    private sealed class DirectExecutor : Java.Lang.Object, IExecutor
    {
        public static readonly DirectExecutor Instance = new();
        public void Execute(Java.Lang.IRunnable? command) => command?.Run();
    }
}
#endif

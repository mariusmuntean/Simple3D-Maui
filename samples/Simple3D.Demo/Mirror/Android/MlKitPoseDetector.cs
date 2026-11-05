#if ANDROID
using Android.Graphics;
using Android.Runtime;
using System.Diagnostics;
using System.Numerics;
using Java.Util.Concurrent;
using Simple3D.Shared.Mirror;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Face;
using Xamarin.Google.MLKit.Vision.Pose;
using Xamarin.Google.MLKit.Vision.Pose.Accurate;
using GmsTask = Android.Gms.Tasks.Task;

namespace Simple3D.Demo.Mirror;

// Bitmaps are borrowed for the duration of the callback and recycled afterwards; consumers must copy them.
internal interface IAndroidPosePreview
{
    event Action<Bitmap>? ImageReady;
}

/// <summary>ML Kit accurate pose model. Not reentrant: callers analyze one image at a time.</summary>
internal sealed class MlKitPoseDetector(bool stream) : IDisposable
{
    private static readonly int[] LandmarkTypes =
    [
        PoseLandmark.Nose, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder,
        PoseLandmark.LeftElbow, PoseLandmark.RightElbow, PoseLandmark.LeftWrist, PoseLandmark.RightWrist,
        PoseLandmark.LeftHip, PoseLandmark.RightHip, PoseLandmark.LeftKnee, PoseLandmark.RightKnee,
        PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle
    ];

    private const string BuilderClass = "com/google/mlkit/vision/pose/accurate/AccuratePoseDetectorOptions$Builder";
    private readonly IPoseDetector detector = PoseDetection.GetClient(CreateOptions(stream));
    // Fast mode with contours (mouth, brows) and classification (smile, eyes open). Contours are only computed for the most
    // prominent face, which is also the one nearest the body in practice.
    private readonly IFaceDetector faceDetector = FaceDetection.GetClient(new FaceDetectorOptions.Builder()
        .SetPerformanceMode(FaceDetectorOptions.PerformanceModeFast)
        .SetContourMode(FaceDetectorOptions.ContourModeAll)
        .SetClassificationMode(FaceDetectorOptions.ClassificationModeAll)
        .Build());

    /// <summary>Time from the start of the last <see cref="DetectAsync"/> until its face detection finished.</summary>
    public double LastFaceMilliseconds { get; private set; }

    private static PoseDetectorOptionsBase CreateOptions(bool stream)
    {
        // Stream mode tracks the person between frames; single-image mode re-detects every decoded frame.
        // The accurate model (larger download, slower) keeps the whole body far more reliably than the base model.
        // The binding's SetDetectorMode targets an erased (I)Ljava/lang/Object; signature that does not exist; call the real one.
        using var builder = new AccuratePoseDetectorOptions.Builder();
        var method = JNIEnv.GetMethodID(builder.Class.Handle, "setDetectorMode", "(I)L" + BuilderClass + ";");
        JNIEnv.DeleteLocalRef(JNIEnv.CallObjectMethod(builder.Handle, method,
            new JValue(stream ? PoseDetectorOptionsBase.StreamMode : PoseDetectorOptionsBase.SingleImageMode)));
        // The binding omits build() as well.
        var build = JNIEnv.GetMethodID(builder.Class.Handle, "build", "()Lcom/google/mlkit/vision/pose/accurate/AccuratePoseDetectorOptions;");
        return Java.Lang.Object.GetObject<AccuratePoseDetectorOptions>(JNIEnv.CallObjectMethod(builder.Handle, build), JniHandleOwnership.TransferLocalRef)!;
    }

    /// <summary>Detects in <paramref name="image"/>, whose upright size is <paramref name="width"/>×<paramref name="height"/>.</summary>
    public async Task<PoseFrame> DetectAsync(InputImage image, int width, int height, TimeSpan timestamp)
    {
        // Both detectors run in parallel on ML Kit's own threads.
        var start = Stopwatch.GetTimestamp();
        var poseTask = AwaitAsync(detector.Process(image));
        var faceTask = DetectFacesAsync(image, start);
        var result = await poseTask.ConfigureAwait(false);
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
        using var faces = (await faceTask.ConfigureAwait(false))?.JavaCast<Java.Util.IList>();
        return new PoseFrame(timestamp, width, height, landmarks) { Face = FindFace(faces, landmarks, width, height) };
    }

    private async Task<Java.Lang.Object?> DetectFacesAsync(InputImage image, long start)
    {
        var faces = await AwaitAsync(faceDetector.Process(image)).ConfigureAwait(false);
        LastFaceMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return faces;
    }

    // The face nearest the body's nose (or shoulders), else the largest face.
    private static FaceFrame? FindFace(Java.Util.IList? faces, Landmark[]? body, int width, int height)
    {
        if (faces is null) return null;
        Landmark? anchor = body is null ? null : body[(int)BodyJoint.Nose] is { Confidence: > .1f } nose ? nose
            : new((body[(int)BodyJoint.LeftShoulder].X + body[(int)BodyJoint.RightShoulder].X) / 2,
                (body[(int)BodyJoint.LeftShoulder].Y + body[(int)BodyJoint.RightShoulder].Y) / 2, 1);
        Face? best = null;
        var bestScore = double.PositiveInfinity;
        for (var i = 0; i < faces.Size(); i++)
        {
            var face = faces.Get(i)!.JavaCast<Face>()!;
            using var box = face.BoundingBox;
            var score = anchor is { } a ? Math.Pow((double)box.CenterX() / width - a.X, 2) + Math.Pow((double)box.CenterY() / height - a.Y, 2)
                : -(double)box.Width() * box.Height();
            if (score < bestScore) { best?.Dispose(); best = face; bestScore = score; }
            else face.Dispose();
        }
        using (best) return best is null ? null : Measure(best);
    }

    private static FaceFrame Measure(Face face)
    {
        // Pixels, flipped to Y up as FaceGeometry expects.
        Vector2[] Points(int type)
        {
            using var contour = face.GetContour(type);
            if (contour?.Points is not { } points) return [];
            var result = new Vector2[points.Count];
            for (var i = 0; i < result.Length; i++)
                using (var p = points[i]) result[i] = new(p.X, -p.Y);
            return result;
        }
        static float X(Vector2[] p) => p.Length == 0 ? 0 : p.Average(v => v.X);
        // ML Kit's head Euler angles are in degrees with FaceFrame's signs: X positive looking up, Y positive turning toward
        // the image's right, Z positive rotating counter-clockwise in the image (the top of the head toward the image's left).
        // Checked against Apple Vision on four clips: all three correlate positively.
        float yaw = float.DegreesToRadians(face.HeadEulerAngleY), pitch = float.DegreesToRadians(face.HeadEulerAngleX),
            roll = float.DegreesToRadians(face.HeadEulerAngleZ);
        // FaceGeometry wants image sides, so order each eye and brow pair by X rather than trust ML Kit's names.
        var (eyeA, eyeB) = (Points(FaceContour.LeftEye), Points(FaceContour.RightEye));
        var browA = Points(FaceContour.LeftEyebrowTop).Concat(Points(FaceContour.LeftEyebrowBottom)).ToArray();
        var browB = Points(FaceContour.RightEyebrowTop).Concat(Points(FaceContour.RightEyebrowBottom)).ToArray();
        if (X(eyeA) > X(eyeB)) (eyeA, eyeB, browA, browB) = (eyeB, eyeA, browB, browA);
        var lip = Points(FaceContour.UpperLipTop);
        // Nine points each from corner to corner; the middle three match Vision's central inner-lip points.
        var upper = Points(FaceContour.UpperLipBottom);
        var lower = Points(FaceContour.LowerLipTop);
        static float? Probability(Java.Lang.Float? value) => value is null ? null : Math.Clamp(value.FloatValue(), 0, 1);
        var smile = Probability(face.SmilingProbability);
        // ML Kit's eye-open probabilities are named by image side in an unmirrored image: a clip of a
        // person winking their right eye (Mixkit 11519) gives a low LeftEyeOpenProbability. So "left" is the person's right.
        float? blinkLeft = 1 - Probability(face.RightEyeOpenProbability), blinkRight = 1 - Probability(face.LeftEyeOpenProbability);
        if (lip.Length < 2 || upper.Length < 9 || lower.Length < 9)
            return new(yaw, pitch, roll, 0, smile ?? 0, 0, blinkLeft ?? 0, blinkRight ?? 0);
        var (cornerA, cornerB) = lip[0].X < lip[^1].X ? (lip[0], lip[^1]) : (lip[^1], lip[0]);
        return FaceGeometry.Measure(new(eyeA, eyeB, browA, browB, cornerA, cornerB, upper[3..6], lower[3..6]),
            yaw, pitch, roll, smile, blinkLeft, blinkRight);
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
        faceDetector.Close();
        faceDetector.Dispose();
    }

    private sealed class CompletionListener(TaskCompletionSource<Java.Lang.Object?> completion)
        : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(GmsTask task)
        {
            if (task.IsCanceled) completion.TrySetCanceled();
            else if (task.IsSuccessful) completion.TrySetResult(task.Result);
            else completion.TrySetException((Exception?)task.Exception ?? new InvalidOperationException("ML Kit detection failed."));
        }
    }

    private sealed class DirectExecutor : Java.Lang.Object, IExecutor
    {
        public static readonly DirectExecutor Instance = new();
        public void Execute(Java.Lang.IRunnable? command) => command?.Run();
    }
}
#endif

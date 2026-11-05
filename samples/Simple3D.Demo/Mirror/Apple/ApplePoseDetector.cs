#if IOS || MACCATALYST
using System.Diagnostics;
using System.Numerics;
using CoreGraphics;
using CoreImage;
using CoreVideo;
using Foundation;
using Simple3D.Shared.Mirror;
using Vision;

namespace Simple3D.Demo.Mirror;

// Images are borrowed for the duration of the callback; consumers must retain their own image.
internal interface IApplePosePreview
{
    event Action<CGImage>? ImageReady;
}

internal sealed class ApplePoseDetector : IDisposable
{
    private static readonly VNHumanBodyPoseObservationJointName[] JointNames =
    [
        VNHumanBodyPoseObservationJointName.Nose,
        VNHumanBodyPoseObservationJointName.LeftShoulder, VNHumanBodyPoseObservationJointName.RightShoulder,
        VNHumanBodyPoseObservationJointName.LeftElbow, VNHumanBodyPoseObservationJointName.RightElbow,
        VNHumanBodyPoseObservationJointName.LeftWrist, VNHumanBodyPoseObservationJointName.RightWrist,
        VNHumanBodyPoseObservationJointName.LeftHip, VNHumanBodyPoseObservationJointName.RightHip,
        VNHumanBodyPoseObservationJointName.LeftKnee, VNHumanBodyPoseObservationJointName.RightKnee,
        VNHumanBodyPoseObservationJointName.LeftAnkle, VNHumanBodyPoseObservationJointName.RightAnkle
    ];

    private readonly VNDetectHumanBodyPoseRequest request = new(null);
    // Run first, the rectangles request gives the landmarks request continuous (revision 3) yaw, pitch and roll;
    // on its own the landmarks request reports angles in 45° steps.
    private readonly VNDetectFaceRectanglesRequest faceRectangles = new(null);
    private readonly VNDetectFaceLandmarksRequest faceLandmarks;
    private readonly CIContext imageContext = CIContext.Create();
    private long performStart;

    /// <summary>Time the face requests of the last <see cref="Detect"/> took; they run before the body request.</summary>
    public double LastFaceMilliseconds { get; private set; }

    public ApplePoseDetector()
    {
        // Vision performs the requests in order and calls each completion handler as its request finishes.
        faceLandmarks = new((_, _) => LastFaceMilliseconds = Stopwatch.GetElapsedTime(performStart).TotalMilliseconds)
        {
            // FindFace indexes the lip regions of the 76-point constellation; the default differs between devices.
            Constellation = VNRequestFaceLandmarksConstellation.SeventySixPoints
        };
#if IOS
        // Simulator Vision has no Neural Engine; its CPU implementation also works in headless probes.
#pragma warning disable CA1422
        if (ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR)
        {
            request.UsesCpuOnly = true;
            faceRectangles.UsesCpuOnly = true;
            faceLandmarks.UsesCpuOnly = true;
        }
#pragma warning restore CA1422
#endif
    }

    public CGImage CreateImage(CVPixelBuffer pixels, CGAffineTransform transform)
    {
        using var input = CIImage.FromImageBuffer(pixels);
        using var upright = input.ImageByApplyingTransform(transform);
        return imageContext.CreateCGImage(upright, upright.Extent)
            ?? throw new InvalidOperationException("Could not create the camera image.");
    }

    public PoseFrame Detect(CGImage image, TimeSpan timestamp)
    {
        using var handler = new VNImageRequestHandler(image, new VNImageOptions());
        LastFaceMilliseconds = 0;
        performStart = Stopwatch.GetTimestamp();
        if (!handler.Perform([faceRectangles, faceLandmarks, request], out var error))
        {
            using (error)
                throw new InvalidOperationException($"Body pose detection failed: {error?.LocalizedDescription}");
        }
        error?.Dispose();

        Landmark[]? largest = null;
        double largestArea = -1;
        foreach (var observation in request.Results ?? [])
        {
            using (observation)
            {
                var landmarks = new Landmark[PoseFrame.JointCount];
                double minX = 1, minY = 1, maxX = 0, maxY = 0;
                var found = 0;
                for (var i = 0; i < JointNames.Length; i++)
                {
                    using var point = observation.GetRecognizedPoint(JointNames[i], out var pointError);
                    pointError?.Dispose();
                    if (point is null || point.Confidence <= 0) continue;
                    var x = Math.Clamp((float)point.Location.X, 0, 1);
                    var y = Math.Clamp(1 - (float)point.Location.Y, 0, 1);
                    landmarks[i] = new Landmark(x, y, Math.Clamp(point.Confidence, 0, 1));
                    if (point.Confidence < 0.1f) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    found++;
                }
                var area = (maxX - minX) * (maxY - minY);
                if (found > 0 && area > largestArea)
                {
                    largest = landmarks;
                    largestArea = area;
                }
            }
        }
        var faces = faceLandmarks.Results ?? [];
        var face = FindFace(faces, largest, image.Width, image.Height);
        foreach (var observation in faces) observation.Dispose();
        foreach (var observation in faceRectangles.Results ?? []) observation.Dispose();
        return new PoseFrame(timestamp, (int)image.Width, (int)image.Height, largest) { Face = face };
    }

    // The face nearest the body's nose (or shoulders), else the largest face.
    private static FaceFrame? FindFace(VNFaceObservation[] faces, Landmark[]? body, nint width, nint height)
    {
        Landmark? anchor = body is null ? null : body[(int)BodyJoint.Nose] is { Confidence: > .1f } nose ? nose
            : new((body[(int)BodyJoint.LeftShoulder].X + body[(int)BodyJoint.RightShoulder].X) / 2,
                (body[(int)BodyJoint.LeftShoulder].Y + body[(int)BodyJoint.RightShoulder].Y) / 2, 1);
        VNFaceObservation? best = null;
        var bestScore = double.PositiveInfinity;
        foreach (var face in faces)
        {
            var box = face.BoundingBox;
            var score = anchor is { } a ? Math.Pow(box.GetMidX() - a.X, 2) + Math.Pow(1 - box.GetMidY() - a.Y, 2) : -box.Width * box.Height;
            if (score < bestScore) { bestScore = score; best = face; }
        }
        if (best?.Landmarks is not { } regions) return null;
        // Pixel points (Y up): the face box's normalized points would stretch X against Y when the box is not square in pixels.
        var size = new CGSize(width, height);
        Vector2[] Points(VNFaceLandmarkRegion2D? region) =>
            region?.GetPointsInImage(size).Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray() ?? [];
        var outer = Points(regions.OuterLips);
        var inner = Points(regions.InnerLips);
        // Vision reports pitch positive looking down; FaceFrame's is positive looking up. Yaw and roll already match.
        float yaw = (float)(best.Yaw ?? 0), pitch = -(float)(best.Pitch ?? 0), roll = (float)(best.Roll ?? 0);
        if (outer.Length < 8 || inner.Length < 6) return new(yaw, pitch, roll, 0, 0, 0, 0, 0);
        // Vision's left eye is on the image's left (the person's right). Outer lips run from the image-left corner (0)
        // along the upper lip to the other corner (7); inner lips are three upper, then three lower points.
        return FaceGeometry.Measure(new(Points(regions.LeftEye), Points(regions.RightEye), Points(regions.LeftEyebrow),
            Points(regions.RightEyebrow), outer[0], outer[7], inner[..3], inner[3..6]), yaw, pitch, roll);
    }

    public void Dispose()
    {
        request.Dispose();
        faceRectangles.Dispose();
        faceLandmarks.Dispose();
        imageContext.Dispose();
    }
}
#endif

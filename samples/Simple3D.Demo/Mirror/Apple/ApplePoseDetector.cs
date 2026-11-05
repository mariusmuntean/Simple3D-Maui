#if IOS || MACCATALYST
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
    private readonly CIContext imageContext = CIContext.Create();

    public ApplePoseDetector()
    {
#if IOS
        // Simulator Vision has no Neural Engine; its CPU implementation also works in headless probes.
#pragma warning disable CA1422
        if (ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR)
            request.UsesCpuOnly = true;
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
        if (!handler.Perform([request], out var error))
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
        return new PoseFrame(timestamp, (int)image.Width, (int)image.Height, largest);
    }

    public void Dispose()
    {
        request.Dispose();
        imageContext.Dispose();
    }
}
#endif

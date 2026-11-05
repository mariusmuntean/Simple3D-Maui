#if IOS || MACCATALYST
using System.Diagnostics;
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;

namespace Simple3D.Demo.Mirror;

/// <summary>Front camera frames analyzed by Vision on a serial queue; late frames are dropped rather than queued.</summary>
public sealed class CameraPoseSource : PoseSource
{
    private readonly DispatchQueue _queue = new("dev.simple3d.mirror.camera");
    private AVCaptureSession? _session;
    private Delegate? _delegate;
    private bool _mirrored = true;

    public override bool IsMirrored => _mirrored;
    /// <summary>The running capture session, for the preview layer; null when stopped.</summary>
    internal AVCaptureSession? Session => _session;
    internal event Action? SessionChanged;

    public override Task StartAsync()
    {
        if (_session is not null) return Task.CompletedTask;
        var device = AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Front)
            ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video)
            ?? throw new InvalidOperationException("This device has no camera.");
        _mirrored = device.Position != AVCaptureDevicePosition.Back;
        var input = AVCaptureDeviceInput.FromDevice(device, out var inputError)
            ?? throw new InvalidOperationException($"The camera could not be opened: {inputError?.LocalizedDescription}");
        var session = new AVCaptureSession();
        session.BeginConfiguration();
        // Vision's body model works on small images; 640×480 keeps detection fast and memory low.
        session.SessionPreset = session.CanSetSessionPreset(AVCaptureSession.Preset640x480) ? AVCaptureSession.Preset640x480 : AVCaptureSession.PresetMedium;
        if (!session.CanAddInput(input)) throw new InvalidOperationException("The camera input is not supported.");
        session.AddInput(input);
        var output = new AVCaptureVideoDataOutput
        {
            AlwaysDiscardsLateVideoFrames = true,
            WeakVideoSettings = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA }.Dictionary
        };
        if (!session.CanAddOutput(output)) throw new InvalidOperationException("The camera output is not supported.");
        session.AddOutput(output);
        if (output.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!) is { } connection)
        {
            // Analyze upright, unmirrored portrait images; only the preview is mirrored.
#if IOS
#pragma warning disable CA1422 // VideoOrientation: replacement (rotation angle) needs iOS 17; this sample supports iOS 15.
            if (connection.SupportsVideoOrientation) connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;
#pragma warning restore CA1422
#endif
            if (connection.SupportsVideoMirroring) { connection.AutomaticallyAdjustsVideoMirroring = false; connection.VideoMirrored = false; }
        }
        _delegate = new Delegate(this);
        output.SetSampleBufferDelegate(_delegate, _queue);
        session.CommitConfiguration();
        _session = session;
        // startRunning blocks; never call it on the UI thread.
        return Task.Run(() =>
        {
            session.StartRunning();
            MainThread.BeginInvokeOnMainThread(() => SessionChanged?.Invoke());
        });
    }

    public override Task StopAsync()
    {
        var session = _session;
        if (session is null) return Task.CompletedTask;
        _session = null;
        SessionChanged?.Invoke();
        return Task.Run(() =>
        {
            session.StopRunning();
            // Drain the analysis queue before releasing the detector it uses.
            _queue.DispatchSync(() => { });
            foreach (var output in session.Outputs) session.RemoveOutput(output);
            foreach (var input in session.Inputs) session.RemoveInput(input);
            session.Dispose();
            _delegate?.Dispose();
            _delegate = null;
        });
    }

    public override async ValueTask DisposeAsync() => await StopAsync();

    private sealed class Delegate(CameraPoseSource owner) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private readonly ApplePoseDetector _detector = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            try
            {
                using var pool = new NSAutoreleasePool();
                using var pixels = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
                if (pixels is null) return;
                using var image = _detector.CreateImage(pixels, CGAffineTransform.MakeIdentity());
                var start = Stopwatch.GetTimestamp();
                var frame = _detector.Detect(image, _clock.Elapsed);
                owner.Publish(frame, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            catch (Exception error) { owner.ReportError(error); }
            finally { sampleBuffer.Dispose(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _detector.Dispose();
            base.Dispose(disposing);
        }
    }
}
#endif

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
public sealed class CameraPoseSource(bool preferFront = true) : PoseSource
{
    private readonly DispatchQueue _queue = new("dev.simple3d.mirror.camera");
    private AVCaptureSession? _session;
    private Delegate? _delegate;
    private bool _mirrored = true;
    private Task _running = Task.CompletedTask;

    public override bool IsMirrored => _mirrored;
    private AVCaptureDevice? _device;
    // Beyond a few times the image gets too soft for pose detection.
    public override float MaximumZoom => _device is { } device ? (float)Math.Min(device.ActiveFormat.VideoMaxZoomFactor, 6) : 1;
    public override float Zoom
    {
        get => _device is { } device ? (float)device.VideoZoomFactor : 1;
        set
        {
            if (_device is not { } device || !device.LockForConfiguration(out _)) return;
            device.VideoZoomFactor = Math.Clamp(value, 1, MaximumZoom);
            device.UnlockForConfiguration();
        }
    }
    /// <summary>The running capture session, for the preview layer; null when stopped.</summary>
    internal AVCaptureSession? Session => _session;
    internal event Action? SessionChanged;

    public override Task StartAsync()
    {
        if (_session is not null) return Task.CompletedTask;
        var preferred = preferFront ? AVCaptureDevicePosition.Front : AVCaptureDevicePosition.Back;
        var device = AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video, preferred)
            ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video)
            ?? throw new InvalidOperationException("This device has no camera.");
        // Only the front camera's preview is mirrored; external or back cameras show the scene as is.
        _mirrored = device.Position == AVCaptureDevicePosition.Front;
        var input = AVCaptureDeviceInput.FromDevice(device, out var inputError)
            ?? throw new InvalidOperationException($"The camera could not be opened: {inputError?.LocalizedDescription}");
        var session = new AVCaptureSession();
        try { Configure(session, input); }
        catch
        {
            // Release a half-configured session; nothing else holds it yet.
            session.Dispose(); input.Dispose(); _delegate?.Dispose(); _delegate = null;
            throw;
        }
        _session = session;
        _device = device;
        // startRunning blocks; never call it on the UI thread. Stop waits for this task.
        return _running = Task.Run(() =>
        {
            session.StartRunning();
            MainThread.BeginInvokeOnMainThread(() => SessionChanged?.Invoke());
        });
    }

    private void Configure(AVCaptureSession session, AVCaptureDeviceInput input)
    {
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
    }

    public override Task StopAsync()
    {
        var session = _session;
        if (session is null) return Task.CompletedTask;
        _session = null;
        _device = null;
        SessionChanged?.Invoke();
        var running = _running;
        return Task.Run(async () =>
        {
            try { await running.ConfigureAwait(false); } catch { }
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
                owner.Publish(frame, Stopwatch.GetElapsedTime(start).TotalMilliseconds, _detector.LastFaceMilliseconds);
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

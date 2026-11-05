#if IOS || MACCATALYST
using AVFoundation;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Simple3D.Demo.Mirror;

/// <summary>Shows a camera source's capture session, or the frames a video source analyzed.</summary>
public sealed class CameraPreviewHandler() : ViewHandler<CameraPreview, CameraPreviewHandler.PreviewView>(Mapper)
{
    public static readonly IPropertyMapper<CameraPreview, CameraPreviewHandler> Mapper =
        new PropertyMapper<CameraPreview, CameraPreviewHandler>(ViewMapper) { [nameof(CameraPreview.Source)] = (handler, _) => handler.Attach() };

    protected override PreviewView CreatePlatformView() => new();

    protected override void DisconnectHandler(PreviewView platformView)
    {
        platformView.Detach();
        base.DisconnectHandler(platformView);
    }

    private void Attach() => PlatformView.Attach(VirtualView.Source);

    public sealed class PreviewView : UIView
    {
        private AVCaptureVideoPreviewLayer? _cameraLayer;
        private readonly CALayer _imageLayer = new() { ContentsGravity = CALayer.GravityResizeAspectFill, MasksToBounds = true };
        private CameraPoseSource? _camera;
        private IApplePosePreview? _video;

        public PreviewView()
        {
            ClipsToBounds = true;
            BackgroundColor = UIColor.Black;
            Layer.AddSublayer(_imageLayer);
        }

        internal void Attach(object? source)
        {
            Detach();
            if (source is CameraPoseSource camera)
            {
                _camera = camera;
                camera.SessionChanged += UpdateSession;
                UpdateSession();
            }
            else if (source is IApplePosePreview video)
            {
                _video = video;
                video.ImageReady += ShowImage;
            }
        }

        internal void Detach()
        {
            if (_camera is not null) _camera.SessionChanged -= UpdateSession;
            if (_video is not null) _video.ImageReady -= ShowImage;
            _camera = null; _video = null;
            _cameraLayer?.RemoveFromSuperLayer();
            _cameraLayer?.Dispose();
            _cameraLayer = null;
            var last = _imageLayer.Contents;
            _imageLayer.Contents = null;
            last?.Dispose();
        }

        private void UpdateSession()
        {
            _cameraLayer?.RemoveFromSuperLayer();
            _cameraLayer?.Dispose();
            _cameraLayer = null;
            if (_camera?.Session is not { } session) return;
            // The preview layer mirrors the front camera automatically, which is what people expect to see.
            _cameraLayer = new AVCaptureVideoPreviewLayer(session) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill, Frame = Bounds };
            Layer.InsertSublayer(_cameraLayer, 0);
        }

        // Called on the analysis thread with a borrowed image: copy the reference before hopping threads.
        private void ShowImage(CGImage image)
        {
            var copy = image.Clone();
            BeginInvokeOnMainThread(() =>
            {
                var previous = _imageLayer.Contents;
                _imageLayer.Contents = _video is null ? null : copy;
                previous?.Dispose();
                if (_video is null) copy?.Dispose();
            });
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            _imageLayer.Frame = Bounds;
            if (_cameraLayer is not null) _cameraLayer.Frame = Bounds;
            CATransaction.Commit();
        }
    }
}
#endif

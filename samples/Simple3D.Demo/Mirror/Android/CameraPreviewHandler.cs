#if ANDROID
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.Camera.View;
using Microsoft.Maui.Handlers;

namespace Simple3D.Demo.Mirror;

/// <summary>Shows a camera source through a CameraX PreviewView, or the frames a video source analyzed.</summary>
public sealed class CameraPreviewHandler() : ViewHandler<CameraPreview, FrameLayout>(Mapper)
{
    public static readonly IPropertyMapper<CameraPreview, CameraPreviewHandler> Mapper =
        new PropertyMapper<CameraPreview, CameraPreviewHandler>(ViewMapper) { [nameof(CameraPreview.Source)] = (handler, _) => handler.Attach() };

    private CameraPoseSource? camera;
    private PreviewView? cameraView;
    private IAndroidPosePreview? video;
    private BitmapSink? sink;

    protected override FrameLayout CreatePlatformView()
    {
        var layout = new FrameLayout(Context) { ClipToOutline = true };
        layout.SetBackgroundColor(Android.Graphics.Color.Black);
        layout.LayoutChange += (_, _) => UpdateRotation();
        return layout;
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        Detach();
        base.DisconnectHandler(platformView);
    }

    private void Attach()
    {
        Detach();
        var source = VirtualView?.Source;
        if (source is CameraPoseSource cameraSource)
        {
            // PreviewView mirrors the front camera like a mirror; the analyzed images stay unmirrored.
            cameraView = new PreviewView(Context);
            cameraView.SetScaleType(PreviewView.ScaleType.FillCenter!);
            // TextureView-backed preview composes correctly inside MAUI's clipped, transformed layouts.
            cameraView.SetImplementationMode(PreviewView.ImplementationMode.Compatible!);
            PlatformView.AddView(cameraView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            camera = cameraSource;
            camera.PreviewChanged += UpdatePreview;
            UpdatePreview();
        }
        else if (source is IAndroidPosePreview videoSource)
        {
            var imageView = new ImageView(Context);
            imageView.SetScaleType(ImageView.ScaleType.CenterCrop!);
            PlatformView.AddView(imageView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            sink = new BitmapSink(imageView);
            video = videoSource;
            video.ImageReady += sink.Show;
        }
    }

    private void Detach()
    {
        if (camera is not null)
        {
            camera.PreviewChanged -= UpdatePreview;
            if (camera.Preview is { } preview) preview.SurfaceProvider = null;
        }
        if (video is not null && sink is not null) video.ImageReady -= sink.Show;
        sink?.Close();
        camera = null; video = null; sink = null;
        PlatformView?.RemoveAllViews();
        cameraView?.Dispose();
        cameraView = null;
    }

    // Raised on the UI thread when the source binds or unbinds its preview use case.
    private void UpdatePreview()
    {
        if (cameraView is not null && camera?.Preview is { } preview) preview.SurfaceProvider = cameraView.SurfaceProvider;
        UpdateRotation();
    }

    private void UpdateRotation()
    {
        // The activity handles rotation itself, so bound use cases survive configuration changes.
        if (PlatformView.Display is { } display) camera?.UpdateRotation((int)display.Rotation);
    }

    /// <summary>Copies borrowed analysis bitmaps and recycles each one only after the next has replaced it on screen.</summary>
    private sealed class BitmapSink(ImageView view)
    {
        private Bitmap? shown, retired;
        private bool closed;

        // Analysis thread: the source recycles its bitmap after this returns.
        public void Show(Bitmap borrowed)
        {
            if (Volatile.Read(ref closed) || borrowed.IsRecycled) return;
            var copy = borrowed.Copy(Bitmap.Config.Argb8888!, false);
            if (copy is null) return;
            if (!view.Post(() => Swap(copy))) Drop(copy);
        }

        private void Swap(Bitmap copy)
        {
            if (closed) { Drop(copy); return; }
            view.SetImageBitmap(copy);
            // The render thread may still draw the frame that was replaced just now; recycle one frame later.
            if (retired is not null) Drop(retired);
            retired = shown;
            shown = copy;
        }

        // UI thread. The last two bitmaps may still be referenced by a pending frame, so the GC releases them.
        public void Close()
        {
            Volatile.Write(ref closed, true);
            view.SetImageDrawable(null);
            shown?.Dispose(); retired?.Dispose();
            shown = retired = null;
        }

        private static void Drop(Bitmap bitmap)
        {
            bitmap.Recycle();
            bitmap.Dispose();
        }
    }
}
#endif

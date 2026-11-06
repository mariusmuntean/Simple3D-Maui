using Android.App;
using Android.Content.PM;

namespace Simple3D.Demo;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnPostCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnPostCreate(savedInstanceState);
        // Android may default a window to 60 Hz on high-refresh screens: ask for the fastest mode at the current resolution.
#pragma warning disable CA1422 // WindowManager.DefaultDisplay: the replacement, Context.Display, needs API 30.
        var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? Display : WindowManager?.DefaultDisplay;
#pragma warning restore CA1422
        if (!OperatingSystem.IsAndroidVersionAtLeast(23) || display?.GetMode() is not { } current || Window?.Attributes is not { } attributes) return;
        var fastest = display.GetSupportedModes()?
            .Where(m => m.PhysicalWidth == current.PhysicalWidth && m.PhysicalHeight == current.PhysicalHeight)
            .MaxBy(m => m.RefreshRate);
        if (fastest is null || fastest.ModeId == current.ModeId) return;
        attributes.PreferredDisplayModeId = fastest.ModeId;
        Window.Attributes = attributes;
    }

#if DEBUG || NATIVE_RENDER_PROBE
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        if (Intent?.GetStringExtra("simple3d_gpu_probe") == "1")
            Environment.SetEnvironmentVariable("SIMPLE3D_GPU_PROBE", "1");
        if (Intent?.GetStringExtra("simple3d_gpu_view_probe") == "1")
            Environment.SetEnvironmentVariable("SIMPLE3D_GPU_VIEW_PROBE", "1");
        if (Intent?.GetStringExtra("simple3d_label_sync_probe") is { } labelSync)
            Environment.SetEnvironmentVariable("SIMPLE3D_LABEL_SYNC_PROBE", labelSync);
        if (Intent?.GetStringExtra("simple3d_pose_probe") is { } poseVideo)
            Environment.SetEnvironmentVariable("SIMPLE3D_POSE_PROBE", poseVideo);
        if (Intent?.GetStringExtra("simple3d_mirror_video") is { } mirrorVideo)
            Environment.SetEnvironmentVariable("SIMPLE3D_MIRROR_VIDEO", mirrorVideo);
        if (Intent?.GetStringExtra("simple3d_mirror_landmarks") is { } mirrorLandmarks)
            Environment.SetEnvironmentVariable("SIMPLE3D_MIRROR_LANDMARKS", mirrorLandmarks);
        if (Intent?.GetStringExtra("simple3d_gallery_scene") is { } galleryScene)
            Environment.SetEnvironmentVariable("SIMPLE3D_GALLERY_SCENE", galleryScene);
        if (Intent?.GetStringExtra("simple3d_gallery_show") is { } galleryShow)
            Environment.SetEnvironmentVariable("SIMPLE3D_GALLERY_SHOW", galleryShow);
        if (Intent?.GetStringExtra("simple3d_camera_probe") is { } cameraProbe)
            Environment.SetEnvironmentVariable("SIMPLE3D_CAMERA_PROBE", cameraProbe);
        base.OnCreate(savedInstanceState);
    }
#endif
}

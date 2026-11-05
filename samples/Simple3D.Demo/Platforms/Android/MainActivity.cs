using Android.App;
using Android.Content.PM;

namespace Simple3D.Demo;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
#if DEBUG || NATIVE_RENDER_PROBE
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        if (Intent?.GetStringExtra("simple3d_gpu_probe") == "1")
            Environment.SetEnvironmentVariable("SIMPLE3D_GPU_PROBE", "1");
        if (Intent?.GetStringExtra("simple3d_gpu_view_probe") == "1")
            Environment.SetEnvironmentVariable("SIMPLE3D_GPU_VIEW_PROBE", "1");
        if (Intent?.GetStringExtra("simple3d_label_sync_probe") is { } labelSync)
            Environment.SetEnvironmentVariable("SIMPLE3D_LABEL_SYNC_PROBE", labelSync);
        base.OnCreate(savedInstanceState);
    }
#endif
}

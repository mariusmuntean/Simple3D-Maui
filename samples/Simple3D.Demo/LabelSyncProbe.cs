#if (DEBUG || NATIVE_RENDER_PROBE) && (IOS || MACCATALYST || ANDROID)
using System.Numerics;
using Simple3D.Core;
using Simple3D.Maui;
namespace Simple3D.Demo;

// Opt-in visual check that labels stay on the GPU frame they belong to. A magenta marker sphere
// and a white label share one anchor while the camera orbits continuously; screenshots taken from
// outside the app (scripts/check-label-sync.py) must show a constant label-to-marker offset.
// SIMPLE3D_LABEL_SYNC_PROBE=timer orbits from a timer; =frame orbits from FrameSubmitted.
internal sealed class LabelSyncProbePage : ContentPage
{
    internal static string? Mode => Environment.GetEnvironmentVariable("SIMPLE3D_LABEL_SYNC_PROBE");
    private readonly SceneView _view;

    internal LabelSyncProbePage()
    {
        BackgroundColor = Colors.Black;
        var anchor = new Vector3(1.1f, .5f, .2f);
        var scene = new Scene().Add(Shape.Sphere(0xFFFF00FF).WithMaterial(new Material(0xFFFF00FF, lit: false)).Scaled(.12f).At(anchor.X, anchor.Y, anchor.Z));
        scene.AddLabel(new WorldLabel("XX", anchor, 0xFFFFFFFF));
        _view = new SceneView { RenderingMode = RenderingMode.Hardware, IsInteractive = false, SceneBackgroundColor = Colors.Black, Scene = scene, Camera = new Camera(4, 0, .3f) };
        Content = _view;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // How far the label moves per GPU frame (in screen pixels): the size of a one-frame desync.
        Vector2? last = null; double moved = 0; var frames = 0;
        _view.DebugLabelTrace = (gpu, labels, width, height) =>
        {
            if (!gpu) return;
            if (labels.Count == 0) { last = null; return; }
            var scale = _view.Width > 0 ? (float)(width / _view.Width) : 1; // render px per dp
            var p = labels[0].Position / scale; // dp
            if (last is { } l) { moved += Vector2.Distance(p, l); frames++; }
            last = p;
        };
        if (Mode == "frame") { _view.FrameSubmitted += (_, _) => _view.Camera.Orbit(.03f, 0); _view.Refresh(); }
        else Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () => { _view.Camera.Orbit(.03f, 0); return true; });
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            var density = DeviceDisplay.Current.MainDisplayInfo.Density;
            Console.WriteLine($"LABEL_SYNC_READY backend={_view.ActiveRenderingBackend} motion_px_per_frame={(frames > 0 ? moved / frames * density : 0):F1} {_view.RenderingDiagnostic}");
            return true;
        });
    }
}
#endif

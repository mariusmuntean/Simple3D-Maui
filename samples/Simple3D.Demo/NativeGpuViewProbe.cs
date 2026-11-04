#if (DEBUG || NATIVE_RENDER_PROBE) && (IOS || MACCATALYST || ANDROID)
using System.Numerics;
using Simple3D.Core;
using Simple3D.Maui;
#if IOS || MACCATALYST
using Metal;
#endif
namespace Simple3D.Demo;

// Opt-in diagnostic that hosts a real SceneView, unlike NativeGpuProbe which drives the renderers
// offscreen. It covers native view creation and composition under the Skia layer, scene-content
// errors that must not disable hardware, mode switching, and handler disconnect/reattach.
// Backgrounding and physical-device performance still need manual device checks.
internal sealed class NativeGpuViewProbePage : ContentPage
{
    internal static bool IsRequested => Environment.GetEnvironmentVariable("SIMPLE3D_GPU_VIEW_PROBE") == "1";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);
    private readonly Shape _box = Shape.Box();
    private readonly SceneView _view;
    private string? _failure;
    private bool _started;

    internal NativeGpuViewProbePage()
    {
        // Small enough that no screen density pushes the render size past MaximumRenderDimension,
        // so frames without a selection must take the direct (single-pass) path.
        _view = new SceneView { RenderingMode = RenderingMode.Hardware, IsInteractive = false, WidthRequest = 320, HeightRequest = 320,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _view.Scene = new Scene().Add(_box);
        _view.RenderingFailed += OnProbeFailure;
        Content = _view;
    }

    private void OnProbeFailure(object? sender, RenderingFailureEventArgs e) => _failure ??= "RenderingFailed: " + e.Message;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        try
        {
            await WaitFor("first hardware frame", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);
            var hardwareName = _view.RenderingDiagnostic;
            Log("backend " + hardwareName);
            if (!ReferenceEquals(_view.PickAt(_view.Width / 2, _view.Height / 2, _view.Width, _view.Height), _box))
                throw new InvalidOperationException("Hardware pick at the view center did not hit the box.");

            await ProbeRenderPathAndOverlay();
            await ProbeTransientFailure();

            // Scene content errors are reported but must keep hardware rendering available.
            var overBudget = new Scene();
            for (var i = 0; i <= DepthRenderer.MaximumTriangles / _box.TriangleCount; i++) overBudget.Add(Shape.Box());
            var valid = _view.Scene;
            _view.Scene = overBudget;
            await WaitFor("over-budget diagnostic", () => _view.RenderingDiagnostic?.Contains("budget", StringComparison.OrdinalIgnoreCase) == true);
            if (_view.ActiveRenderingBackend != RenderingBackend.Hardware) throw new InvalidOperationException("Scene error changed the backend.");
            _view.Scene = valid;
            await WaitFor("recovery after scene error", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware && _view.RenderingDiagnostic == hardwareName);

            _view.RenderingMode = RenderingMode.Software;
            await WaitFor("software mode", () => _view.ActiveRenderingBackend == RenderingBackend.Software);
            _view.RenderingMode = RenderingMode.Hardware;
            await WaitFor("hardware after mode switch", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);

            // The gallery's renderer picker: Auto -> Software -> Auto must not crash or lose hardware.
            _view.RenderingMode = RenderingMode.Auto;
            await WaitFor("auto mode", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);
            _view.RenderingMode = RenderingMode.Software;
            await WaitFor("software from auto", () => _view.ActiveRenderingBackend == RenderingBackend.Software);
            _view.RenderingMode = RenderingMode.Auto;
            await WaitFor("auto after software", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);
            _view.RenderingMode = RenderingMode.Hardware;
            await WaitFor("hardware after auto", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);

            Content = new Label { Text = "Detached" };
            _view.Handler?.DisconnectHandler();
            await WaitFor("disconnect", () => _view.ActiveRenderingBackend == RenderingBackend.None);
            Content = _view;
            await WaitFor("hardware after reattach", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);

            await ProbePermanentFailureFallsBack();
            Log("NATIVE_GPU_VIEW_PROBE_PASS");
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            Log($"NATIVE_GPU_VIEW_PROBE_FAIL {error.Message} (backend={_view.ActiveRenderingBackend}, diagnostic={_view.RenderingDiagnostic})");
            Environment.Exit(1);
        }
    }

    // Frames without a visible selection skip the offscreen composite; the overlay is only
    // composited and repainted when the scene has labels, at most about once per GPU frame.
    private async Task ProbeRenderPathAndOverlay()
    {
        await NextFrames(2);
        if (LastFrameDirect() is not true) throw new InvalidOperationException("Frames without a visible selection must render directly to the drawable/window surface.");
        Log("ok direct path without selection");
        _view.SelectAt(_view.Width / 2, _view.Height / 2, _view.Width, _view.Height);
        if (!ReferenceEquals(_view.SelectedShape, _box)) throw new InvalidOperationException("Selecting the box at the view center failed.");
        await NextFrames(2);
        if (LastFrameDirect() is not false) throw new InvalidOperationException("A visible selection must use the outline composite.");
        Log("ok composite path with selection");
        _view.ClearSelection();
        await NextFrames(2);

        if (_view.DebugOverlayVisible) throw new InvalidOperationException("Overlay must be hidden in hardware mode when the scene has no labels.");
        var paints = _view.DebugOverlayPaints;
        var frames = _view.DebugHardwareFrames;
        for (var i = 0; i < 20; i++) { _view.Camera.Orbit(.02f, 0); await Task.Delay(20); }
        await NextFrames(1);
        if (_view.DebugOverlayPaints != paints) throw new InvalidOperationException($"Hidden overlay repainted {_view.DebugOverlayPaints - paints} times.");
        Log($"ok no overlay paints without labels ({_view.DebugHardwareFrames - frames} GPU frames)");

        _view.Scene.AddLabel(new WorldLabel("probe", Vector3.Zero));
        await WaitFor("overlay shown for labels", () => _view.DebugOverlayVisible);
        await NextFrames(2);
        paints = _view.DebugOverlayPaints; frames = _view.DebugHardwareFrames;
        for (var i = 0; i < 20; i++) { _view.Camera.Orbit(.02f, 0); await Task.Delay(20); }
        await NextFrames(2);
        var paintDelta = _view.DebugOverlayPaints - paints; var frameDelta = _view.DebugHardwareFrames - frames;
        if (paintDelta < 1 || paintDelta > frameDelta + 2) throw new InvalidOperationException($"Overlay painted {paintDelta} times for {frameDelta} GPU frames.");
        Log($"ok overlay paints {paintDelta} for {frameDelta} GPU frames");
        _view.Scene = new Scene().Add(_box);
        await WaitFor("overlay hidden after labels removed", () => !_view.DebugOverlayVisible);
    }

    private bool? LastFrameDirect() => _view.DebugNativeView switch
    {
#if IOS || MACCATALYST
        AppleGpuView apple => apple.Renderer?.LastFrameDirect,
#else
        AndroidGpuView android => android.Renderer?.LastFrameDirect,
#endif
        _ => null
    };

    // Temporary GPU errors (iOS background submission, Android context loss) must not disable hardware.
    private async Task ProbeTransientFailure()
    {
        var frames = _view.DebugHardwareFrames;
#if IOS || MACCATALYST
        if (_view.DebugNativeView is not AppleGpuView apple) throw new InvalidOperationException("No Metal view.");
        apple.DebugSimulateCommandFailure(MTLCommandBufferError.NotPermitted);
        await WaitFor("frame after background-submit error", () => _view.DebugHardwareFrames > frames);
        Log("ok background-submit error kept hardware");
#else
        if (_view.DebugNativeView is not AndroidGpuView android) throw new InvalidOperationException("No GLES view.");
        var recoveries = android.Recoveries;
        android.DebugLoseContext();
        await WaitFor("frame after EGL context loss", () => android.Recoveries > recoveries && _view.DebugHardwareFrames > frames && android.Renderer is not null);
        Log("ok recovered from EGL context loss");
#endif
        if (_view.ActiveRenderingBackend != RenderingBackend.Hardware) throw new InvalidOperationException("Transient failure changed the backend.");
    }

    // A real device failure in Auto still falls back to software (and is reported once).
    private async Task ProbePermanentFailureFallsBack()
    {
        _view.RenderingMode = RenderingMode.Auto;
        await WaitFor("auto before permanent failure", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);
        string? reported = null;
        void OnFailed(object? sender, RenderingFailureEventArgs e) => reported = e.Message;
        _view.RenderingFailed -= OnProbeFailure; _view.RenderingFailed += OnFailed;
#if IOS || MACCATALYST
        (_view.DebugNativeView as AppleGpuView)?.DebugSimulateCommandFailure(MTLCommandBufferError.OutOfMemory);
#else
        // Exhaust recoveries: repeated context loss within a short window is treated as a device failure.
        for (var i = 0; i < 4 && _view.DebugNativeView is AndroidGpuView android; i++)
        {
            android.DebugLoseContext();
            await Task.Delay(200);
        }
#endif
        await WaitFor("software fallback after permanent failure", () => _view.ActiveRenderingBackend == RenderingBackend.Software && reported is not null);
        _view.RenderingFailed -= OnFailed; _view.RenderingFailed += OnProbeFailure;
        Log("ok permanent failure fell back to software: " + reported);
    }

    private async Task NextFrames(int count)
    {
        var target = _view.DebugHardwareFrames + count;
        _view.Refresh();
        var started = DateTime.UtcNow;
        while (_view.DebugHardwareFrames < target && DateTime.UtcNow - started < TimeSpan.FromSeconds(5))
        { await Task.Delay(16); if (_view.DebugHardwareFrames < target) _view.Refresh(); }
    }

    private async Task WaitFor(string step, Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (_failure is not null) throw new InvalidOperationException($"{step}: {_failure}");
            if (DateTime.UtcNow - started > StepTimeout) throw new TimeoutException($"{step} timed out after {StepTimeout.TotalSeconds:F0}s.");
            await Task.Delay(50);
        }
        if (_failure is not null) throw new InvalidOperationException($"{step}: {_failure}");
        Log("ok " + step);
    }

    private static void Log(string message) => Console.WriteLine("GPU_VIEW_PROBE: " + message);
}
#endif

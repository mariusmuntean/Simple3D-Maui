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
            await ProbeLabelSync();
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

    // What is on screen after each main run-loop turn (one Core Animation commit / one Android
    // traversal) is the last GPU frame and the last overlay paint so far. Their labels must agree,
    // for an off-centre label whose projection moves with the camera. Measured twice: camera
    // animated from a timer, and camera advanced from FrameSubmitted (inside the native draw).
    private async Task ProbeLabelSync()
    {
        var anchor = new Vector3(.5f, .5f, .5f); // Box corner: moves on screen when orbiting.
        _view.Scene = new Scene().Add(_box);
        _view.Scene.AddLabel(new WorldLabel("corner", anchor));
        await WaitFor("overlay shown for sync probe", () => _view.DebugOverlayVisible);
        await NextFrames(3);
        foreach (var fromFrameEvent in new[] { false, true })
        {
            Vector2? gpu = null, overlay = null, shown = null;
            var turns = 0; var compared = 0; var mismatches = 0; var worst = 0f;
            var order = new System.Text.StringBuilder(); var orders = new Dictionary<string, int>();
            static Vector2? First(IReadOnlyList<ProjectedLabel> labels, int w, int h) =>
                labels.Count == 0 ? null : new Vector2(labels[0].Position.X / w, labels[0].Position.Y / h);
            _view.DebugLabelTrace = (isGpu, labels, w, h) =>
            {
                if (isGpu) gpu = First(labels, w, h); else overlay = First(labels, w, h);
                order.Append(isGpu ? 'G' : 'O');
            };
            using var turnEnd = new MainLoopTurns(() =>
            {
                turns++;
                if (order.Length > 0) { var key = order.ToString(); orders[key] = orders.GetValueOrDefault(key) + 1; order.Clear(); }
                // Apple: called after the commit, which shows the last GPU frame and overlay so far.
                // Android: called before each traversal draws. The previous traversal showed the
                // GPU frame latched when it started, with the overlay it painted.
                var displayed = MainLoopTurns.BeforeDraw ? shown : gpu;
                shown = gpu;
                if (displayed is not { } g || overlay is not { } o) return;
                compared++;
                var error = Vector2.Distance(g, o);
                worst = Math.Max(worst, error);
                if (error > .002f) mismatches++; // > ~0.6 px in the 320 dp probe view.
            });
            void Advance(object? sender, EventArgs e) => _view.Camera.Orbit(.03f, 0);
            if (fromFrameEvent) _view.FrameSubmitted += Advance;
            try
            {
                for (var i = 0; i < 60; i++)
                {
                    if (!fromFrameEvent) _view.Camera.Orbit(.03f, 0); else _view.Refresh();
                    await Task.Delay(16);
                }
                await NextFrames(2);
            }
            finally { if (fromFrameEvent) _view.FrameSubmitted -= Advance; _view.DebugLabelTrace = null; }
            var pattern = string.Join(", ", orders.OrderByDescending(p => p.Value).Select(p => $"{p.Key}x{p.Value}"));
            var name = fromFrameEvent ? "camera advanced from FrameSubmitted" : "camera animated from a timer";
            Log($"label sync, {name}: {mismatches}/{compared} turns mismatched (worst {worst:F4} of view), per-turn draw order {pattern}");
            if (compared < 10) throw new InvalidOperationException($"Label sync ({name}) compared only {compared} turns.");
            if (mismatches > 0) throw new InvalidOperationException($"Labels out of sync with the GPU frame ({name}): {mismatches}/{compared} turns, worst {worst:F4}.");
        }
        _view.Scene = new Scene().Add(_box);
        await WaitFor("overlay hidden after sync probe", () => !_view.DebugOverlayVisible);
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

// Calls back once at the end of every main run-loop turn, after the platform has committed that
// turn's drawing (Core Animation commit on Apple, view traversal on Android).
internal sealed class MainLoopTurns : IDisposable
{
#if IOS || MACCATALYST
    internal const bool BeforeDraw = false;
    private static Action? s_current;
    private readonly IntPtr _observer;
    internal unsafe MainLoopTurns(Action onTurn)
    {
        s_current = onTurn;
        // kCFRunLoopBeforeWaiting, ordered after Core Animation's commit observer (2000000).
        _observer = CFRunLoopObserverCreate(IntPtr.Zero, 1 << 5, true, 3_000_000, &OnObserver, IntPtr.Zero);
        var cf = ObjCRuntime.Dlfcn.dlopen(CF, 0);
        var commonModes = ObjCRuntime.Dlfcn.GetIntPtr(cf, "kCFRunLoopCommonModes");
        CFRunLoopAddObserver(CFRunLoopGetMain(), _observer, commonModes);
    }
    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static void OnObserver(IntPtr observer, nuint activity, IntPtr info) => s_current?.Invoke();
    public void Dispose() { CFRunLoopObserverInvalidate(_observer); CFRelease(_observer); s_current = null; }
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [System.Runtime.InteropServices.DllImport(CF)] private static extern unsafe IntPtr CFRunLoopObserverCreate(IntPtr allocator, nuint activities, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] bool repeats, nint order, delegate* unmanaged<IntPtr, nuint, IntPtr, void> callout, IntPtr context);
    [System.Runtime.InteropServices.DllImport(CF)] private static extern IntPtr CFRunLoopGetMain();
    [System.Runtime.InteropServices.DllImport(CF)] private static extern void CFRunLoopAddObserver(IntPtr loop, IntPtr observer, IntPtr mode);
    [System.Runtime.InteropServices.DllImport(CF)] private static extern void CFRunLoopObserverInvalidate(IntPtr observer);
    [System.Runtime.InteropServices.DllImport(CF)] private static extern void CFRelease(IntPtr handle);
#else
    // Android: a pre-draw listener runs as each traversal starts drawing, when the TextureView will
    // latch the newest swapped GPU frame and the Skia overlay will paint.
    internal const bool BeforeDraw = true;
    private readonly Android.Views.ViewTreeObserver _observer;
    private readonly Listener _listener;
    internal MainLoopTurns(Action onTurn)
    {
        _observer = Platform.CurrentActivity!.Window!.DecorView.ViewTreeObserver!;
        _listener = new Listener(onTurn);
        _observer.AddOnDrawListener(_listener);
    }
    public void Dispose() { if (_observer.IsAlive) _observer.RemoveOnDrawListener(_listener); }
    private sealed class Listener(Action onTurn) : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnDrawListener
    { public void OnDraw() => onTurn(); }
#endif
}
#endif

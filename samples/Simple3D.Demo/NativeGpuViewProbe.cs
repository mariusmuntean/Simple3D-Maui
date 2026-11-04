#if (DEBUG || NATIVE_RENDER_PROBE) && (IOS || MACCATALYST || ANDROID)
using Simple3D.Core;
using Simple3D.Maui;
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
        _view = new SceneView { RenderingMode = RenderingMode.Hardware, IsInteractive = false };
        _view.Scene = new Scene().Add(_box);
        _view.RenderingFailed += (_, e) => _failure ??= "RenderingFailed: " + e.Message;
        Content = _view;
    }

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

            Content = new Label { Text = "Detached" };
            _view.Handler?.DisconnectHandler();
            await WaitFor("disconnect", () => _view.ActiveRenderingBackend == RenderingBackend.None);
            Content = _view;
            await WaitFor("hardware after reattach", () => _view.ActiveRenderingBackend == RenderingBackend.Hardware);

            Log("NATIVE_GPU_VIEW_PROBE_PASS");
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            Log($"NATIVE_GPU_VIEW_PROBE_FAIL {error.Message} (backend={_view.ActiveRenderingBackend}, diagnostic={_view.RenderingDiagnostic})");
            Environment.Exit(1);
        }
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

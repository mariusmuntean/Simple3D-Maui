using Microsoft.Maui.Controls;
using Simple3D.Core;
namespace Simple3D.Maui;

public sealed partial class SceneView
{
    /// <summary>Requested rendering mode. Auto prefers hardware with observable software fallback.</summary>
    public static readonly BindableProperty RenderingModeProperty = BindableProperty.Create(nameof(RenderingMode), typeof(RenderingMode), typeof(SceneView),
        RenderingMode.Auto, validateValue: (_, value) => value is RenderingMode mode && Enum.IsDefined(mode),
        propertyChanged: (view, _, _) => ((SceneView)view).ConfigureRendering());
    /// <summary>Choose Auto, Software or Hardware. Changes retain scene, camera and selection.</summary>
    public RenderingMode RenderingMode { get => (RenderingMode)GetValue(RenderingModeProperty); set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); SetValue(RenderingModeProperty, value); } }
    /// <summary>Backend actually presenting frames. None means initializing, disconnected or failed required hardware.</summary>
    public RenderingBackend ActiveRenderingBackend { get; private set; }
    /// <summary>Native backend description, fallback reason, initialization status, or why the current scene content was skipped (for example exceeding the triangle budget).</summary>
    public string? RenderingDiagnostic { get; private set; }
    /// <summary>CPU elapsed time preparing and submitting the last GPU frame, including driver blocking. Not GPU execution time or displayed FPS.</summary>
    public double LastHardwareSubmissionMilliseconds { get; private set; }
    /// <summary>Raised after backend/status changes. Subscribe before attaching the view to observe initialization failures.</summary>
    public event EventHandler? RenderingBackendChanged;
    /// <summary>Raised when hardware initialization or drawing fails. Hardware mode does not silently use software.</summary>
    public event EventHandler<RenderingFailureEventArgs>? RenderingFailed;
    private readonly Grid _surfaces = new();
    private readonly SkiaSharp.Views.Maui.Controls.SKCanvasView _canvas = new() { InputTransparent = true };
    private readonly GpuScene _gpuScene = new();
    private GpuSurface? _gpuSurface;
    private bool _hardwareFailed;

    private void InitializeSurfaces()
    {
        _canvas.PaintSurface += Paint;
        _surfaces.Children.Add(_canvas);
        Content = _surfaces;
    }
    private void ConfigureRendering()
    {
        ReleaseHardware();
        _hardwareFailed = false;
        if (RenderingMode == RenderingMode.Software) SetBackend(RenderingBackend.Software, "Software renderer selected.");
        else if (Handler?.MauiContext is null) SetBackend(RenderingBackend.None, "Waiting for a native surface.");
        else
        {
            SetBackend(RenderingBackend.None, "Initializing native GPU.");
            try
            {
#if ANDROID || IOS || MACCATALYST
                _gpuSurface = new GpuSurface(this) { InputTransparent = true };
                var handler = new GpuSurfaceHandler();
                handler.SetMauiContext(Handler.MauiContext);
                _gpuSurface.Handler = handler;
                _surfaces.Children.Insert(0, _gpuSurface);
#else
                throw new PlatformNotSupportedException("Native GPU rendering requires Android, iOS or Mac Catalyst.");
#endif
            }
            catch (Exception ex) { ReportHardwareFailure(ex); }
        }
        InvalidateSurface();
    }
    private void SetBackend(RenderingBackend backend, string diagnostic)
    {
        ActiveRenderingBackend = backend; RenderingDiagnostic = diagnostic;
        OnPropertyChanged(nameof(ActiveRenderingBackend)); OnPropertyChanged(nameof(RenderingDiagnostic));
        RenderingBackendChanged?.Invoke(this, EventArgs.Empty);
    }
    // Device, driver, shader or allocation failures only. Scene content problems go through
    // TryUpdateGpuScene and never disable hardware rendering.
    internal void ReportHardwareFailure(Exception error)
    {
        if (_hardwareFailed) return;
        _hardwareFailed = true;
        var fallback = RenderingMode == RenderingMode.Auto;
        SetBackend(fallback ? RenderingBackend.Software : RenderingBackend.None, error.Message);
        if (_gpuSurface is { } failed)
        {
            failed.IsVisible = false;
            // Free the native view and its GPU resources. Deferred because failures can be
            // reported from inside native callbacks that still use them.
            void Release() { if (_hardwareFailed && ReferenceEquals(_gpuSurface, failed)) ReleaseHardware(); }
            if (Dispatcher is { } dispatcher) dispatcher.Dispatch(Release); else Release();
        }
        _canvas.InvalidateSurface();
        RenderingFailed?.Invoke(this, new(error.Message, fallback));
    }
    /// <summary>Prepares the hardware scene, or returns null and reports a diagnostic when the scene content cannot be prepared.</summary>
    internal GpuFrame? PrepareHardwareFrame(int width, int height) =>
        TryUpdateGpuScene(width, height) ? new(_gpuScene, Scene, Camera, SelectedShape, BackgroundPixel()) : null;
    private bool TryUpdateGpuScene(int width, int height)
    {
        try
        {
            _gpuScene.Update(Scene, Camera, width, height);
            return true;
        }
        catch (GpuSceneException error)
        {
            // Skip the frame but keep the backend: the next valid scene renders normally.
            if (RenderingDiagnostic != error.Message) SetBackend(ActiveRenderingBackend, error.Message);
            return false;
        }
    }
    internal bool CanRenderHardware => !_hardwareFailed && RenderingMode != RenderingMode.Software;
    internal void HardwareFramePresented(string name, double submissionMilliseconds)
    {
        LastHardwareSubmissionMilliseconds = submissionMilliseconds;
        if (ActiveRenderingBackend != RenderingBackend.Hardware || RenderingDiagnostic != name) SetBackend(RenderingBackend.Hardware, name);
        _canvas.InvalidateSurface();
    }
    private void ReleaseHardware()
    {
        if (_gpuSurface is not { } surface) return;
        _gpuSurface = null;
        surface.Release();
        _surfaces.Children.Remove(surface);
    }
    /// <summary>Requests a new native paint. Scene and camera changes call this automatically.</summary>
    public void InvalidateSurface()
    {
        _gpuSurface?.RequestRender();
        _canvas.InvalidateSurface();
    }
}

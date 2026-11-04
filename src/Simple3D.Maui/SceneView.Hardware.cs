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
    // Labels of the last presented GPU frame, in its render-pixel space. Android paints the overlay
    // from this snapshot so labels match the 3D image even if the camera moved since.
    private readonly List<ProjectedLabel> _presentedLabels = new();
    private int _presentedWidth, _presentedHeight;
#if DEBUG || NATIVE_RENDER_PROBE
    // Diagnostics for the native GPU probes; compiled out of release builds.
    internal int DebugHardwareFrames { get; private set; }
    internal int DebugOverlayPaints { get; private set; }
    internal bool DebugOverlayVisible => _canvas.IsVisible;
    internal object? DebugNativeView => _gpuSurface?.Handler?.PlatformView;
    internal void DebugCountOverlayPaint() => DebugOverlayPaints++;
#endif

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
        UpdateOverlayVisibility();
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
    // renderPixelsPerDip: Render pixels per device-independent unit; sizes the selection outline.
    internal GpuFrame? PrepareHardwareFrame(int width, int height, double renderPixelsPerDip = 1) =>
        TryUpdateGpuScene(width, height) ? new(_gpuScene, Scene, Camera, SelectedShape, BackgroundPixel(), GpuFrame.ClampOutlineRadius(renderPixelsPerDip)) : null;
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
    /// <summary>Whether the Metal frame should be presented inside the Core Animation transaction, so the
    /// overlay (labels, or the opaque placeholder before the first frame) changes in the same frame.</summary>
    internal bool PresentWithOverlay => ActiveRenderingBackend != RenderingBackend.Hardware || _canvas.IsVisible;
    // width, height: Render width of the presented frame (the space of its projected labels).
    internal void HardwareFramePresented(string name, double submissionMilliseconds, int width, int height)
    {
        LastHardwareSubmissionMilliseconds = submissionMilliseconds;
        _presentedLabels.Clear(); _presentedLabels.AddRange(_gpuScene.Labels);
        _presentedWidth = width; _presentedHeight = height;
#if DEBUG || NATIVE_RENDER_PROBE
        DebugHardwareFrames++;
#endif
        var first = ActiveRenderingBackend != RenderingBackend.Hardware;
        if (first || RenderingDiagnostic != name) SetBackend(RenderingBackend.Hardware, name);
#if ANDROID
        // The TextureView shows this frame at the next traversal; repaint labels for the same frame.
        if (_canvas.IsVisible) _canvas.InvalidateSurface();
#else
        // Apple repaints the overlay with each request instead (same transaction as the Metal frame);
        // here only the first frame replaces the opaque placeholder.
        if (first && _canvas.IsVisible) _canvas.InvalidateSurface();
#endif
    }
    // In hardware mode the transparent Skia layer is only composited and repainted when the scene has
    // labels; it is a full-screen CPU raster surface otherwise redrawn for nothing every frame.
    private void UpdateOverlayVisibility()
    {
        var visible = ActiveRenderingBackend != RenderingBackend.Hardware || Scene.Labels.Count > 0;
        if (_canvas.IsVisible != visible) _canvas.IsVisible = visible;
    }
    internal (IReadOnlyList<ProjectedLabel> Labels, int Width, int Height)? HardwareOverlayLabels(int surfaceWidth, int surfaceHeight)
    {
#if ANDROID
        return _presentedWidth > 0 ? (_presentedLabels, _presentedWidth, _presentedHeight) : null;
#else
        return TryUpdateGpuScene(surfaceWidth, surfaceHeight) ? (_gpuScene.Labels, surfaceWidth, surfaceHeight) : null;
#endif
    }
    private void ReleaseHardware()
    {
        if (_gpuSurface is not { } surface) return;
        _gpuSurface = null;
        surface.Release();
    }
    /// <summary>Requests a new native paint. Scene and camera changes call this automatically.</summary>
    public void InvalidateSurface()
    {
        _gpuSurface?.RequestRender();
        if (ActiveRenderingBackend != RenderingBackend.Hardware) { _canvas.InvalidateSurface(); return; }
        UpdateOverlayVisibility();
#if !ANDROID
        if (_canvas.IsVisible) _canvas.InvalidateSurface();
#endif
    }
}

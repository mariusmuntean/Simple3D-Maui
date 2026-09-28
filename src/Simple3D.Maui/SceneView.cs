using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Simple3D.Core;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
#if MACCATALYST
using UIKit;
#endif

namespace Simple3D.Maui;

/// <summary>A depth-rendered, touch-enabled surface for small opaque scenes. Mutate its scene and camera on the UI thread.</summary>
public sealed class SceneView : SKCanvasView
{
    private const int DragPreviewMaximumDimension = 1024;
    /// <summary>The scene displayed by this view.</summary>
    public static readonly BindableProperty SceneProperty = BindableProperty.Create(nameof(Scene), typeof(Scene), typeof(SceneView),
        defaultValueCreator: _ => new Scene(), propertyChanged: (bindable, oldValue, newValue) =>
            ((SceneView)bindable).ReplaceScene((Scene?)oldValue, (Scene?)newValue));
    /// <summary>The camera used by this view.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(nameof(Camera), typeof(Camera), typeof(SceneView),
        defaultValueCreator: _ => new Camera(), propertyChanged: (bindable, oldValue, newValue) =>
            ((SceneView)bindable).ReplaceCamera((Camera?)oldValue, (Camera?)newValue));
    /// <summary>The opaque clear color, expressed as a MAUI color.</summary>
    public static readonly BindableProperty SceneBackgroundColorProperty = BindableProperty.Create(nameof(SceneBackgroundColor), typeof(Color), typeof(SceneView),
        Color.FromArgb("#F4F6FA"), propertyChanged: (bindable, _, _) => ((SceneView)bindable).Refresh());
    /// <summary>Whether the view installs orbit, zoom and picking gestures.</summary>
    public static readonly BindableProperty IsInteractiveProperty = BindableProperty.Create(nameof(IsInteractive), typeof(bool), typeof(SceneView),
        true, propertyChanged: (bindable, _, _) => ((SceneView)bindable).UpdateInteraction());
    /// <summary>Maximum physical width or height used for native painting.</summary>
    public static readonly BindableProperty MaximumRenderDimensionProperty = BindableProperty.Create(nameof(MaximumRenderDimension), typeof(int), typeof(SceneView),
        DepthRenderer.MaximumDimension, validateValue: (_, value) => value is int size && size >= 1 && size <= DepthRenderer.MaximumDimension,
        propertyChanged: (bindable, _, _) => ((SceneView)bindable).Refresh());

    private DepthRenderer _renderer = new();
    private RenderFrame? _frame;
    private RenderTarget? _paintTarget;
    private RenderTarget? _paintBitmapTarget;
    private SKBitmap? _paintBitmap;
    private long _paintVersion;
    private long _bitmapVersion;
    private int _paintSurfaceWidth, _paintSurfaceHeight;
    private int _paintRequestedWidth, _paintRequestedHeight;
    private (long Nodes, long Triangles) _fallbackGeometry;
    private float _fallbackDistance, _fallbackOrthographicHeight, _fallbackFieldOfView;
    private CameraProjection _fallbackProjection;
    private long _lastPaintTimestamp;
    private long _lastFailedProbeTimestamp;
    private long _failedProbeSamples;
    private bool _dirty = true;
    private bool _paintTargetDirty = true;
    private bool _subscriptionsActive;
    private bool _wasConnected;
    private bool _panActive;
    private double _lastPanX, _lastPanY;
    private double _lastMacPinchScale = 1;
    private readonly List<IGestureRecognizer> _interactionGestures = new();
#if MACCATALYST
    private UIView? _macPinchView;
    private UIPinchGestureRecognizer? _macPinchRecognizer;
#endif

    /// <summary>Raised when the user taps a visible shape or the background.</summary>
    public event EventHandler<Shape?>? SelectionChanged;
    /// <summary>The last shape selected by a tap, or null for the background.</summary>
    public Shape? SelectedShape { get; private set; }

    /// <summary>Creates a view and installs orbit, pinch and picking gestures.</summary>
    public SceneView()
    {
        PaintSurface += Paint;
        SetSubscriptions(true);
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started) { _lastPanX = _lastPanY = 0; _panActive = true; }
            else if (args.StatusType == GestureStatus.Running)
            {
                Orbit((float)((args.TotalX - _lastPanX) * .012), (float)(-(args.TotalY - _lastPanY) * .012));
                _lastPanX = args.TotalX;
                _lastPanY = args.TotalY;
            }
            else { _panActive = false; Refresh(); }
        };
        _interactionGestures.Add(pan);
#if !MACCATALYST
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) => { if (args.Status == GestureStatus.Running) Zoom((float)args.Scale); };
        _interactionGestures.Add(pinch);
#endif
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, args) =>
        {
            var point = args.GetPosition(this);
            if (point is null) return;
            SelectedShape = PickAt(point.Value.X, point.Value.Y, Width, Height);
            SelectionChanged?.Invoke(this, SelectedShape);
        };
        _interactionGestures.Add(tap);
        UpdateInteraction();
    }

    /// <summary>Scene instance. Changes to this scene invalidate the cached frame.</summary>
    public Scene Scene
    {
        get => (Scene)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }
    /// <summary>Camera instance. Changes to this camera invalidate the cached frame.</summary>
    public Camera Camera
    {
        get => (Camera)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }
    /// <summary>Opaque MAUI color behind the scene.</summary>
    public Color SceneBackgroundColor
    {
        get => (Color)GetValue(SceneBackgroundColorProperty);
        set => SetValue(SceneBackgroundColorProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }
    /// <summary>Enable orbit, zoom and tap selection. Set false for a display only scene.</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }
    /// <summary>Maximum physical render dimension for native paints. Lower values reduce per-frame work during animation.</summary>
    public int MaximumRenderDimension
    {
        get => (int)GetValue(MaximumRenderDimensionProperty);
        set => SetValue(MaximumRenderDimensionProperty, value);
    }

    /// <summary>Orbit the camera by radians.</summary>
    public void Orbit(float yawDelta, float pitchDelta) => Camera.Orbit(yawDelta, pitchDelta);
    /// <summary>Zoom by an incremental factor.</summary>
    public void Zoom(float factor) => Camera.Zoom(factor);
    internal void ApplyMacPinch(GestureStatus status, double scale)
    {
        if (!IsInteractive) return;
        if (status == GestureStatus.Started) { _lastMacPinchScale = 1; return; }
        if (status != GestureStatus.Running) { _lastMacPinchScale = 1; return; }
        if (!double.IsFinite(scale) || scale <= 0) return;
        Zoom((float)(scale / _lastMacPinchScale));
        _lastMacPinchScale = scale;
    }
    /// <summary>Restore the default camera for this view.</summary>
    public void ResetCamera() => Camera = new Camera();
    /// <summary>Invalidate the frame after external changes that do not raise scene or camera notifications.</summary>
    public void Refresh()
    {
        _dirty = true;
        _paintTargetDirty = true;
        InvalidateSurface();
    }

    /// <summary>Render or retrieve an owned frame of the requested physical pixel dimensions.</summary>
    public RenderFrame CaptureFrame(int width, int height)
    {
        if (_dirty || _frame is null || _frame.Width != width || _frame.Height != height)
        {
            _frame = _renderer.Render(Scene, Camera, width, height, BackgroundPixel());
            _dirty = false;
        }
        return _frame;
    }

    /// <summary>Pick at view coordinates measured from the top left. Uses the native paint target when available, otherwise the last owned frame.</summary>
    public Shape? PickAt(double x, double y, double viewWidth, double viewHeight)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            viewWidth <= 0 || viewHeight <= 0 || x < 0 || y < 0 || x >= viewWidth || y >= viewHeight) return null;
        if (_paintTarget is not null)
        {
            if (_paintTargetDirty) CapturePaintTarget(_paintSurfaceWidth, _paintSurfaceHeight);
            return _paintTarget.Pick((int)(x * _paintTarget.Width / viewWidth), (int)(y * _paintTarget.Height / viewHeight));
        }
        if (_frame is null) return null;
        if (_dirty) CaptureFrame(_frame.Width, _frame.Height);
        return _frame.Pick((int)(x * _frame.Width / viewWidth), (int)(y * _frame.Height / viewHeight));
    }

    private uint BackgroundPixel()
    {
        var c = SceneBackgroundColor;
        if (c.Alpha < 1) throw new ArgumentException("Scene background must be opaque.", nameof(SceneBackgroundColor));
        return 0xFF000000u | ((uint)Math.Round(c.Red * 255) << 16) |
            ((uint)Math.Round(c.Green * 255) << 8) | (uint)Math.Round(c.Blue * 255);
    }

    /// <summary>Attaches notifications when the native handler connects and releases them on disconnect.</summary>
    protected override void OnHandlerChanged()
    {
#if MACCATALYST
        if (_macPinchRecognizer is not null)
        {
            _macPinchView?.RemoveGestureRecognizer(_macPinchRecognizer);
            _macPinchRecognizer.Dispose();
            _macPinchRecognizer = null;
            _macPinchView = null;
        }
#endif
        base.OnHandlerChanged();
        if (Handler is null)
        {
            _panActive = false;
            ReleaseRenderResources();
            if (_wasConnected) SetSubscriptions(false);
        }
        else
        {
            _wasConnected = true;
            SetSubscriptions(true);
            Refresh();
#if MACCATALYST
            if (Handler.PlatformView is UIView platformView)
            {
                // Trackpad pinches have zero UIKit touches; the MAUI bridge ends them after one update.
                _macPinchRecognizer = new UIPinchGestureRecognizer(recognizer =>
                {
                    var status = recognizer.State switch
                    {
                        UIGestureRecognizerState.Began => GestureStatus.Started,
                        UIGestureRecognizerState.Changed => GestureStatus.Running,
                        UIGestureRecognizerState.Ended => GestureStatus.Completed,
                        _ => GestureStatus.Canceled
                    };
                    ApplyMacPinch(status, recognizer.Scale);
                });
                _macPinchRecognizer.Enabled = IsInteractive;
                platformView.AddGestureRecognizer(_macPinchRecognizer);
                _macPinchView = platformView;
            }
#endif
        }
    }

    private void SetSubscriptions(bool active)
    {
        if (active == _subscriptionsActive) return;
        if (active) { Scene.Changed += SourceChanged; Camera.Changed += SourceChanged; }
        else { Scene.Changed -= SourceChanged; Camera.Changed -= SourceChanged; }
        _subscriptionsActive = active;
    }
    private void ReplaceScene(Scene? previous, Scene? current)
    {
        if (_subscriptionsActive)
        {
            if (previous is not null) previous.Changed -= SourceChanged;
            if (current is not null) current.Changed += SourceChanged;
        }
        SelectedShape = null;
        _paintRequestedWidth = _paintRequestedHeight = 0;
        _lastFailedProbeTimestamp = 0;
        Refresh();
    }
    private void ReplaceCamera(Camera? previous, Camera? current)
    {
        if (_subscriptionsActive)
        {
            if (previous is not null) previous.Changed -= SourceChanged;
            if (current is not null) current.Changed += SourceChanged;
        }
        _paintRequestedWidth = _paintRequestedHeight = 0;
        _lastFailedProbeTimestamp = 0;
        Refresh();
    }
    private void SourceChanged(object? sender, EventArgs args) => Refresh();

    private void UpdateInteraction()
    {
        _panActive = false;
        GestureRecognizers.Clear();
        if (IsInteractive)
            foreach (var gesture in _interactionGestures) GestureRecognizers.Add(gesture);
#if MACCATALYST
        if (_macPinchRecognizer is not null) _macPinchRecognizer.Enabled = IsInteractive;
#endif
        Refresh();
    }

    internal static (int Width, int Height) RenderSize(int width, int height, int maximumDimension = DepthRenderer.MaximumDimension)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        var scale = Math.Min(1.0, (double)maximumDimension / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    internal RenderTarget CapturePaintTarget(int surfaceWidth, int surfaceHeight)
    {
        _paintSurfaceWidth = surfaceWidth;
        _paintSurfaceHeight = surfaceHeight;
        var (width, height) = RenderSize(surfaceWidth, surfaceHeight,
            _panActive ? Math.Min(DragPreviewMaximumDimension, MaximumRenderDimension) : MaximumRenderDimension);
        var requestedWidth = width;
        var requestedHeight = height;
        if (_paintTarget is not null && !_paintTargetDirty &&
            _paintRequestedWidth == width && _paintRequestedHeight == height) return _paintTarget;
        var retryAfterIdle = _paintTargetDirty && _lastPaintTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastPaintTimestamp) >= TimeSpan.FromMilliseconds(500);
        var probeCooldown = _paintTarget is not null && _lastFailedProbeTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastFailedProbeTimestamp) < TimeSpan.FromSeconds(2) &&
            _paintTarget.RasterSamples >= _failedProbeSamples * .5;
        if (_paintTarget is not null && _paintRequestedWidth == width && _paintRequestedHeight == height &&
            (_paintTarget.Width < width || _paintTarget.Height < height) &&
            Camera.Projection == _fallbackProjection &&
            Camera.Distance < _fallbackDistance * 1.25f &&
            Camera.OrthographicHeight < _fallbackOrthographicHeight * 1.25f &&
            Camera.FieldOfView < _fallbackFieldOfView * 1.15f &&
            SceneGeometry(Scene) == _fallbackGeometry && (!retryAfterIdle || probeCooldown) &&
            (probeCooldown || EstimatedFullResolutionWork(_paintTarget, width, height) >= DepthRenderer.MaximumRasterSamples * .75))
        {
            width = _paintTarget.Width;
            height = _paintTarget.Height;
        }
        var attemptedRequestedSize = width == requestedWidth && height == requestedHeight;
        var previousTarget = _paintTarget;
        while (true)
        {
            if (_paintTarget is null || _paintTarget.Width != width || _paintTarget.Height != height)
            {
                _paintTarget = new RenderTarget(width, height);
                _paintTargetDirty = true;
            }
            if (!_paintTargetDirty) return _paintTarget;
            try
            {
                _renderer.RenderInto(Scene, Camera, _paintTarget, BackgroundPixel());
                _paintTargetDirty = false;
                _paintRequestedWidth = requestedWidth;
                _paintRequestedHeight = requestedHeight;
                if (attemptedRequestedSize && (width < requestedWidth || height < requestedHeight))
                {
                    _fallbackDistance = Camera.Distance;
                    _fallbackOrthographicHeight = Camera.OrthographicHeight;
                    _fallbackFieldOfView = Camera.FieldOfView;
                    _fallbackProjection = Camera.Projection;
                    _fallbackGeometry = SceneGeometry(Scene);
                }
                else if (width == requestedWidth && height == requestedHeight)
                    _lastFailedProbeTimestamp = 0;
                _lastPaintTimestamp = Stopwatch.GetTimestamp();
                _paintVersion++;
                return _paintTarget;
            }
            catch (RasterBudgetExceededException) when (width > 1 || height > 1)
            {
                if (attemptedRequestedSize && width == requestedWidth && height == requestedHeight &&
                    previousTarget is not null &&
                    (previousTarget.Width < requestedWidth || previousTarget.Height < requestedHeight))
                {
                    _lastFailedProbeTimestamp = Stopwatch.GetTimestamp();
                    _failedProbeSamples = previousTarget.RasterSamples;
                }
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }
    }

    private static double EstimatedFullResolutionWork(RenderTarget target, int requestedWidth, int requestedHeight) =>
        target.RasterSamples * ((double)requestedWidth / target.Width) * ((double)requestedHeight / target.Height);

    internal static (long Nodes, long Triangles) SceneGeometry(Scene scene)
    {
        long nodes = 0, triangles = 0;
        foreach (var shape in scene.Shapes) Visit(shape);
        return (nodes, triangles);

        void Visit(Shape shape)
        {
            if (++nodes > Scene.MaximumNodes)
                throw new ArgumentException("Scene exceeds node visit budget.", nameof(scene));
            triangles += shape.TriangleCount;
            foreach (var child in shape.Children) Visit(child);
        }
    }

    internal static float LabelFontSize(int surfaceWidth, double layoutWidth) =>
        layoutWidth > 0 ? 14f * (float)(surfaceWidth / layoutWidth) : 14f;

    internal SKBitmap PaintBitmap(RenderTarget target)
    {
        if (_paintBitmap is null || _paintBitmap.Width != target.Width || _paintBitmap.Height != target.Height)
        {
            ReleasePaintBitmap();
            _paintBitmap = new SKBitmap(new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        }
        if (!ReferenceEquals(_paintBitmapTarget, target) || _bitmapVersion != _paintVersion)
        {
            MemoryMarshal.AsBytes(target.Pixels.Span).CopyTo(_paintBitmap.GetPixelSpan());
            _paintBitmap.NotifyPixelsChanged();
            _paintBitmapTarget = target;
            _bitmapVersion = _paintVersion;
        }
        return _paintBitmap;
    }

    internal void ReleasePaintBitmap()
    {
        _paintBitmap?.Dispose();
        _paintBitmap = null;
        _paintBitmapTarget = null;
        _bitmapVersion = 0;
    }

    internal void ReleaseRenderResources()
    {
        ReleasePaintBitmap();
        _frame = null;
        _paintTarget = null;
        _renderer = new DepthRenderer();
        _dirty = true;
        _paintTargetDirty = true;
        _paintSurfaceWidth = _paintSurfaceHeight = 0;
        _paintRequestedWidth = _paintRequestedHeight = 0;
        _lastPaintTimestamp = 0;
        _lastFailedProbeTimestamp = 0;
        _failedProbeSamples = 0;
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        if (args.Info.Width < 1 || args.Info.Height < 1) return;
        var target = CapturePaintTarget(args.Info.Width, args.Info.Height);
        canvas.DrawBitmap(PaintBitmap(target), new SKRect(0, 0, args.Info.Width, args.Info.Height));
        if (target.Labels.Count == 0) return;
        using var font = new SKFont(SKTypeface.Default, LabelFontSize(args.Info.Width, Width));
        using var paint = new SKPaint { IsAntialias = true };
        var sx = (float)args.Info.Width / target.Width;
        var sy = (float)args.Info.Height / target.Height;
        foreach (var label in target.Labels)
        {
            paint.Color = new SKColor((byte)(label.Label.Color >> 16), (byte)(label.Label.Color >> 8), (byte)label.Label.Color);
            canvas.DrawText(label.Label.Text, label.Position.X * sx, label.Position.Y * sy, font, paint);
        }
    }
}

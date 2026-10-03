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
    private bool[] _selectionIds = [];
    private const int DragPreviewMaximumDimension = 512;
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
    /// <summary>Whether native painting softens high-contrast diagonal pixel steps.</summary>
    public static readonly BindableProperty IsAntialiasEnabledProperty = BindableProperty.Create(nameof(IsAntialiasEnabled), typeof(bool), typeof(SceneView),
        true, propertyChanged: (bindable, _, _) =>
        {
            var view = (SceneView)bindable;
            view._bitmapVersion = -1;
            view.InvalidateSurface();
        });
    /// <summary>Maximum physical width or height used for native painting.</summary>
    public static readonly BindableProperty MaximumRenderDimensionProperty = BindableProperty.Create(nameof(MaximumRenderDimension), typeof(int), typeof(SceneView),
        DepthRenderer.MaximumDimension, validateValue: (_, value) => value is int size && size >= 1 && size <= DepthRenderer.MaximumDimension,
        propertyChanged: (bindable, _, _) => ((SceneView)bindable).Refresh());

    private DepthRenderer _renderer = new();
    private RenderFrame? _frame;
    private RenderTarget? _paintTarget;
    private RenderTarget? _paintBitmapTarget;
    private SKBitmap? _paintBitmap;
    private Shape? _bitmapSelection;
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
    private bool _pinchActive;
    private double _lastPanX, _lastPanY;
    private double _lastMacPinchScale = 1;
    private readonly List<IGestureRecognizer> _interactionGestures = new();
    private readonly List<PaintedLabel> _paintLabels = new();
#if MACCATALYST
    private UIView? _macGestureView;
    private UIPanGestureRecognizer? _macPanRecognizer;
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
#if !MACCATALYST
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) => ApplyPan(args.StatusType, args.TotalX, args.TotalY);
        _interactionGestures.Add(pan);
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) =>
        {
            _pinchActive = args.Status is GestureStatus.Started or GestureStatus.Running;
            if (args.Status == GestureStatus.Running) Zoom((float)args.Scale);
            else if (!_pinchActive) Refresh();
        };
        _interactionGestures.Add(pinch);
#endif
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, args) =>
        {
            var point = args.GetPosition(this);
            if (point is null) return;
            SelectAt(point.Value.X, point.Value.Y, Width, Height);
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
    /// <summary>Soften diagonal edges during native painting. Core captures and picking remain unchanged.</summary>
    public bool IsAntialiasEnabled
    {
        get => (bool)GetValue(IsAntialiasEnabledProperty);
        set => SetValue(IsAntialiasEnabledProperty, value);
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
    internal void ApplyPan(GestureStatus status, double totalX, double totalY)
    {
        if (!IsInteractive) return;
        if (status == GestureStatus.Started)
        {
            _lastPanX = _lastPanY = 0;
            _panActive = true;
            return;
        }
        if (status == GestureStatus.Running)
        {
            if (!_panActive || !double.IsFinite(totalX) || !double.IsFinite(totalY)) return;
            Orbit((float)((totalX - _lastPanX) * .012), (float)(-(totalY - _lastPanY) * .012));
            _lastPanX = totalX;
            _lastPanY = totalY;
            return;
        }
        _panActive = false;
        Refresh();
    }

    internal void ApplyMacPan(GestureStatus status, double totalX, double totalY)
    {
        if (status == GestureStatus.Started)
        {
            ApplyPan(status, 0, 0);
            ApplyPan(GestureStatus.Running, totalX, totalY);
        }
        else if (status == GestureStatus.Completed)
        {
            ApplyPan(GestureStatus.Running, totalX, totalY);
            ApplyPan(status, totalX, totalY);
        }
        else ApplyPan(status, totalX, totalY);
    }

    internal void ApplyMacPinch(GestureStatus status, double scale)
    {
        if (!IsInteractive) return;
        if (status == GestureStatus.Started) { _lastMacPinchScale = 1; _pinchActive = true; return; }
        if (status != GestureStatus.Running)
        {
            _lastMacPinchScale = 1;
            _pinchActive = false;
            Refresh();
            return;
        }
        _pinchActive = true;
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

    /// <summary>Select a visible shape at layout coordinates, or clear selection on the background.</summary>
    public Shape? SelectAt(double x, double y, double viewWidth, double viewHeight)
    {
        var selected = PickAt(x, y, viewWidth, viewHeight);
        if (ReferenceEquals(selected, SelectedShape)) return selected;
        SelectedShape = selected;
        InvalidateSurface();
        SelectionChanged?.Invoke(this, SelectedShape);
        return SelectedShape;
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
        if (_macPanRecognizer is not null)
        {
            _macGestureView?.RemoveGestureRecognizer(_macPanRecognizer);
            _macPanRecognizer.Dispose();
            _macPanRecognizer = null;
        }
        if (_macPinchRecognizer is not null)
        {
            _macGestureView?.RemoveGestureRecognizer(_macPinchRecognizer);
            _macPinchRecognizer.Dispose();
            _macPinchRecognizer = null;
            _macGestureView = null;
        }
#endif
        base.OnHandlerChanged();
        if (Handler is null)
        {
            _panActive = _pinchActive = false;
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
                // Mouse pans can report zero touches during Changed; use native translation directly.
                _macPanRecognizer = new UIPanGestureRecognizer(recognizer =>
                {
                    var translation = recognizer.TranslationInView(platformView);
                    ApplyMacPan(MacGestureStatus(recognizer.State), translation.X, translation.Y);
                });
                _macPanRecognizer.Enabled = IsInteractive;
                platformView.AddGestureRecognizer(_macPanRecognizer);
                // Trackpad pinches have zero UIKit touches; the MAUI bridge ends them after one update.
                _macPinchRecognizer = new UIPinchGestureRecognizer(recognizer =>
                {
                    ApplyMacPinch(MacGestureStatus(recognizer.State), recognizer.Scale);
                });
                _macPinchRecognizer.Enabled = IsInteractive;
                platformView.AddGestureRecognizer(_macPinchRecognizer);
                _macGestureView = platformView;
            }
#endif
        }
    }

#if MACCATALYST
    private static GestureStatus MacGestureStatus(UIGestureRecognizerState state) => state switch
    {
        UIGestureRecognizerState.Began => GestureStatus.Started,
        UIGestureRecognizerState.Changed => GestureStatus.Running,
        UIGestureRecognizerState.Ended => GestureStatus.Completed,
        _ => GestureStatus.Canceled
    };
#endif

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
    private void SourceChanged(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, Scene) && SelectedShape is not null)
        {
            var previous = SelectedShape;
            if (args is ShapeReplacementEventArgs replacement)
            {
                var remaining = Scene.MaximumNodes;
                var mapped = MapSelection(replacement.OldShape, replacement.NewShape, previous, ref remaining);
                if (mapped.Found) SelectedShape = mapped.Shape;
            }
            else
            {
                var remaining = Scene.MaximumNodes;
                var present = false;
                foreach (var root in Scene.Shapes)
                    if (MapSelection(root, root, previous, ref remaining).Found) { present = true; break; }
                if (!present) SelectedShape = null;
            }
            if (!ReferenceEquals(previous, SelectedShape))
            {
                Refresh();
                SelectionChanged?.Invoke(this, SelectedShape);
                return;
            }
        }
        Refresh();
    }

    private static (bool Found, Shape? Shape) MapSelection(Shape oldShape, Shape? newShape, Shape selected, ref int remaining)
    {
        if (--remaining < 0) throw new ArgumentException("Scene exceeds node visit budget.");
        if (ReferenceEquals(oldShape, selected)) return (true, newShape);
        for (var i = 0; i < oldShape.Children.Count; i++)
        {
            var next = newShape is not null && i < newShape.Children.Count ? newShape.Children[i] : null;
            var result = MapSelection(oldShape.Children[i], next, selected, ref remaining);
            if (result.Found) return result;
        }
        return (false, null);
    }

    private void UpdateInteraction()
    {
        _panActive = _pinchActive = false;
        GestureRecognizers.Clear();
        if (IsInteractive)
            foreach (var gesture in _interactionGestures) GestureRecognizers.Add(gesture);
#if MACCATALYST
        if (_macPinchRecognizer is not null) _macPinchRecognizer.Enabled = IsInteractive;
        if (_macPanRecognizer is not null) _macPanRecognizer.Enabled = IsInteractive;
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
            (_panActive || _pinchActive) ? Math.Min(DragPreviewMaximumDimension, MaximumRenderDimension) : MaximumRenderDimension);
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
        var selected = SelectedShape;
        if (_paintBitmap is null || _paintBitmap.Width != target.Width || _paintBitmap.Height != target.Height)
        {
            ReleasePaintBitmap();
            _paintBitmap = new SKBitmap(new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        }
        if (!ReferenceEquals(_paintBitmapTarget, target) || _bitmapVersion != _paintVersion ||
            !ReferenceEquals(_bitmapSelection, selected))
        {
            MemoryMarshal.AsBytes(target.Pixels.Span).CopyTo(_paintBitmap.GetPixelSpan());
            if (IsAntialiasEnabled)
                SmoothDiagonalEdges(target.Pixels.Span, MemoryMarshal.Cast<byte, uint>(_paintBitmap.GetPixelSpan()), target.Width, target.Height);
            if (selected is not null)
            {
                var pixels = MemoryMarshal.Cast<byte, uint>(_paintBitmap.GetPixelSpan());
                var ids = target.VisibleIds;
                var shapes = target.VisibleShapes;
                if (_selectionIds.Length < shapes.Length) _selectionIds = new bool[shapes.Length];
                for (var i = 0; i < shapes.Length; i++) _selectionIds[i] = ReferenceEquals(shapes[i], selected);
                var width = target.Width;
                var height = target.Height;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;
                        if (index < ids.Length && ids[index] >= 0 && _selectionIds[ids[index]])
                        {
                            if (x < 2 || x + 2 >= width || y < 2 || y + 2 >= height ||
                                ids[index - 2] < 0 || !_selectionIds[ids[index - 2]] ||
                                ids[index + 2] < 0 || !_selectionIds[ids[index + 2]] ||
                                ids[index - 2 * width] < 0 || !_selectionIds[ids[index - 2 * width]] ||
                                ids[index + 2 * width] < 0 || !_selectionIds[ids[index + 2 * width]])
                            {
                                pixels[index] = 0xFFFFD27Au;
                                continue;
                            }
                            var pixel = pixels[index];
                            // A mint tint preserves the underlying light and never changes scene materials.
                            var red = (((pixel >> 16) & 255) * 2 + 142) / 3;
                            var green = (((pixel >> 8) & 255) * 2 + 225) / 3;
                            var blue = ((pixel & 255) * 2 + 205) / 3;
                            pixels[index] = 0xFF000000u | (red << 16) | (green << 8) | blue;
                        }
                    }
            }
            _bitmapSelection = selected;
            _paintBitmap.NotifyPixelsChanged();
            _paintBitmapTarget = target;
            _bitmapVersion = _paintVersion;
        }
        return _paintBitmap;
    }

    internal static void SmoothDiagonalEdges(ReadOnlySpan<uint> source, Span<uint> destination, int width, int height)
    {
        for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;
                var center = source[index];
                var north = source[index - width];
                var south = source[index + width];
                var west = source[index - 1];
                var east = source[index + 1];
                if ((north == center && south == center) || (west == center && east == center)) continue;
                var c = Luma(center);
                var n = Luma(north);
                var s = Luma(south);
                var w = Luma(west);
                var e = Luma(east);
                var range = Math.Max(c, Math.Max(Math.Max(n, s), Math.Max(w, e))) -
                    Math.Min(c, Math.Min(Math.Min(n, s), Math.Min(w, e)));
                if (range < 24) continue;
                var threshold = Math.Max(24, range / 4);
                var northEdge = Math.Abs(n - c) >= threshold;
                var southEdge = Math.Abs(s - c) >= threshold;
                var westEdge = Math.Abs(w - c) >= threshold;
                var eastEdge = Math.Abs(e - c) >= threshold;
                // Two perpendicular transitions describe a staircase corner, unlike a straight edge or thin line.
                if (northEdge == southEdge || westEdge == eastEdge) continue;
                var a = northEdge ? north : south;
                var b = westEdge ? west : east;
                var redBlue = (((center & 0x00FF00FFu) * 6 + (a & 0x00FF00FFu) + (b & 0x00FF00FFu)) >> 3) & 0x00FF00FFu;
                var green = (((center & 0x0000FF00u) * 6 + (a & 0x0000FF00u) + (b & 0x0000FF00u)) >> 3) & 0x0000FF00u;
                destination[index] = 0xFF000000u | redBlue | green;
            }

        static int Luma(uint pixel) => (int)((((pixel >> 16) & 255) * 54 + ((pixel >> 8) & 255) * 183 + (pixel & 255) * 19) >> 8);
    }

    internal void ReleasePaintBitmap()
    {
        _paintBitmap?.Dispose();
        _paintBitmap = null;
        _paintBitmapTarget = null;
        _bitmapSelection = null;
        _bitmapVersion = 0;
    }

    internal void ReleaseRenderResources()
    {
        ReleasePaintBitmap();
        _frame = null;
        _paintTarget = null;
        _paintLabels.Clear();
        _selectionIds = [];
        _renderer = new DepthRenderer();
        _dirty = true;
        _paintTargetDirty = true;
        _paintSurfaceWidth = _paintSurfaceHeight = 0;
        _paintRequestedWidth = _paintRequestedHeight = 0;
        _lastPaintTimestamp = 0;
        _lastFailedProbeTimestamp = 0;
        _failedProbeSamples = 0;
    }

    internal static void DrawSceneBitmap(SKCanvas canvas, SKBitmap bitmap, int width, int height)
    {
        using var shader = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear),
            SKMatrix.CreateScale((float)width / bitmap.Width, (float)height / bitmap.Height));
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
    }

    internal readonly record struct PaintedLabel(string Text, SKPoint Position, SKRect Bounds, uint Color);

    internal IReadOnlyList<PaintedLabel> LayoutLabels(IReadOnlyList<ProjectedLabel> labels, SKFont font,
        int width, int height, int renderWidth, int renderHeight)
    {
        _paintLabels.Clear();
        var margin = font.Size * .25f;
        if (width <= margin * 2 || height <= margin * 2) return _paintLabels;
        foreach (var label in labels)
        {
            var text = FitLabelText(label.Label.Text, font, width - margin * 2);
            font.MeasureText(text, out var bounds);
            if (bounds.IsEmpty || bounds.Height > height - margin * 2) continue;
            var x = Math.Clamp(label.Position.X * width / renderWidth, margin - bounds.Left, width - margin - bounds.Right);
            var y = Math.Clamp(label.Position.Y * height / renderHeight, margin - bounds.Top, height - margin - bounds.Bottom);
            var rowHeight = bounds.Height + margin;
            // Search at most eight rows in either direction; dense labels must not stall a frame.
            var rows = Math.Min(17, (int)(height / rowHeight) * 2 + 1);
            for (var row = 0; row < rows; row++)
            {
                var rowY = y + ((row + 1) / 2) * rowHeight * (row % 2 == 0 ? -1 : 1);
                var candidate = bounds;
                candidate.Offset(x, rowY);
                if (candidate.Top < margin || candidate.Bottom > height - margin) continue;
                var padded = candidate;
                padded.Inflate(margin, margin);
                var overlaps = false;
                foreach (var existing in _paintLabels)
                    if (padded.IntersectsWith(existing.Bounds)) { overlaps = true; break; }
                if (overlaps) continue;
                _paintLabels.Add(new(text, new(x, rowY), candidate, label.Label.Color));
                break;
            }
        }
        return _paintLabels;
    }

    private static string FitLabelText(string text, SKFont font, float width)
    {
        font.MeasureText(text, out var bounds);
        if (bounds.Width <= width) return text;
        const string ellipsis = "…";
        font.MeasureText(ellipsis, out bounds);
        if (bounds.Width > width) return "";
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = elements.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == elements.Length ? text.Length : elements[middle];
            font.MeasureText(text[..end] + ellipsis, out bounds);
            if (bounds.Width <= width) low = middle;
            else high = middle - 1;
        }
        return text[..(low == elements.Length ? text.Length : elements[low])] + ellipsis;
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        if (args.Info.Width < 1 || args.Info.Height < 1) return;
        var target = CapturePaintTarget(args.Info.Width, args.Info.Height);
        DrawSceneBitmap(canvas, PaintBitmap(target), args.Info.Width, args.Info.Height);
        if (target.Labels.Count == 0) return;
        using var font = new SKFont(SKTypeface.Default, LabelFontSize(args.Info.Width, Width));
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var label in LayoutLabels(target.Labels, font, args.Info.Width, args.Info.Height, target.Width, target.Height))
        {
            paint.Color = new SKColor((byte)(label.Color >> 16), (byte)(label.Color >> 8), (byte)label.Color);
            canvas.DrawText(label.Text, label.Position.X, label.Position.Y, font, paint);
        }
    }
}

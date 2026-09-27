using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Simple3D.Core;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Simple3D.Maui;

/// <summary>A depth-rendered, touch-enabled surface for small opaque scenes. Mutate its scene and camera on the UI thread.</summary>
public sealed class SceneView : SKCanvasView
{
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

    private readonly DepthRenderer _renderer = new();
    private RenderFrame? _frame;
    private bool _dirty = true;
    private bool _subscriptionsActive = true;
    private bool _wasConnected;
    private double _lastPanX, _lastPanY;

    /// <summary>Raised when the user taps a visible shape or the background.</summary>
    public event EventHandler<Shape?>? SelectionChanged;
    /// <summary>The last shape selected by a tap, or null for the background.</summary>
    public Shape? SelectedShape { get; private set; }

    /// <summary>Creates a view and installs orbit, pinch and picking gestures.</summary>
    public SceneView()
    {
        PaintSurface += Paint;
        Scene.Changed += SourceChanged;
        Camera.Changed += SourceChanged;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started) { _lastPanX = _lastPanY = 0; }
            else if (args.StatusType == GestureStatus.Running)
            {
                Orbit((float)((args.TotalX - _lastPanX) * .012), (float)(-(args.TotalY - _lastPanY) * .012));
                _lastPanX = args.TotalX;
                _lastPanY = args.TotalY;
            }
        };
        GestureRecognizers.Add(pan);
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) => { if (args.Status == GestureStatus.Running) Zoom((float)args.Scale); };
        GestureRecognizers.Add(pinch);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, args) =>
        {
            var point = args.GetPosition(this);
            if (point is null) return;
            SelectedShape = PickAt(point.Value.X, point.Value.Y, Width, Height);
            SelectionChanged?.Invoke(this, SelectedShape);
        };
        GestureRecognizers.Add(tap);
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

    /// <summary>Orbit the camera by radians.</summary>
    public void Orbit(float yawDelta, float pitchDelta) => Camera.Orbit(yawDelta, pitchDelta);
    /// <summary>Zoom by an incremental factor.</summary>
    public void Zoom(float factor) => Camera.Zoom(factor);
    /// <summary>Restore the default camera for this view.</summary>
    public void ResetCamera() => Camera = new Camera();
    /// <summary>Invalidate the frame after external changes that do not raise scene or camera notifications.</summary>
    public void Refresh()
    {
        _dirty = true;
        InvalidateSurface();
    }

    /// <summary>Render or retrieve an owned frame of the requested physical pixel dimensions.</summary>
    public RenderFrame CaptureFrame(int width, int height)
    {
        if (_dirty || _frame is null || _frame.Width != width || _frame.Height != height)
        {
            var c = SceneBackgroundColor;
            if (c.Alpha < 1) throw new ArgumentException("Scene background must be opaque.", nameof(SceneBackgroundColor));
            var background = 0xFF000000u | ((uint)Math.Round(c.Red * 255) << 16) |
                ((uint)Math.Round(c.Green * 255) << 8) | (uint)Math.Round(c.Blue * 255);
            _frame = _renderer.Render(Scene, Camera, width, height, background);
            _dirty = false;
        }
        return _frame;
    }

    /// <summary>Pick at view coordinates measured from the top left. Uses the last rendered frame.</summary>
    public Shape? PickAt(double x, double y, double viewWidth, double viewHeight)
    {
        if (_frame is null || !double.IsFinite(x) || !double.IsFinite(y) ||
            viewWidth <= 0 || viewHeight <= 0 || x < 0 || y < 0 || x >= viewWidth || y >= viewHeight) return null;
        return _frame.Pick((int)(x * _frame.Width / viewWidth), (int)(y * _frame.Height / viewHeight));
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            if (_wasConnected) SetSubscriptions(false);
        }
        else
        {
            _wasConnected = true;
            SetSubscriptions(true);
            Refresh();
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
        Refresh();
    }
    private void ReplaceCamera(Camera? previous, Camera? current)
    {
        if (_subscriptionsActive)
        {
            if (previous is not null) previous.Changed -= SourceChanged;
            if (current is not null) current.Changed += SourceChanged;
        }
        Refresh();
    }
    private void SourceChanged(object? sender, EventArgs args) => Refresh();

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        var width = Math.Min(args.Info.Width, DepthRenderer.MaximumDimension);
        var height = Math.Min(args.Info.Height, DepthRenderer.MaximumDimension);
        if (width < 1 || height < 1) return;
        var frame = CaptureFrame(width, height);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        MemoryMarshal.AsBytes(frame.Pixels.Span).CopyTo(bitmap.GetPixelSpan());
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, args.Info.Width, args.Info.Height));
        if (frame.Labels.Count == 0) return;
        using var font = new SKFont(SKTypeface.Default, 14);
        using var paint = new SKPaint { IsAntialias = true };
        var sx = (float)args.Info.Width / width;
        var sy = (float)args.Info.Height / height;
        foreach (var label in frame.Labels)
        {
            paint.Color = new SKColor((byte)(label.Label.Color >> 16), (byte)(label.Label.Color >> 8), (byte)label.Label.Color);
            canvas.DrawText(label.Label.Text, label.Position.X * sx, label.Position.Y * sy, font, paint);
        }
    }
}

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Simple3D.Core;

namespace Simple3D.Maui;

/// <summary>A touch-enabled MAUI surface for small opaque 3D scenes.</summary>
public sealed class SceneView : GraphicsView
{
    private Scene _scene = new();
    private Camera _camera = new();
    private PointF? _previousTouch;

    public SceneView()
    {
        Drawable = new SceneDrawable(this);
        StartInteraction += (_, args) => _previousTouch = args.Touches.Length == 1 ? args.Touches[0] : null;
        DragInteraction += (_, args) =>
        {
            if (args.Touches.Length != 1) { _previousTouch = null; return; }
            var now = args.Touches[0];
            if (_previousTouch is { } previous)
                Orbit((now.X - previous.X) * .012f, -(now.Y - previous.Y) * .012f);
            _previousTouch = now;
        };
        EndInteraction += (_, _) => _previousTouch = null;
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) =>
        {
            // MAUI supplies an incremental factor, not a cumulative gesture scale.
            if (args.Status == GestureStatus.Running) Zoom((float)args.Scale);
        };
        GestureRecognizers.Add(pinch);
    }

    public Scene Scene
    {
        get => _scene;
        set { _scene = value ?? throw new ArgumentNullException(nameof(value)); Invalidate(); }
    }

    public void Orbit(float yawDelta, float pitchDelta) { _camera.Orbit(yawDelta, pitchDelta); Invalidate(); }
    public void Zoom(float factor) { _camera.Zoom(factor); Invalidate(); }
    public void ResetCamera() { _camera = new Camera(); Invalidate(); }
    /// <summary>Redraw after changing the contents of an existing Scene.</summary>
    public void Refresh() => Invalidate();

    private sealed class SceneDrawable(SceneView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            foreach (var triangle in SceneRenderer.Render(owner.Scene, owner._camera, dirtyRect.Width, dirtyRect.Height))
            {
                var color = triangle.Color;
                canvas.FillColor = Color.FromRgba((int)((color >> 16) & 255), (int)((color >> 8) & 255),
                    (int)(color & 255), (int)((color >> 24) & 255));
                using var path = new PathF();
                path.MoveTo(triangle.A.X, triangle.A.Y);
                path.LineTo(triangle.B.X, triangle.B.Y);
                path.LineTo(triangle.C.X, triangle.C.Y);
                path.Close();
                canvas.FillPath(path);
            }
        }
    }
}

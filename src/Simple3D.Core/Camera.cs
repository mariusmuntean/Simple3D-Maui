using System.Numerics;

namespace Simple3D.Core;

/// <summary>Camera projection model.</summary>
public enum CameraProjection
{
    /// <summary>Distance reduces apparent size.</summary>
    Perspective,
    /// <summary>Parallel projection with constant apparent size.</summary>
    Orthographic
}

/// <summary>Mutable orbit camera; angles are radians. Mutate and render on the same thread.</summary>
public sealed class Camera
{
    private Vector3 _target;
    private float _fieldOfView = .76101275f, _orthographicHeight = 4, _nearPlane = .05f;
    private CameraProjection _projection;

    /// <summary>Raised synchronously once after an effective property change or completed camera operation.</summary>
    public event EventHandler? Changed;

    /// <summary>Horizontal orbit angle in radians.</summary>
    public float Yaw { get; private set; }
    /// <summary>Vertical orbit angle, limited to avoid the poles.</summary>
    public float Pitch { get; private set; }
    /// <summary>Distance from target in world units.</summary>
    public float Distance { get; private set; }
    /// <summary>Finite orbit center.</summary>
    public Vector3 Target
    {
        get => _target;
        set { Validation.Vector(value, nameof(value)); Set(ref _target, value); }
    }
    /// <summary>Perspective or orthographic projection.</summary>
    public CameraProjection Projection
    {
        get => _projection;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref _projection, value);
        }
    }
    /// <summary>Vertical field of view in radians, strictly between .01 and 3.13.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            if (!float.IsFinite(value) || value <= .01f || value >= 3.13f)
                throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref _fieldOfView, value);
        }
    }
    /// <summary>Positive finite visible world height in orthographic mode.</summary>
    public float OrthographicHeight
    {
        get => _orthographicHeight;
        set { Positive(value); Set(ref _orthographicHeight, value); }
    }
    /// <summary>Positive finite distance of the clipping plane from the eye.</summary>
    public float NearPlane
    {
        get => _nearPlane;
        set { Positive(value); Set(ref _nearPlane, value); }
    }

    /// <summary>Creates an orbit camera with positive finite distance.</summary>
    public Camera(float distance = 5, float yaw = .55f, float pitch = .35f)
    {
        Positive(distance);
        Distance = distance;
        Orbit(yaw, pitch);
    }

    /// <summary>Adjusts orbit angles; nonfinite deltas are ignored for gesture compatibility.</summary>
    public void Orbit(float yawDelta, float pitchDelta)
    {
        var yaw = Yaw;
        var pitch = Pitch;
        if (float.IsFinite(yawDelta))
            Yaw = MathF.IEEERemainder(Yaw + MathF.IEEERemainder(yawDelta, 2 * MathF.PI), 2 * MathF.PI);
        if (float.IsFinite(pitchDelta)) Pitch = Math.Clamp(Pitch + pitchDelta, -1.45f, 1.45f);
        if (Yaw != yaw || Pitch != pitch) Notify();
    }

    /// <summary>Divides distance and orthographic height by a positive factor, clamped to .001..1e12; invalid factors are ignored.</summary>
    public void Zoom(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0 || factor == 1) return;
        var distance = Math.Clamp(Distance / factor, .001f, 1e12f);
        var height = Math.Clamp(_orthographicHeight / factor, .001f, 1e12f);
        if (Distance == distance && _orthographicHeight == height) return;
        Distance = distance;
        _orthographicHeight = height;
        Notify();
    }

    /// <summary>Fits geometry bounds using a conservative enclosing sphere. Empty scenes do nothing; aspect is width/height. Overflowing fits throw without changing the camera.</summary>
    public void FitToScene(Scene scene, float aspectRatio = 1, float padding = 1.15f)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Positive(aspectRatio);
        if (!float.IsFinite(padding) || padding < 1) throw new ArgumentOutOfRangeException(nameof(padding));
        var bounds = scene.GetBounds();
        if (bounds is null) return;
        var b = bounds.Value;
        var radius = b.Size.Length() * .5f;
        if (!float.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(scene), "Scene bounds overflow.");
        radius = MathF.Max(radius, .001f) * padding;
        var angle = MathF.Min(FieldOfView * .5f, MathF.Atan(MathF.Tan(FieldOfView * .5f) * aspectRatio));
        var distance = MathF.Max(radius / MathF.Sin(angle), radius + NearPlane * 2);
        var height = radius * 2 / MathF.Min(1, aspectRatio);
        Positive(distance);
        Positive(height);
        if (_target == b.Center && Distance == distance && _orthographicHeight == height) return;
        _target = b.Center;
        Distance = distance;
        _orthographicHeight = height;
        Notify();
    }

    internal (Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        var direction = new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch));
        var forward = -direction;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        return (Target + direction * Distance, forward, right, Vector3.Cross(right, forward));
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify();
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    private static void Positive(float value)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

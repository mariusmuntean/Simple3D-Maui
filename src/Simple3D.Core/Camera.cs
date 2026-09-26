namespace Simple3D.Core;

/// <summary>Orbit camera looking at the origin; angles are in radians.</summary>
public sealed class Camera
{
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public float Distance { get; private set; }
    public Camera(float distance = 5, float yaw = .55f, float pitch = .35f)
    {
        if (!float.IsFinite(distance) || distance <= 0) throw new ArgumentOutOfRangeException(nameof(distance));
        Distance = Math.Clamp(distance, 1.2f, 100f);
        Orbit(yaw, pitch);
    }
    public void Orbit(float yawDelta, float pitchDelta)
    {
        if (float.IsFinite(yawDelta)) Yaw = MathF.IEEERemainder(Yaw + yawDelta, 2 * MathF.PI);
        if (float.IsFinite(pitchDelta)) Pitch = Math.Clamp(Pitch + pitchDelta, -1.45f, 1.45f);
    }
    public void Zoom(float factor)
    {
        if (float.IsFinite(factor) && factor > 0) Distance = Math.Clamp(Distance / factor, 1.2f, 100f);
    }
}

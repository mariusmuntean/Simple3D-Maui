using System.Numerics;

namespace Simple3D.Core;

/// <summary>Immutable world-space light emitting parallel rays, without shadows.</summary>
public sealed class DirectionalLight
{
    /// <summary>Normalized direction in which rays travel, from the light toward the scene.</summary>
    public Vector3 Direction { get; }
    /// <summary>Opaque packed RGB light color.</summary>
    public uint Color { get; }
    /// <summary>Diffuse intensity in [0,1].</summary>
    public float Intensity { get; }
    internal Vector3 Rgb { get; }
    internal static DirectionalLight Default { get; } = new(new(.4f, -.8f, -1), intensity: .62f);

    /// <summary>Creates a light with a finite nonzero ray direction, opaque color, and intensity in [0,1].</summary>
    public DirectionalLight(Vector3 direction, uint color = 0xFFFFFFFF, float intensity = 1)
    {
        Direction = Validation.UnitVector(direction, nameof(direction));
        if (color >> 24 != 255) throw new ArgumentOutOfRangeException(nameof(color));
        Validation.UnitInterval(intensity, nameof(intensity));
        Color = color; Intensity = intensity;
        Rgb = new Vector3((color >> 16) & 255, (color >> 8) & 255, color & 255) / 255;
    }
}

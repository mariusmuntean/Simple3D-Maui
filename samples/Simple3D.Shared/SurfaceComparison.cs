using Simple3D.Core;

namespace Simple3D.Shared;

/// <summary>Switches prepared wave meshes and world-space lights without rebuilding geometry.</summary>
public sealed class SurfaceComparison
{
    // Directions are the way rays travel, so light from the right travels toward -X.
    private static readonly DirectionalLight FromRight = new(new(-.8f, -1, -.7f), intensity: .7f);
    private static readonly DirectionalLight FromLeft = new(new(.8f, -1, -.7f), intensity: .7f);
    private readonly Scene _scene;
    private readonly Shape _flat;
    private readonly Shape _smooth;

    /// <summary>Whether the wave currently uses averaged vertex normals.</summary>
    public bool IsSmooth { get; private set; } = true;
    /// <summary>Whether the light shines from the left (-X) side of the wave instead of the right.</summary>
    public bool IsLightFromLeft { get; private set; }

    internal SurfaceComparison(Scene scene, Shape flat, Shape smooth)
    {
        (_scene, _flat, _smooth) = (scene, flat, smooth);
        _scene.Light = FromRight;
    }

    /// <summary>Replaces only the wave; corresponding selection follows the replacement.</summary>
    public void SetSmoothShading(bool smooth)
    {
        if (smooth == IsSmooth) return;
        if (!_scene.Replace(IsSmooth ? _smooth : _flat, smooth ? _smooth : _flat))
            throw new InvalidOperationException("Wave surface is no longer in the scene.");
        IsSmooth = smooth;
    }

    /// <summary>Changes the light while keeping the mesh, camera and animation state.</summary>
    public void SetLightFromLeft(bool fromLeft)
    {
        if (fromLeft == IsLightFromLeft) return;
        _scene.Light = fromLeft ? FromLeft : FromRight;
        IsLightFromLeft = fromLeft;
    }

    /// <summary>Restores smooth shading and light from the right.</summary>
    public void Reset()
    {
        SetSmoothShading(true);
        SetLightFromLeft(false);
    }
}

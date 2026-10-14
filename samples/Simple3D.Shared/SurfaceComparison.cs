using Simple3D.Core;

namespace Simple3D.Shared;

/// <summary>Switches prepared wave meshes and world-space lights without rebuilding geometry.</summary>
public sealed class SurfaceComparison
{
    private static readonly DirectionalLight LeftLight = new(new(-.8f, -1, -.7f), intensity: .7f);
    private static readonly DirectionalLight RightLight = new(new(.8f, -1, -.7f), intensity: .7f);
    private readonly Scene _scene;
    private readonly Shape _flat;
    private readonly Shape _smooth;

    /// <summary>Whether the wave currently uses averaged vertex normals.</summary>
    public bool IsSmooth { get; private set; } = true;
    /// <summary>Whether the light rays travel rightward in world space.</summary>
    public bool IsLightToRight { get; private set; }

    internal SurfaceComparison(Scene scene, Shape flat, Shape smooth)
    {
        (_scene, _flat, _smooth) = (scene, flat, smooth);
        _scene.Light = LeftLight;
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
    public void SetLightToRight(bool toRight)
    {
        if (toRight == IsLightToRight) return;
        _scene.Light = toRight ? RightLight : LeftLight;
        IsLightToRight = toRight;
    }

    /// <summary>Restores smooth shading and leftward light rays.</summary>
    public void Reset()
    {
        SetSmoothShading(true);
        SetLightToRight(false);
    }
}

using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

/// <summary>A named teaching scene with its initial camera, explanation and time-based animation.</summary>
public sealed record DemoScene(string Name, string Description, Scene Scene, Camera Camera, Action<float> Animate)
{
    /// <summary>Optional prepared surface comparison, controlled by the gallery.</summary>
    public SurfaceComparison? SurfaceComparison { get; init; }
}

/// <summary>Shared scene factories for the app, examples and documentation images.</summary>
public static partial class DemoScenes
{
    /// <summary>Creates examples progressing from primitives and data updates to meshes, hierarchy and linked motion.</summary>
    public static IReadOnlyList<DemoScene> All => [.. Factories.Select(create => create())];

    /// <summary>The factories behind <see cref="All"/>, in the same order, for creating one scene at a time.</summary>
    public static IReadOnlyList<Func<DemoScene>> Factories => [Equipment, Telemetry, Surface, RobotArm, GantryCrane];

    private static Action<float> AnimateNode(Scene scene, Shape initial, Func<float, Shape> atTime)
    {
        var current = initial;
        return time =>
        {
            if (!float.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
            var next = atTime(time);
            if (!scene.Replace(current, next)) throw new InvalidOperationException("Animated shape is no longer in the scene.");
            current = next;
        };
    }

    /// <summary>The same color with its hue turned 180° and its lightness and saturation kept (HSL).</summary>
    internal static uint OppositeHue(uint color)
    {
        uint r = (color >> 16) & 255, g = (color >> 8) & 255, b = color & 255;
        var sum = Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b));
        return (color & 0xFF000000) | ((sum - r) << 16) | ((sum - g) << 8) | (sum - b);
    }

    private static uint Mix(uint start, uint end, float fraction)
    {
        var red = (uint)MathF.Round(((start >> 16) & 255) * (1 - fraction) + ((end >> 16) & 255) * fraction);
        var green = (uint)MathF.Round(((start >> 8) & 255) * (1 - fraction) + ((end >> 8) & 255) * fraction);
        var blue = (uint)MathF.Round((start & 255) * (1 - fraction) + (end & 255) * fraction);
        return 0xFF000000 | (red << 16) | (green << 8) | blue;
    }
}

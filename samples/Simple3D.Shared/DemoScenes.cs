using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

/// <summary>A named teaching scene with its initial camera, explanation and time-based animation.</summary>
public sealed record DemoScene(string Name, string Description, Scene Scene, Camera Camera, Action<float> Animate);

/// <summary>Shared scene factories for the app, examples and documentation images.</summary>
public static partial class DemoScenes
{
    /// <summary>Creates independent examples of product, scientific, data and spatial illustrations.</summary>
    public static IReadOnlyList<DemoScene> All =>
        [Equipment(), RobotArm(), Telemetry(), Surface(), GantryCrane()];

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

    private static uint Mix(uint start, uint end, float fraction)
    {
        var red = (uint)MathF.Round(((start >> 16) & 255) * (1 - fraction) + ((end >> 16) & 255) * fraction);
        var green = (uint)MathF.Round(((start >> 8) & 255) * (1 - fraction) + ((end >> 8) & 255) * fraction);
        var blue = (uint)MathF.Round((start & 255) * (1 - fraction) + (end & 255) * fraction);
        return 0xFF000000 | (red << 16) | (green << 8) | blue;
    }
}

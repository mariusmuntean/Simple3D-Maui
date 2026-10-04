using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
    /// <summary>Builds an orthographic three-dimensional bar chart with named data points.</summary>
    public static DemoScene Telemetry()
    {
        var scene = new Scene();
        var values = new[] { .9f, 1.45f, 2.05f, 1.7f, 1.15f };
        Shape BarsAt(float time)
        {
            var bars = new Shape[values.Length];
            for (var i = 0; i < bars.Length; i++)
            {
                var height = Math.Clamp(values[i] + .45f * MathF.Sin(time * 2.1f + i * .6f), .35f, 2.5f);
                var color = height < 1f
                    ? Mix(0xFF80B2FF, 0xFF76DBC7, Math.Clamp((height - .35f) / .65f, 0, 1))
                    : Mix(0xFF76DBC7, 0xFFFFA66F, Math.Clamp((height - 1.65f) / .85f, 0, 1));
                bars[i] = Shape.Box(color).Named($"Sample {i + 1}: {height:0.00}")
                    .Scaled(.56f, height, .56f).At((i - 2) * .78f, height / 2, 0);
            }
            return Shape.Group(bars).Named("Synthetic samples");
        }
        var barGroup = BarsAt(0);
        scene.Add(barGroup);
        scene.Add(Shape.Box(0xFF526B9A).Named("Baseline").Scaled(4.1f, .08f, 1.15f).At(0, -.08f, 0));
        scene.AddLabel(new("5 SAMPLES / 0–2.5", new(-1.7f, 2.75f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .45f, .32f) { Projection = CameraProjection.Orthographic };
        camera.FitToScene(scene, 4f / 3);
        return new("Telemetry", "Five synthetic samples mapped to bar height and color in an orthographic chart.", scene, camera,
            AnimateNode(scene, barGroup, BarsAt));
    }

}

using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
    /// <summary>Builds a labeled tabletop equipment illustration with grouped parts.</summary>
    public static DemoScene Equipment()
    {
        var scene = new Scene();
        var basePlate = Shape.Box(0xFF607AB1).Named("Base plate").Scaled(2.8f, .16f, 1.8f).At(0, -.8f, 0);
        var housing = Shape.Cylinder(0xFF8DA9FF).Named("Motor housing").Scaled(.65f, 1.25f, .65f).At(-.4f, -.1f, 0);
        var cap = Shape.Sphere(0xFFFFBE79).Named("Inspection cap").Scaled(.34f).At(-.4f, .67f, 0);
        scene.Add(Shape.Group(basePlate, housing, cap).Named("Drive assembly"));
        var axis = Shape.Arrow(Vector3.Zero, new(.9f, .75f, 0), .09f, 0xFF76DBC7)
            .Named("Output axis").At(.55f, -.2f, 0);
        scene.Add(axis);
        scene.AddLabel(new("MOTOR", new(-.4f, 1.15f, 0), 0xFFE4ECFF));
        scene.AddLabel(new("OUTPUT", new(1.2f, .9f, 0), 0xFFE4ECFF));
        var camera = new Camera(5.5f, .55f, .32f);
        camera.FitToScene(scene, 4f / 3);
        return new("Equipment", "Grouped primitives, labels and an animated output vector.", scene, camera,
            AnimateNode(scene, axis, time => axis.Rotated(0, .42f * MathF.Sin(time * 1.7f), 0)));
    }

}

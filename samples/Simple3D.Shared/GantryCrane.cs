using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
    /// <summary>Builds a gantry with a moving trolley and a variable-length hoist attached to its load.</summary>
    public static DemoScene GantryCrane()
    {
        var structure = new List<Shape>
        {
            Shape.Box(0xFF526B9A).Named("Crane floor").Scaled(3.8f, .1f, 2.4f).At(0, -.82f, 0)
        };
        for (var i = 0; i < 4; i++)
            structure.Add(Shape.Box(0xFF7894C8).Named($"Gantry column {i + 1}").Scaled(.18f, 2.3f, .18f)
                .At((i % 2 == 0 ? -1 : 1) * 1.55f, .35f, (i / 2 == 0 ? -1 : 1) * .85f));
        for (var i = 0; i < 2; i++)
            structure.Add(Shape.Box(0xFF8DA9FF).Named($"Runway rail {i + 1}").Scaled(3.5f, .2f, .18f)
                .At(0, 1.58f, (i == 0 ? -1 : 1) * .85f));
        var bridge = Shape.Box(0xFFFFBE79).Named("Moving bridge").Scaled(.35f, .2f, 1.9f).At(0, 1.7f, 0);
        var trolley = Shape.Box(0xFF76DBC7).Named("Hoist trolley").Scaled(.5f, .3f, .5f).At(0, 1.45f, 0);
        var cable = Shape.Cylinder(0xFFB7C6E2).Named("Hoist cable");
        var load = Shape.Box(0xFFFFC18B).Named("Carried load").Scaled(.52f, .44f, .52f);
        Shape TrolleyAt(float time)
        {
            var lift = -.45f + .65f * (.5f - .5f * MathF.Cos(time * 1.1f));
            var cableBottom = lift + .22f;
            var length = 1.3f - cableBottom;
            return Shape.Group(bridge, trolley,
                cable.Scaled(.035f, length, .035f).At(0, (1.3f + cableBottom) / 2, 0),
                load.At(0, lift, 0)).Named("Crane carriage").At(.95f * MathF.Sin(time * .75f), 0, 0);
        }
        var carriage = TrolleyAt(0);
        var scene = new Scene().Add(Shape.Group(structure.ToArray()).Named("Gantry structure")).Add(carriage);
        scene.AddLabel(new("GANTRY / TRAVERSE + HOIST", new(-1.6f, 2.12f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .5f, .4f);
        camera.FitToScene(scene, 4f / 3);
        return new("Gantry Crane", "Inspect a moving bridge and trolley. The hoist raises and lowers a load while its cable stays attached.",
            scene, camera, AnimateNode(scene, carriage, TrolleyAt));
    }

}

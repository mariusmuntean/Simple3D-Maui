using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
    /// <summary>Builds a labeled tabletop equipment illustration with grouped parts.</summary>
    public static DemoScene Equipment()
    {
        var scene = new Scene();
        var basePlate = Shape.Box(0xFFB19760).Named("Base plate").Scaled(3.2f, .16f, 1.8f).At(0, -.8f, 0);
        var housing = Shape.Cylinder(0xFFFFE38D).Named("Motor housing").Scaled(.65f, 1.25f, .65f).At(-.75f, -.1f, 0);
        var cap = Shape.Sphere(0xFF79BAFF).Named("Inspection cap").Scaled(.34f).At(-.75f, .67f, 0);
        // This wedge enclosure has an independent UV layout for its front and solid sides.
        var controllerMesh = ObjReader.Parse("""
            v -0.55 -0.55 0.5
            v 0.55 -0.55 0.5
            v 0.55 0.55 0.5
            v -0.55 0.55 0.5
            v -0.55 -0.55 -0.5
            v 0.55 -0.55 -0.5
            v 0.55 0.75 -0.5
            v -0.55 0.75 -0.5
            vt 0.125 0.125
            vt 0.875 0.125
            vt 0.875 0.875
            vt 0.125 0.875
            vt 0.02 0.02
            f 1/1 2/2 3/3 4/4
            f 2/5 6/5 7/5 3/5
            f 6/5 5/5 8/5 7/5
            f 5/5 1/5 4/5 8/5
            f 4/5 3/5 7/5 8/5
            f 5/5 6/5 2/5 1/5
            """);
        // Keep this shared Core sample independent of image codecs. In MAUI, an image
        // can supply the same material through TextureLoader.Load, once at startup.
        var panelTexture = new Texture2D(96, 96, Enumerable.Range(0, 96 * 96).Select(i =>
        {
            var x = i % 96; var y = i / 96;
            if (x is < 12 or > 83 || y is < 12 or > 83) return 0xFFB19760u;
            if (y is >= 14 and <= 18) return 0xFF79BAFFu;
            if (x is >= 19 and <= 76 && y is >= 25 and <= 59)
            {
                var trace = 47 - (int)(9 * MathF.Sin((x - 19) * .18f));
                if (Math.Abs(y - trace) <= 1) return 0xFFDB768Au;
                if (x % 12 == 0 || y % 10 == 0) return 0xFF6C5630u;
                return 0xFF221C0Du;
            }
            if (x is >= 21 and <= 29 && y is >= 68 and <= 76) return 0xFFDB768Au;
            if (x is >= 37 and <= 72 && (y is 69 or 73)) return 0xFFFFE8B7u;
            return 0xFF3B2F18u;
        }));
        var controller = Shape.FromMesh(controllerMesh, new Material(panelTexture))
            .Named("Imported controller").Scaled(1.1f, .95f, .8f).At(.65f, -.2f, .25f);
        scene.Add(Shape.Group(basePlate, housing, cap, controller).Named("Drive assembly"));
        var axis = Shape.Arrow(Vector3.Zero, new(.9f, .75f, 0), .09f, 0xFFDB768A)
            .Named("Output axis").At(.25f, .65f, -.3f);
        scene.Add(axis);
        scene.AddLabel(new("MOTOR", new(-.75f, 1.15f, 0), 0xFFFFF7E4));
        scene.AddLabel(new("TEXTURE · OBJ", new(.65f, .55f, .65f), 0xFFFFF7E4));
        scene.AddLabel(new("OUTPUT", new(1.15f, 1.55f, -.3f), 0xFFFFF7E4));
        var camera = new Camera(5.5f, .55f, .32f);
        camera.FitToScene(scene, 4f / 3);
        return new("Equipment", "Orbit the imported controller to see its mapped display, then animate the output vector.", scene, camera,
            AnimateNode(scene, axis, time => axis.Rotated(0, .42f * MathF.Sin(time * 1.7f), 0)));
    }

}

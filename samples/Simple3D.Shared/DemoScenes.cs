using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

/// <summary>A named teaching scene with its initial camera, explanation and time-based animation.</summary>
public sealed record DemoScene(string Name, string Description, Scene Scene, Camera Camera, Action<float> Animate);

/// <summary>Shared scene factories for the app, examples and documentation images.</summary>
public static class DemoScenes
{
    /// <summary>Creates independent examples of product, scientific, data and spatial illustrations.</summary>
    public static IReadOnlyList<DemoScene> All =>
        [Equipment(), Packing(), Surface(), Assembly(), Molecule(), Telemetry(), City()];

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
        return new("Equipment", "Inspect the motor and animate its output vector.", scene, camera,
            AnimateNode(scene, axis, time => axis.Rotated(0, .42f * MathF.Sin(time * 1.7f), 0)));
    }

    /// <summary>Builds an orthographic packing arrangement with nested transforms.</summary>
    public static DemoScene Packing()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Tray").Scaled(3.5f, .12f, 2.5f).At(0, -.75f, 0));
        var packages = new List<Shape>();
        for (var row = 0; row < 2; row++)
            for (var col = 0; col < 3; col++)
            {
                var color = (row + col) % 2 == 0 ? 0xFF80B2FFu : 0xFFFFC18Bu;
                var item = Shape.Box(color).Named($"Package {row * 3 + col + 1}").Scaled(.82f, .72f, .92f)
                    .At((col - 1) * 1.03f, -.32f, (row - .5f) * 1.13f);
                packages.Add(item);
            }
        var packageGroup = Shape.Group(packages.ToArray()).Named("Packages");
        scene.Add(packageGroup);
        scene.AddLabel(new("3 × 2 PACKING GRID", new(0, .55f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .45f, .7f) { Projection = CameraProjection.Orthographic };
        camera.FitToScene(scene, 4f / 3);
        return new("Packing", "An orthographic tray with six packages that lift and settle.", scene, camera,
            AnimateNode(scene, packageGroup, time => Shape.Group(packages.Select((item, i) =>
            {
                var lift = MathF.Max(0, .45f * (MathF.Sin(time * 2.4f + i * .7f) - MathF.Sin(i * .7f)));
                return item.At(item.Position.X, item.Position.Y + lift, item.Position.Z);
            }).ToArray()).Named("Packages")));
    }

    /// <summary>Builds a wave from an indexed mesh and adds measurement markers.</summary>
    public static DemoScene Surface()
    {
        const int cells = 16;
        var vertices = new List<Vector3>((cells + 1) * (cells + 1));
        var indices = new List<int>(cells * cells * 6);
        for (var z = 0; z <= cells; z++)
            for (var x = 0; x <= cells; x++)
            {
                var px = (x - cells / 2f) * .18f;
                var pz = (z - cells / 2f) * .18f;
                vertices.Add(new(px, .38f * MathF.Sin(px * 2.5f) * MathF.Cos(pz * 2.2f), pz));
            }
        for (var z = 0; z < cells; z++)
            for (var x = 0; x < cells; x++)
            {
                var a = z * (cells + 1) + x;
                indices.AddRange([a, a + cells + 1, a + 1, a + 1, a + cells + 1, a + cells + 2]);
            }
        var mesh = new Mesh(vertices, indices);
        var scene = new Scene().Add(Shape.FromMesh(mesh, new Material(0xFF8BAAFF)).Named("Wave surface"));
        var samplePoint = Shape.Sphere(0xFFFFBE79).Named("Sample point").Scaled(.13f).At(0, .13f, 0);
        scene.Add(samplePoint);
        scene.AddLabel(new("INDEXED MESH · 512 TRIANGLES", new(0, .8f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .55f, .65f);
        camera.FitToScene(scene, 4f / 3);
        return new("Surface", "Follow a sample point across a procedural wave mesh.", scene, camera,
            AnimateNode(scene, samplePoint, time =>
            {
                var x = 1.15f * MathF.Sin(time * 1.25f);
                var z = .7f * MathF.Sin(time * .73f);
                var y = .38f * MathF.Sin(x * 2.5f) * MathF.Cos(z * 2.2f) + .13f;
                return samplePoint.At(x, y, z);
            }));
    }

    /// <summary>Builds a layered camera module from individually pickable parts.</summary>
    public static DemoScene Assembly()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF7894C8).Named("Camera body").Scaled(2.4f, 1.45f, .8f));
        scene.Add(Shape.Cylinder(0xFF96B7EE).Named("Lens barrel").Scaled(1.2f, .65f, 1.2f)
            .Rotated(MathF.PI / 2, 0, 0).At(0, 0, 1));
        var frontElement = Shape.Cylinder(0xFF72DCCB).Named("Front element").Scaled(.9f, .1f, .9f)
            .Rotated(MathF.PI / 2, 0, 0).At(0, 0, 1.75f);
        scene.Add(frontElement);
        scene.Add(Shape.Box(0xFFFFBC78).Named("Top control").Scaled(.48f, .22f, .48f).At(.7f, .84f, 0));
        scene.AddLabel(new("BODY / LENS / CONTROL", new(-1.15f, 1.32f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .6f, .3f);
        camera.FitToScene(scene, 4f / 3);
        return new("Assembly", "Watch the front element travel as the camera focuses.", scene, camera,
            AnimateNode(scene, frontElement, time => frontElement.At(0, 0, 1.75f + .28f * (1 - MathF.Cos(time * 2.1f)) / 2)));
    }

    /// <summary>Builds a compact methane model with atoms and bonds as separate shapes.</summary>
    public static DemoScene Molecule()
    {
        var scene = new Scene();
        var atoms = new[]
        {
            new Vector3(1, 1, 1), new Vector3(-1, -1, 1),
            new Vector3(-1, 1, -1), new Vector3(1, -1, -1)
        };
        var parts = new List<Shape>();
        foreach (var atom in atoms)
        {
            var position = atom * .85f;
            parts.Add(Shape.Line(Vector3.Zero, position, .12f, 0xFF7894C8).Named("C–H bond"));
            parts.Add(Shape.Sphere(0xFFDDE9FF).Named("Hydrogen").Scaled(.32f)
                .At(position.X, position.Y, position.Z));
        }
        parts.Add(Shape.Sphere(0xFF76DBC7).Named("Carbon").Scaled(.58f));
        var molecule = Shape.Group(parts.ToArray()).Named("Methane");
        scene.Add(molecule);
        scene.AddLabel(new("CH₄ / TETRAHEDRAL", new(-1.15f, 1.45f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .45f, .28f);
        camera.FitToScene(scene, 4f / 3);
        return new("Molecule", "Rotate a pickable methane model to see its tetrahedral form.", scene, camera,
            AnimateNode(scene, molecule, time => molecule.Rotated(.12f * MathF.Sin(time * .8f), time * .7f, 0)));
    }

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
            return Shape.Group(bars).Named("Live samples");
        }
        var barGroup = BarsAt(0);
        scene.Add(barGroup);
        scene.Add(Shape.Box(0xFF526B9A).Named("Baseline").Scaled(4.1f, .08f, 1.15f).At(0, -.08f, 0));
        scene.AddLabel(new("5 SAMPLES / 0–2.5", new(-1.7f, 2.75f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .45f, .32f) { Projection = CameraProjection.Orthographic };
        camera.FitToScene(scene, 4f / 3);
        return new("Telemetry", "Watch five live bars change height and color within their scale.", scene, camera,
            AnimateNode(scene, barGroup, BarsAt));
    }

    /// <summary>Builds a small architectural massing study with selectable buildings.</summary>
    public static DemoScene City()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Site").Scaled(3.8f, .1f, 3.2f).At(0, -.12f, 0));
        var heights = new[,] { { 1.0f, 1.5f, .8f }, { 1.35f, 2.2f, 1.6f }, { .7f, 1.15f, 1.4f } };
        var buildings = new List<Shape>();
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var height = heights[row, column];
                var color = row == 1 && column == 1 ? 0xFFFFBC78u :
                    (row + column) % 2 == 0 ? 0xFF8DA9FFu : 0xFF76DBC7u;
                buildings.Add(Shape.Box(color).Named($"Block {row * 3 + column + 1}")
                    .Scaled(.72f, height, .72f).At((column - 1) * 1.08f, height / 2, (row - 1) * .94f));
            }
        var city = Shape.Group(buildings.ToArray()).Named("Buildings");
        scene.Add(city);
        scene.AddLabel(new("NINE BLOCKS", new(-1.35f, 2.6f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .64f, .46f);
        camera.FitToScene(scene, 4f / 3);
        return new("City", "Watch nine building masses rise and settle in sequence.", scene, camera,
            AnimateNode(scene, city, time => Shape.Group(buildings.Select((building, i) =>
            {
                var factor = 1 - .55f * MathF.Max(0, MathF.Sin(time * 1.5f)) *
                    (.5f + .5f * MathF.Sin(i * .65f + time * .5f));
                var height = building.Size.Y * factor;
                return building.Scaled(building.Size.X, height, building.Size.Z)
                    .At(building.Position.X, height / 2, building.Position.Z);
            }).ToArray()).Named("Buildings")));
    }

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

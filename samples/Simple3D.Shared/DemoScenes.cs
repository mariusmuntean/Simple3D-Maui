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
        [Equipment(), Packing(), Surface(), Assembly(), Molecule(), Telemetry(), City(), RobotArm(), Orbit(), Wind(), ConveyorInspection(), SolarTracker(), PacketRouting(), DroneSurvey(), PatternedSurface(), BatteryStorage(), GantryCrane()];

    /// <summary>Builds six battery modules with bounded, independently changing charge gauges.</summary>
    public static DemoScene BatteryStorage()
    {
        var cabinets = new List<Shape>
        {
            Shape.Box(0xFF526B9A).Named("Battery plinth").Scaled(3.6f, .12f, 1.2f).At(0, -.72f, 0)
        };
        var gauges = new Shape[6];
        for (var i = 0; i < gauges.Length; i++)
        {
            var x = (i % 3 - 1) * 1.1f;
            var y = i / 3 * 1.7f;
            cabinets.Add(Shape.Box(0xFF7894C8).Named($"Module {i + 1} cabinet")
                .Scaled(.85f, 1.5f, .7f).At(x, .1f + y, 0));
            cabinets.Add(Shape.Box(0xFF162238).Named($"Module {i + 1} gauge scale")
                .Scaled(.62f, 1.1f, .035f).At(x, .05f + y, .37f));
            cabinets.Add(Shape.Box(0xFFB7C6E2).Named($"Module {i + 1} terminal")
                .Scaled(.35f, .08f, .28f).At(x, .89f + y, 0));
            gauges[i] = Shape.Box().Named($"Charge {i + 1}").At(x, -.5f + y, .41f);
        }
        Shape ChargeAt(float time)
        {
            var charges = new Shape[gauges.Length];
            for (var i = 0; i < charges.Length; i++)
            {
                var level = .12f + .78f * (.5f + .5f * MathF.Sin(time * .9f + i * .9f));
                var color = level < .5f
                    ? Mix(0xFFFF927B, 0xFFFFD27A, (level - .12f) / .38f)
                    : Mix(0xFFFFD27A, 0xFF76DBC7, (level - .5f) / .4f);
                var height = 1.1f * level;
                charges[i] = gauges[i].WithMaterial(new Material(color, lit: false))
                    .Scaled(.48f, height, .035f).At(gauges[i].Position.X, gauges[i].Position.Y + height / 2, gauges[i].Position.Z);
            }
            return Shape.Group(charges).Named("Module charges");
        }
        var charge = ChargeAt(0);
        var scene = new Scene().Add(Shape.Group(cabinets.ToArray()).Named("Battery cabinets")).Add(charge);
        scene.AddLabel(new("ENERGY STORAGE / CHARGE STATE", new(-1.4f, 2.95f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .4f, .25f);
        camera.FitToScene(scene, 4f / 3);
        return new("Battery Storage", "Six modules charge and discharge. Gauge height and color show synthetic charge state within a fixed scale.",
            scene, camera, AnimateNode(scene, charge, ChargeAt));
    }

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

    /// <summary>Builds a curved checker mesh with fixed directional lighting and individually selectable cells.</summary>
    public static DemoScene PatternedSurface()
    {
        const int cells = 8;
        var parts = new List<Shape>();
        for (var row = 0; row < cells; row++)
            for (var col = 0; col < cells; col++)
            {
                var x = (col - cells / 2f) * .35f;
                var z = (row - cells / 2f) * .35f;
                var mesh = new Mesh([Point(x, z), Point(x + .35f, z), Point(x, z + .35f), Point(x + .35f, z + .35f)],
                    [0, 2, 1, 1, 2, 3]);
                parts.Add(Shape.FromMesh(mesh, new Material((row + col) % 2 == 0 ? 0xFFECCB95u : 0xFF396E78u))
                    .Named($"Cell {row + 1}, {col + 1}"));
            }
        var panel = Shape.Group(parts.ToArray()).Named("Checker surface");
        var scene = new Scene().Add(panel)
            .Add(Shape.Box(0xFF526B9A).Scaled(3.25f, .15f, 3.25f).At(0, -.65f, 0).Named("Display plinth"));
        scene.AddLabel(new("MESH PATTERN · DIRECTIONAL LIGHT", new(0, .95f, 0), 0xFFE4ECFF));
        var camera = new Camera(5.5f, .5f, .7f);
        camera.FitToScene(scene, 4f / 3);
        return new("Patterned Surface", "Tilt a checker mesh to inspect fixed directional lighting. Each cell is selectable; no image texture mapping.",
            scene, camera, AnimateNode(scene, panel, time => panel.Rotated(.28f * MathF.Sin(time * 1.3f), 0, .18f * MathF.Sin(time))));

        static Vector3 Point(float x, float z) => new(x, .22f * MathF.Cos(x * 1.3f) * MathF.Cos(z * 1.3f), z);
    }

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

    /// <summary>Builds a two-joint robot arm with independently moving links.</summary>
    public static DemoScene RobotArm()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Workcell").Scaled(2.8f, .1f, 1.8f).At(0, -1.06f, 0));
        scene.Add(Shape.Cylinder(0xFF7894C8).Named("Base").Scaled(.72f, .28f, .72f).At(-.35f, -.86f, 0));
        var shoulder = Shape.Sphere(0xFFFFBE79).Named("Shoulder").Scaled(.24f);
        var upperLink = Shape.Box(0xFF7894C8).Named("Upper link").Scaled(.32f, 1.08f, .32f).At(0, .54f, 0);
        var elbowBase = Shape.Group(
            Shape.Sphere(0xFFFFBE79).Named("Elbow").Scaled(.22f),
            Shape.Box(0xFF8DA9FF).Named("Forearm").Scaled(.26f, .9f, .26f).At(0, .45f, 0),
            Shape.Box(0xFF76DBC7).Named("Gripper").Scaled(.46f, .14f, .28f).At(0, .96f, 0))
            .At(0, 1.08f, 0);
        Shape ArmAt(float time)
        {
            var shoulderAngle = .24f + .3f * MathF.Sin(time * 1.5f);
            var elbowAngle = -.48f - .42f * MathF.Sin(time * 2.2f);
            return Shape.Group(shoulder, upperLink, elbowBase.Rotated(0, 0, elbowAngle))
                .Named("Armature").At(-.35f, -.65f, 0).Rotated(0, 0, shoulderAngle);
        }
        var arm = ArmAt(0);
        scene.Add(arm);
        scene.AddLabel(new("TWO JOINTS / PICK A LINK", new(-1.3f, 1.65f, 0), 0xFFE4ECFF));
        var camera = new Camera(5, .55f, .25f);
        camera.FitToScene(scene, 4f / 3);
        return new("Robot Arm", "Inspect linked parts as the shoulder and elbow articulate.", scene, camera,
            AnimateNode(scene, arm, ArmAt));
    }

    /// <summary>Builds a simple orbital model with a planet and its moon.</summary>
    public static DemoScene Orbit()
    {
        var scene = new Scene();
        const float radius = 1.55f;
        scene.Add(Shape.Sphere(0xFFFFC18B).Named("Star").Scaled(.78f));
        var path = new Shape[40];
        for (var i = 0; i < path.Length; i++)
        {
            var a = i * 2 * MathF.PI / path.Length;
            var b = (i + 1) * 2 * MathF.PI / path.Length;
            path[i] = Shape.Line(new(radius * MathF.Cos(a), 0, radius * MathF.Sin(a)),
                new(radius * MathF.Cos(b), 0, radius * MathF.Sin(b)), .03f, 0xFF526B9A).Named("Orbit path");
        }
        scene.Add(Shape.Group(path).Named("Orbit path"));
        var planet = Shape.Sphere(0xFF80B2FF).Named("Planet").Scaled(.42f);
        var moon = Shape.Sphere(0xFFDDE9FF).Named("Moon").Scaled(.16f);
        Shape BodiesAt(float time)
        {
            var moonAngle = time * 2.1f;
            var planetSystem = Shape.Group(
                planet, moon.At(.48f * MathF.Cos(moonAngle), .12f * MathF.Sin(moonAngle), .48f * MathF.Sin(moonAngle)))
                .Named("Planet system").At(radius, 0, 0);
            return Shape.Group(planetSystem).Named("Orbital bodies").Rotated(0, time * .75f, 0);
        }
        var bodies = BodiesAt(0);
        scene.Add(bodies);
        scene.AddLabel(new("ORBIT / PLANET + MOON", new(-1.4f, 1.1f, 0), 0xFFE4ECFF));
        var camera = new Camera(6, .55f, .72f);
        camera.FitToScene(scene, 4f / 3);
        return new("Orbit", "Follow a planet and moon around a fixed star.", scene, camera,
            AnimateNode(scene, bodies, BodiesAt));
    }

    /// <summary>Builds a wind turbine whose three pickable blades rotate around the hub.</summary>
    public static DemoScene Wind()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Foundation").Scaled(2.5f, .12f, 1.7f).At(0, -1.05f, 0));
        scene.Add(Shape.Cylinder(0xFF8DA9FF).Named("Tower").Scaled(.18f, 2f, .18f).At(0, 0, 0));
        scene.Add(Shape.Box(0xFF7894C8).Named("Nacelle").Scaled(.5f, .26f, .42f).At(0, 1.12f, 0));
        var blades = new Shape[4];
        blades[0] = Shape.Sphere(0xFFFFBE79).Named("Hub").Scaled(.23f);
        for (var i = 0; i < 3; i++)
            blades[i + 1] = Shape.Group(Shape.Box(0xFFDDE9FF).Named($"Blade {i + 1}")
                .Scaled(.14f, .92f, .07f).At(0, .55f, 0))
                .Rotated(0, 0, i * 2 * MathF.PI / 3);
        var rotorBase = Shape.Group(blades).Named("Rotor").At(0, 1.12f, .27f);
        Shape RotorAt(float time) => rotorBase.Rotated(0, 0, -time * 2.4f);
        var rotor = RotorAt(0);
        scene.Add(rotor);
        scene.AddLabel(new("WIND / THREE BLADES", new(-1.1f, 1.65f, 0), 0xFFE4ECFF));
        var camera = new Camera(5.5f, .45f, .22f);
        camera.FitToScene(scene, 4f / 3);
        return new("Wind", "Watch three selectable blades turn around a fixed turbine.", scene, camera,
            AnimateNode(scene, rotor, RotorAt));
    }

    /// <summary>Builds an inspection conveyor with parcels that change status at a fixed scan gate.</summary>
    public static DemoScene ConveyorInspection()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Conveyor belt").Scaled(3.5f, .12f, 1.05f).At(0, -.55f, 0));
        scene.Add(Shape.Group(
            Shape.Box(0xFF8DA9FF).Named("Left post").Scaled(.12f, 1.65f, .12f).At(0, .28f, -.62f),
            Shape.Box(0xFF8DA9FF).Named("Right post").Scaled(.12f, 1.65f, .12f).At(0, .28f, .62f),
            Shape.Box(0xFF76DBC7).Named("Scanner head").Scaled(.22f, .16f, 1.35f).At(0, 1.1f, 0))
            .Named("Scan gate"));
        var pendingParcels = new Shape[3];
        var passedParcels = new Shape[3];
        for (var i = 0; i < pendingParcels.Length; i++)
        {
            pendingParcels[i] = Shape.Box(0xFFFFBE79).Named($"Parcel {i + 1}: pending").Scaled(.43f);
            passedParcels[i] = pendingParcels[i].WithMaterial(new Material(0xFF76DBC7)).Named($"Parcel {i + 1}: passed");
        }
        Shape ParcelsAt(float time)
        {
            var parcels = new Shape[3];
            for (var i = 0; i < parcels.Length; i++)
            {
                var x = -1.25f + (time * 1.2f + i * .85f) % 2.5f;
                var passed = x >= 0;
                parcels[i] = (passed ? passedParcels[i] : pendingParcels[i]).At(x, -.27f, 0);
            }
            return Shape.Group(parcels).Named("Inspected parcels");
        }
        var parcels = ParcelsAt(0);
        scene.Add(parcels);
        scene.AddLabel(new("CONVEYOR / SCAN + SORT", new(-1.65f, 1.48f, 0), 0xFFE4ECFF));
        var camera = new Camera(6f, .5f, .36f);
        camera.FitToScene(scene, 4f / 3);
        return new("Conveyor Inspection", "Follow parcels through a fixed scan gate and inspect their pass state.",
            scene, camera, AnimateNode(scene, parcels, ParcelsAt));
    }

    /// <summary>Builds a solar array that tilts toward a moving sun while its mount stays fixed.</summary>
    public static DemoScene SolarTracker()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Solar site").Scaled(3.8f, .1f, 2.15f).At(0, -.8f, 0));
        scene.Add(Shape.Cylinder(0xFF8DA9FF).Named("Tracker mount").Scaled(.2f, 1.1f, .2f).At(0, -.2f, 0));
        var panelParts = new List<Shape>
        {
            Shape.Box(0xFF7894C8).Named("Solar panel").Scaled(2.45f, .12f, 1.4f)
        };
        for (var row = 0; row < 2; row++)
            for (var column = 0; column < 4; column++)
                panelParts.Add(Shape.Box(0xFF80B2FF).Named($"Cell {row * 4 + column + 1}")
                    .Scaled(.54f, .015f, .6f).At((column - 1.5f) * .59f, .07f, (row - .5f) * .67f));
        var panelBase = Shape.Group(panelParts.ToArray()).Named("Panel assembly").At(0, .35f, 0);
        var sunBase = Shape.Sphere(0xFFFFBE79).Named("Sun").Scaled(.32f);
        Shape TrackingAt(float time)
        {
            var angle = .45f * MathF.Sin(time * .9f);
            var panel = panelBase.Rotated(0, 0, -angle);
            var sun = sunBase
                .At(1.55f * MathF.Sin(time * .9f), 1.8f - .18f * (1 - MathF.Cos(time * .9f)), 0);
            return Shape.Group(panel, sun).Named("Tracking elements");
        }
        var tracking = TrackingAt(0);
        scene.Add(tracking);
        scene.AddLabel(new("SOLAR / FOLLOW THE SUN", new(-1.65f, 2.35f, 0), 0xFFE4ECFF));
        var camera = new Camera(6f, .5f, .32f);
        camera.FitToScene(scene, 4f / 3);
        return new("Solar Tracker", "Watch the panel tilt as the sun follows its path above the fixed mount.",
            scene, camera, AnimateNode(scene, tracking, TrackingAt));
    }

    /// <summary>Builds a routed network with packets moving between fixed, selectable devices.</summary>
    public static DemoScene PacketRouting()
    {
        var scene = new Scene();
        var source = new Vector3(-1.35f, .5f, -.55f);
        var router = new Vector3(0, .5f, .45f);
        var destination = new Vector3(1.35f, .5f, -.55f);
        scene.Add(Shape.Box(0xFF526B9A).Named("Network floor").Scaled(3.9f, .1f, 2.4f).At(0, -.4f, 0));
        scene.Add(Shape.Box(0xFF8DA9FF).Named("Source rack").Scaled(.56f, .8f, .5f).At(source.X, .05f, source.Z));
        scene.Add(Shape.Box(0xFF76DBC7).Named("Router").Scaled(.66f, .8f, .65f).At(router.X, .05f, router.Z));
        scene.Add(Shape.Box(0xFFFFC18B).Named("Destination rack").Scaled(.56f, .8f, .5f)
            .At(destination.X, .05f, destination.Z));
        scene.Add(Shape.Line(source, router, .045f, 0xFF80B2FF).Named("Source link"));
        scene.Add(Shape.Line(router, destination, .045f, 0xFF76DBC7).Named("Destination link"));
        var packetParts = Enumerable.Range(0, 3)
            .Select(i => Shape.Sphere(0xFFFFE19B).Named($"Packet {i + 1}").Scaled(.15f)).ToArray();
        Shape PacketsAt(float time)
        {
            var packets = new Shape[3];
            for (var i = 0; i < packets.Length; i++)
            {
                var progress = (time * .7f + i * .55f) % 2f;
                var position = progress < 1f
                    ? Vector3.Lerp(source, router, progress)
                    : Vector3.Lerp(router, destination, progress - 1f);
                packets[i] = packetParts[i].At(position.X, position.Y, position.Z);
            }
            return Shape.Group(packets).Named("Routed packets");
        }
        var packets = PacketsAt(0);
        scene.Add(packets);
        scene.AddLabel(new("PACKET ROUTING / TWO HOPS", new(-1.75f, 1.3f, 0), 0xFFE4ECFF));
        var camera = new Camera(6.2f, .53f, .52f);
        camera.FitToScene(scene, 4f / 3);
        return new("Packet Routing", "Follow named packets from a source rack through a router to a destination.",
            scene, camera, AnimateNode(scene, packets, PacketsAt));
    }

    /// <summary>Builds a survey drone with four spinning propellers above a fixed landing pad.</summary>
    public static DemoScene DroneSurvey()
    {
        var scene = new Scene();
        scene.Add(Shape.Box(0xFF526B9A).Named("Landing pad").Scaled(3.1f, .1f, 2.25f).At(0, -.7f, 0));
        scene.Add(Shape.Line(new(-.55f, -.64f, 0), new(.55f, -.64f, 0), .045f, 0xFF76DBC7).Named("Pad crosshair"));
        scene.Add(Shape.Line(new(0, -.64f, -.55f), new(0, -.64f, .55f), .045f, 0xFF76DBC7).Named("Pad crosshair"));
        var body = Shape.Sphere(0xFF8DA9FF).Named("Drone body").Scaled(.32f);
        var arms = new Shape[4];
        var rotors = new Shape[4];
        for (var i = 0; i < 4; i++)
        {
            var x = (i % 2 == 0 ? -1 : 1) * .68f;
            var z = (i / 2 == 0 ? -1 : 1) * .52f;
            arms[i] = Shape.Line(Vector3.Zero, new(x, .06f, z), .065f, 0xFF7894C8).Named($"Arm {i + 1}");
            rotors[i] = Shape.Group(
                Shape.Box(0xFFFFBE79).Named($"Propeller {i + 1}").Scaled(.7f, .035f, .09f),
                Shape.Box(0xFFFFBE79).Named($"Propeller cross {i + 1}").Scaled(.09f, .035f, .7f))
                .Named($"Rotor {i + 1}").At(x, .12f, z);
        }
        Shape DroneAt(float time)
        {
            var parts = new Shape[9];
            parts[0] = body;
            for (var i = 0; i < 4; i++)
            {
                parts[i * 2 + 1] = arms[i];
                parts[i * 2 + 2] = rotors[i].Rotated(0, time * 9f + i * .4f, 0);
            }
            var driftX = .36f * MathF.Sin(time * .8f);
            var driftZ = .2f * MathF.Sin(time * 1.1f);
            var rise = .3f + .15f * (1 - MathF.Cos(time * 1.7f));
            return Shape.Group(parts).Named("Survey drone").At(driftX, rise, driftZ);
        }
        var drone = DroneAt(0);
        scene.Add(drone);
        scene.AddLabel(new("DRONE / SURVEY HOVER", new(-1.4f, 1.25f, 0), 0xFFE4ECFF));
        var camera = new Camera(5.5f, .5f, .48f);
        camera.FitToScene(scene, 4f / 3);
        return new("Drone Survey", "Watch a survey drone hover and spin four pickable propellers above its pad.",
            scene, camera, AnimateNode(scene, drone, DroneAt));
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

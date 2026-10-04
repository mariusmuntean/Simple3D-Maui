using System.Numerics;
using Simple3D.Core;

namespace Simple3D.Shared;

public static partial class DemoScenes
{
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

}

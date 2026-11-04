using System.Numerics;
using Simple3D.Core;

internal static class GpuSceneTests
{
    internal static void Run()
    {
        var box = Shape.Box();
        var scene = new Scene().Add(box);
        var camera = new Camera(5, 0, 0);
        var frame = new GpuScene();
        frame.Update(scene, camera, 100, 100);
        var geometry = frame.Items[0].Geometry;
        scene.Replace(box, box.Rotated(0, .2f, 0));
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(geometry, frame.Items[0].Geometry), "animation must reuse geometry");
        Check(geometry.Vertices.Length == box.TriangleCount * 3 * GpuGeometry.FloatsPerVertex, "vertex layout");
        var near = box.At(0, 0, 1);
        scene.Clear(); scene.Add(box).Add(near);
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "nearest pick");
        Check(frame.Pick(-1, 50) is null, "outside pick");
        camera.Projection = CameraProjection.Orthographic;
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "orthographic pick");
        scene.Remove(box);
        camera.NearPlane = 4;
        frame.Update(scene, camera, 100, 100);
        Check(ReferenceEquals(frame.Pick(50, 50), near), "near clipping must retain back face of double sided box");
        scene.Clear(); scene.AddLabel(new WorldLabel("center", Vector3.Zero));
        camera.NearPlane = .05f;
        frame.Update(scene, camera, 100, 100);
        Check(frame.Items.Count == 0 && frame.Labels.Count == 1, "clear geometry and project labels");
        Check(Vector2.Distance(frame.Labels[0].Position, new(50, 50)) < .001f, "center label");
        var p = Vector4.Transform(new Vector4(0, 0, 0, 1), frame.ViewProjection);
        Check(Math.Abs(p.X / p.W) < .001f && p.Z / p.W >= 0, "clip projection");
        Console.WriteLine("PASS GPU geometry reuse, visibility picking, clipping, labels and projection");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

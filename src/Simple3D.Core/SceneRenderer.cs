using System.Numerics;

namespace Simple3D.Core;

public readonly record struct DrawTriangle(Vector2 A, Vector2 B, Vector2 C, float Depth, uint Color);

/// <summary>Projects small opaque scenes into ordered, flat-shaded screen triangles.</summary>
public static class SceneRenderer
{
    public static IReadOnlyList<DrawTriangle> Render(Scene scene, Camera camera, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            return Array.Empty<DrawTriangle>();

        var eye = new Vector3(MathF.Sin(camera.Yaw)*MathF.Cos(camera.Pitch), MathF.Sin(camera.Pitch),
            MathF.Cos(camera.Yaw)*MathF.Cos(camera.Pitch)) * camera.Distance;
        var forward = Vector3.Normalize(-eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var light = Vector3.Normalize(new Vector3(-.4f, .8f, 1));
        var scale = MathF.Min(width, height) * 1.25f;
        var output = new List<DrawTriangle>();
        foreach (var shape in scene.Shapes)
        {
            var transform = Matrix4x4.CreateScale(shape.Size) *
                Matrix4x4.CreateFromYawPitchRoll(shape.Rotation.Y, shape.Rotation.X, shape.Rotation.Z) *
                Matrix4x4.CreateTranslation(shape.Position);
            foreach (var triangle in shape.Mesh)
            {
                var a = Vector3.Transform(triangle.A, transform);
                var b = Vector3.Transform(triangle.B, transform);
                var c = Vector3.Transform(triangle.C, transform);
                var normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, eye - a) <= 0) continue;
                var da = Vector3.Dot(a - eye, forward);
                var db = Vector3.Dot(b - eye, forward);
                var dc = Vector3.Dot(c - eye, forward);
                if (da <= .05f || db <= .05f || dc <= .05f) continue;
                var pa = Project(a, da); var pb = Project(b, db); var pc = Project(c, dc);
                if (!float.IsFinite(pa.X) || !float.IsFinite(pa.Y) ||
                    !float.IsFinite(pb.X) || !float.IsFinite(pb.Y) ||
                    !float.IsFinite(pc.X) || !float.IsFinite(pc.Y)) continue;
                var shade = .38f + .62f * MathF.Max(0, Vector3.Dot(Vector3.Normalize(normal), light));
                output.Add(new(pa, pb, pc, (da + db + dc) / 3, Shade(shape.Color, shade)));
            }
        }
        output.Sort((a,b) => b.Depth.CompareTo(a.Depth));
        return output;

        Vector2 Project(Vector3 point, float distance)
        {
            var relative = point - eye;
            return new(width/2 + Vector3.Dot(relative, right)*scale/distance,
                       height/2 - Vector3.Dot(relative, up)*scale/distance);
        }
    }

    private static uint Shade(uint color, float light)
    {
        var r = (uint)(((color >> 16) & 255) * light);
        var g = (uint)(((color >> 8) & 255) * light);
        var b = (uint)((color & 255) * light);
        return (color & 0xFF000000) | (r << 16) | (g << 8) | b;
    }
}

using System.Numerics;

namespace Simple3D.Core;

public readonly record struct DrawTriangle(Vector2 A, Vector2 B, Vector2 C, float Depth, uint Color);

/// <summary>Projects small opaque scenes into ordered, flat-shaded screen triangles.</summary>
public static class SceneRenderer
{
    private const float NearPlane = .05f;
    private readonly record struct ClipVertex(Vector3 Position, float Depth);

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
        Span<ClipVertex> vertices = stackalloc ClipVertex[3];
        Span<ClipVertex> clipped = stackalloc ClipVertex[4];
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
                var shade = .38f + .62f * MathF.Max(0, Vector3.Dot(Vector3.Normalize(normal), light));
                var color = Shade(shape.Color, shade);
                if (da >= NearPlane && db >= NearPlane && dc >= NearPlane)
                {
                    AddTriangle(new(a, da), new(b, db), new(c, dc), color);
                    continue;
                }

                vertices[0] = new(a, da); vertices[1] = new(b, db); vertices[2] = new(c, dc);
                var count = 0;
                var previous = vertices[2];
                foreach (var current in vertices)
                {
                    var wasInside = previous.Depth >= NearPlane;
                    var isInside = current.Depth >= NearPlane;
                    // A vertex on the plane is already included by the inside branch.
                    // Emitting it again creates zero-area triangles at clipping boundaries.
                    if (wasInside != isInside &&
                        previous.Depth != NearPlane && current.Depth != NearPlane)
                    {
                        var fraction = (NearPlane - previous.Depth) / (current.Depth - previous.Depth);
                        clipped[count++] = new(Vector3.Lerp(previous.Position, current.Position, fraction), NearPlane);
                    }
                    if (isInside) clipped[count++] = current;
                    previous = current;
                }
                if (count >= 3) AddTriangle(clipped[0], clipped[1], clipped[2], color);
                if (count == 4) AddTriangle(clipped[0], clipped[2], clipped[3], color);
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

        void AddTriangle(ClipVertex a, ClipVertex b, ClipVertex c, uint color)
        {
            var pa = Project(a.Position, a.Depth);
            var pb = Project(b.Position, b.Depth);
            var pc = Project(c.Position, c.Depth);
            if (!float.IsFinite(pa.X) || !float.IsFinite(pa.Y) ||
                !float.IsFinite(pb.X) || !float.IsFinite(pb.Y) ||
                !float.IsFinite(pc.X) || !float.IsFinite(pc.Y)) return;
            // Canvas paths below a hundredth of a square pixel cannot contribute
            // visible coverage, and arise at clipping boundaries.
            var twiceArea = ((double)pb.X - pa.X) * ((double)pc.Y - pa.Y) -
                            ((double)pb.Y - pa.Y) * ((double)pc.X - pa.X);
            if (!double.IsFinite(twiceArea) || Math.Abs(twiceArea) < .02) return;
            output.Add(new(pa, pb, pc, (a.Depth + b.Depth + c.Depth) / 3, color));
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

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Simple3D.Core;

// Portable preparation only; no native graphics types or device ownership in Core.
internal sealed class GpuGeometry
{
    internal const int FloatsPerVertex = 16;
    internal float[] Vertices { get; }
    internal Vector3 Min { get; }
    internal Vector3 Max { get; }
    internal GpuGeometry(Shape shape)
    {
        Vertices = new float[checked(shape.TriangleCount * 3 * FloatsPerVertex)];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var t = 0; t < shape.TriangleCount; t++)
        {
            var tri = shape.Mesh[t];
            var face = Vector3.Normalize(Vector3.Cross(tri.B - tri.A, tri.C - tri.A));
            for (var v = 0; v < 3; v++)
            {
                var p = v == 0 ? tri.A : v == 1 ? tri.B : tri.C;
                var index = shape.Geometry?.Indices[t * 3 + v] ?? 0;
                var n = shape.Geometry?.Normals?[index] ?? face;
                var uv = shape.Geometry?.TextureCoordinates?[index] ?? default;
                var color = shape.Geometry?.Colors?[index] ?? 0xFFFFFFFF;
                var o = (t * 3 + v) * FloatsPerVertex;
                Put(o, new(p, 1)); Put(o + 4, new(n, 0)); Put(o + 8, new(uv.X, uv.Y, 0, 0));
                Put(o + 12, Rgba(color));
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        }
        Min = min; Max = max;
    }
    private void Put(int offset, Vector4 v)
    { Vertices[offset] = v.X; Vertices[offset + 1] = v.Y; Vertices[offset + 2] = v.Z; Vertices[offset + 3] = v.W; }
    internal static Vector4 Rgba(uint c) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, 1);
}

internal readonly record struct GpuItem(Shape Shape, GpuGeometry Geometry, Matrix4x4 World, Matrix4x4 Normal);

/// <summary>The scene content cannot be prepared (for example it exceeds the triangle budget). Not a device failure.</summary>
internal sealed class GpuSceneException(string message) : ArgumentException(message);

internal sealed class GpuScene
{
    private readonly ConditionalWeakTable<object, GpuGeometry> _geometry = new();
    internal List<GpuItem> Items { get; } = new();
    internal List<ProjectedLabel> Labels { get; } = new();
    internal Matrix4x4 ViewProjection { get; private set; }
    internal Vector3 Eye { get; private set; }
    private Camera? _camera;
    private int _width, _height;
    private Vector3 _forward, _right, _up;
    // Expansion preserves flat face normals and per-face two-sided orientation. Upload once,
    // share across instances and shape replacements; never expand on camera/transform changes.
    internal void Update(Scene scene, Camera camera, int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        Items.Clear(); Labels.Clear(); _camera = null;
        var basis = camera.Basis();
        Eye = basis.Eye; _forward = basis.Forward; _right = basis.Right; _up = basis.Up;
        _width = width; _height = height;
        var far = camera.NearPlane * 2;
        long triangles = 0;
        foreach (var (shape, world) in scene.Flatten())
        {
            triangles += shape.TriangleCount;
            if (triangles > DepthRenderer.MaximumTriangles) throw new GpuSceneException("Scene exceeds triangle budget.");
            var geometry = _geometry.GetValue(shape.Geometry is { } mesh ? mesh : shape.Mesh, _ => new GpuGeometry(shape));
            // Like the software renderer, skip geometry whose transform overflows instead of
            // failing the whole frame. Bounds corners enclose every transformed vertex.
            var itemFar = far;
            var finite = true;
            for (var i = 0; i < 8 && finite; i++)
            {
                var p = Vector3.Transform(new Vector3((i & 1) == 0 ? geometry.Min.X : geometry.Max.X,
                    (i & 2) == 0 ? geometry.Min.Y : geometry.Max.Y, (i & 4) == 0 ? geometry.Min.Z : geometry.Max.Z), world);
                finite = float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
                if (finite) itemFar = Math.Max(itemFar, Vector3.Dot(p - Eye, _forward) + camera.NearPlane);
            }
            if (!finite || !float.IsFinite(itemFar)) continue;
            far = itemFar;
            Items.Add(new(shape, geometry, world, NormalMatrix(world)));
        }
        var view = new Matrix4x4(_right.X, _up.X, _forward.X, 0,
            _right.Y, _up.Y, _forward.Y, 0, _right.Z, _up.Z, _forward.Z, 0,
            -Vector3.Dot(Eye, _right), -Vector3.Dot(Eye, _up), -Vector3.Dot(Eye, _forward), 1);
        var aspect = (float)width / height;
        Matrix4x4 projection;
        if (camera.Projection == CameraProjection.Perspective)
        {
            var s = 1 / MathF.Tan(camera.FieldOfView / 2);
            projection = new(s / aspect, 0, 0, 0, 0, s, 0, 0, 0, 0, 1, 1, 0, 0, -camera.NearPlane, 0);
        }
        else
        {
            var s = 2 / camera.OrthographicHeight;
            projection = new(s / aspect, 0, 0, 0, 0, s, 0, 0, 0, 0, 1 / (far - camera.NearPlane), 0, 0, 0, -camera.NearPlane / (far - camera.NearPlane), 1);
        }
        ViewProjection = view * projection;
        foreach (var label in scene.Labels)
        {
            var clip = Vector4.Transform(new Vector4(label.Position, 1), ViewProjection);
            if (clip.W <= 0 || clip.Z < 0) continue;
            var p = new Vector2((clip.X / clip.W + 1) * width / 2, (1 - clip.Y / clip.W) * height / 2);
            if (p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                Labels.Add(new(label, p, Vector3.Dot(label.Position - Eye, _forward)));
        }
        _camera = camera;
    }

    // Cofactor matrix: equals det * transpose(inverse) for invertible transforms, scaled by
    // sign(det) so mirrored transforms keep outward normals. Unlike the inverse it stays defined
    // when an axis collapses, where the surviving face normal is still correct (the software
    // renderer likewise lights such faces from their world-space normal).
    internal static Matrix4x4 NormalMatrix(Matrix4x4 world)
    {
        var r0 = new Vector3(world.M11, world.M12, world.M13);
        var r1 = new Vector3(world.M21, world.M22, world.M23);
        var r2 = new Vector3(world.M31, world.M32, world.M33);
        // Uniform rescaling only changes the cofactor's magnitude (the shader normalizes), and
        // keeps the cross products of large scales from overflowing.
        var m = Vector3.Max(Vector3.Abs(r0), Vector3.Max(Vector3.Abs(r1), Vector3.Abs(r2)));
        var scale = MathF.Max(m.X, MathF.Max(m.Y, m.Z));
        if (scale > 0 && float.IsFinite(scale)) { r0 /= scale; r1 /= scale; r2 /= scale; }
        var b0 = Vector3.Cross(r1, r2);
        var b1 = Vector3.Cross(r2, r0);
        var b2 = Vector3.Cross(r0, r1);
        var sign = Vector3.Dot(r0, b0) < 0 ? -1f : 1f;
        // Normalize magnitude so a collapsed axis leaves the surviving normal near unit length
        // rather than subnormal (which GPU normalize() may flush to zero).
        var bm = Vector3.Max(Vector3.Abs(b0), Vector3.Max(Vector3.Abs(b1), Vector3.Abs(b2)));
        var bScale = MathF.Max(bm.X, MathF.Max(bm.Y, bm.Z));
        if (bScale > 0 && float.IsFinite(bScale)) sign /= bScale;
        b0 *= sign; b1 *= sign; b2 *= sign;
        return new(b0.X, b0.Y, b0.Z, 0, b1.X, b1.Y, b1.Z, 0, b2.X, b2.Y, b2.Z, 0, 0, 0, 0, 1);
    }

    // On-demand geometric picking avoids a full-frame CPU render or synchronous GPU readback.
    internal Shape? Pick(double x, double y)
    {
        if (_camera is not { } camera || !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= _width || y >= _height) return null;
        var px = (float)(2 * x / _width - 1) * _width / _height;
        var py = (float)(1 - 2 * y / _height);
        var perspective = camera.Projection == CameraProjection.Perspective;
        var origin = Eye;
        var direction = _forward;
        if (perspective) direction += (_right * px + _up * py) * MathF.Tan(camera.FieldOfView / 2);
        else origin += (_right * px + _up * py) * camera.OrthographicHeight / 2;
        var nearest = float.PositiveInfinity;
        Shape? hit = null;
        foreach (var item in Items)
            foreach (var triangle in item.Shape.Mesh)
            {
                var a = Vector3.Transform(triangle.A, item.World);
                var b = Vector3.Transform(triangle.B, item.World);
                var c = Vector3.Transform(triangle.C, item.World);
                var ab = b - a; var ac = c - a;
                var p = Vector3.Cross(direction, ac);
                var det = Vector3.Dot(ab, p);
                if (!float.IsFinite(det) || det == 0 || (!item.Shape.Material.DoubleSided && det <= 0)) continue;
                var tvec = origin - a;
                var u = Vector3.Dot(tvec, p) / det;
                if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(tvec, ab);
                var v = Vector3.Dot(direction, q) / det;
                if (v < 0 || u + v > 1) continue;
                var distance = Vector3.Dot(ac, q) / det;
                // direction has unit forward component, so distance is camera-space depth.
                if (distance >= camera.NearPlane && distance < nearest) { nearest = distance; hit = item.Shape; }
            }
        return hit;
    }
}

using System.Numerics;

namespace Simple3D.Core;

/// <summary>A projected overlay label. Coordinates use the same pixel space as the frame.</summary>
/// <param name="Label">Source label.</param>
/// <param name="Position">Anchor measured from the top left.</param>
/// <param name="Depth">Positive distance along the view axis.</param>
public readonly record struct ProjectedLabel(WorldLabel Label, Vector2 Position, float Depth);

/// <summary>Owned rendering snapshot. Pixel and picking storage remains valid across subsequent renders.</summary>
public sealed class RenderFrame
{
    private readonly int[] _ids;
    private readonly Shape[] _shapes;

    /// <summary>Width in physical pixels.</summary>
    public int Width { get; }
    /// <summary>Height in physical pixels.</summary>
    public int Height { get; }
    /// <summary>Row-major opaque pixels packed as 0xAARRGGBB. On little-endian machines bytes are BGRA; stride is Width * 4.</summary>
    public ReadOnlyMemory<uint> Pixels { get; }
    /// <summary>Projected labels inside the viewport and in front of the near plane. Labels overlay geometry and are not depth tested.</summary>
    public IReadOnlyList<ProjectedLabel> Labels { get; }

    internal RenderFrame(int width, int height, uint[] pixels, int[] ids, Shape[] shapes, ProjectedLabel[] labels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        _ids = ids;
        _shapes = shapes;
        Labels = Array.AsReadOnly(labels);
    }

    /// <summary>Returns the visible leaf shape at a physical pixel, or null for background or coordinates outside the frame.</summary>
    public Shape? Pick(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        var id = _ids[y * Width + x];
        return id < 0 ? null : _shapes[id];
    }
}

/// <summary>A fixed-size render target whose pixel and picking storage is reused on each render.</summary>
/// <remarks>Content is overwritten by <see cref="DepthRenderer.RenderInto"/>. Copy pixels or use <see cref="DepthRenderer.Render"/> when a snapshot must survive later renders.</remarks>
public sealed class RenderTarget
{
    private readonly uint[] _pixels;
    private readonly int[] _ids;
    private Shape[] _shapes = [];
    private IReadOnlyList<ProjectedLabel> _labels = Array.Empty<ProjectedLabel>();
    private bool _valid;
    private long _rasterSamples;

    /// <summary>Width in physical pixels.</summary>
    public int Width { get; }
    /// <summary>Height in physical pixels.</summary>
    public int Height { get; }
    /// <summary>Pixels from the most recent render; the same storage is overwritten on the next render.</summary>
    public ReadOnlyMemory<uint> Pixels => _pixels;
    /// <summary>Projected labels from the most recent render.</summary>
    public IReadOnlyList<ProjectedLabel> Labels => _labels;
    /// <summary>Clipped triangle bounding-box samples evaluated by the most recent successful render.</summary>
    public long RasterSamples => _rasterSamples;

    /// <summary>Allocates a reusable target with dimensions from 1 to 2048.</summary>
    public RenderTarget(int width, int height)
    {
        if (width < 1 || width > DepthRenderer.MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1 || height > DepthRenderer.MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _pixels = new uint[width * height];
        _ids = new int[width * height];
        Array.Fill(_ids, -1);
    }

    /// <summary>Returns the visible leaf shape at a physical pixel in the most recent render.</summary>
    public Shape? Pick(int x, int y)
    {
        if (!_valid || x < 0 || y < 0 || x >= Width || y >= Height) return null;
        var id = _ids[y * Width + x];
        return id < 0 ? null : _shapes[id];
    }

    internal uint[] PixelBuffer => _pixels;
    internal int[] IdBuffer => _ids;
    internal void BeginRender()
    {
        _valid = false;
        _rasterSamples = 0;
        _shapes = [];
        _labels = Array.Empty<ProjectedLabel>();
    }
    internal void Update(Shape[] shapes, ProjectedLabel[] labels, long rasterSamples)
    {
        _shapes = shapes;
        _labels = Array.AsReadOnly(labels);
        _rasterSamples = rasterSamples;
        _valid = true;
    }
}

/// <summary>Indicates that clipped triangle coverage exceeded the per-frame raster work budget.</summary>
public sealed class RasterBudgetExceededException : ArgumentException
{
    /// <summary>Creates a raster budget diagnostic for the scene argument.</summary>
    public RasterBudgetExceededException() : base("Scene exceeds raster sample budget.", "scene") { }
}

/// <summary>Opaque software triangle rasterizer with per-pixel depth and picking. Instances reuse depth scratch storage and are not thread safe.</summary>
/// <remarks>Transforms use single precision; triangles whose transformed positions or normals overflow or collapse are skipped. There is no far clipping plane.</remarks>
public sealed class DepthRenderer
{
    /// <summary>Maximum width or height. Larger targets must be scaled by the host.</summary>
    public const int MaximumDimension = 2048;
    /// <summary>Maximum triangles visited per render, including repeated mesh instances.</summary>
    public const int MaximumTriangles = 1_000_000;
    /// <summary>Maximum total clipped triangle bounding-box samples evaluated per frame.</summary>
    public const long MaximumRasterSamples = 16_000_000;
    private float[] _depth = [];

    /// <summary>Renders an owned snapshot. Dimensions must be 1..2048; background must be opaque. Equal-depth pixels favor earlier shapes. Scenes exceeding triangle or node budgets throw.</summary>
    public RenderFrame Render(Scene scene, Camera camera, int width, int height, uint background = 0xFFF4F6FA)
    {
        Validate(scene, camera, width, height, background);
        var nodes = PrepareNodes(scene);
        var count = width * height;
        var pixels = new uint[count];
        var ids = new int[count];
        var (shapes, labels, _) = RenderCore(scene, camera, width, height, background, nodes, pixels, ids);
        return new(width, height, pixels, ids, shapes, labels);
    }

    /// <summary>Renders into a reusable target. Its previous pixels and picks are overwritten; after an exception, render again before reading it.</summary>
    public void RenderInto(Scene scene, Camera camera, RenderTarget target, uint background = 0xFFF4F6FA)
    {
        ArgumentNullException.ThrowIfNull(target);
        Validate(scene, camera, target.Width, target.Height, background);
        target.BeginRender();
        var nodes = PrepareNodes(scene);
        var (shapes, labels, rasterSamples) = RenderCore(scene, camera, target.Width, target.Height, background,
            nodes, target.PixelBuffer, target.IdBuffer);
        target.Update(shapes, labels, rasterSamples);
    }

    private static void Validate(Scene scene, Camera camera, int width, int height, uint background)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        if (width < 1 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        if ((background >> 24) != 255) throw new ArgumentOutOfRangeException(nameof(background));
    }

    private static (Shape Shape, Matrix4x4 Transform)[] PrepareNodes(Scene scene)
    {
        var nodes = scene.Flatten().ToArray();
        long triangles = 0;
        foreach (var node in nodes)
        {
            triangles += node.Shape.TriangleCount;
            if (triangles > MaximumTriangles) throw new ArgumentException("Scene exceeds triangle budget.", nameof(scene));
        }
        return nodes;
    }

    private (Shape[] Shapes, ProjectedLabel[] Labels, long RasterSamples) RenderCore(Scene scene, Camera camera, int width, int height,
        uint background, (Shape Shape, Matrix4x4 Transform)[] nodes, uint[] pixels, int[] ids)
    {
        var count = width * height;
        if (_depth.Length < count) _depth = new float[count];
        Array.Fill(_depth, float.PositiveInfinity, 0, count);
        Array.Fill(pixels, background);
        Array.Fill(ids, -1);
        long rasterSamples = 0;

        var basis = camera.Basis();
        var scale = height / (2 * Math.Tan(camera.FieldOfView * .5));
        var orthoScale = height / (double)camera.OrthographicHeight;
        var perspective = camera.Projection == CameraProjection.Perspective;
        var light = Vector3.Normalize(new Vector3(-.4f, .8f, 1));
        Span<Vector3> input = stackalloc Vector3[3];
        Span<Vector3> clipped = stackalloc Vector3[4];
        for (var id = 0; id < nodes.Length; id++)
        {
            var (shape, transform) = nodes[id];
            foreach (var triangle in shape.Mesh)
            {
                var a = Vector3.Transform(triangle.A, transform);
                var b = Vector3.Transform(triangle.B, transform);
                var c = Vector3.Transform(triangle.C, transform);
                var normal = Vector3.Cross(b - a, c - a);
                var magnitude = normal.Length();
                if (!float.IsFinite(magnitude) || magnitude <= 0) continue;
                var facing = Vector3.Dot(normal, perspective ? basis.Eye - a : -basis.Forward);
                if (!shape.Material.DoubleSided && facing <= 0) continue;
                normal /= magnitude;
                if (facing < 0) normal = -normal;
                var shade = shape.Material.Lit ? .38f + .62f * MathF.Max(0, Vector3.Dot(normal, light)) : 1;
                var color = Shade(shape.Color, shade);
                input[0] = View(a);
                input[1] = View(b);
                input[2] = View(c);
                if (!Finite(input[0]) || !Finite(input[1]) || !Finite(input[2])) continue;

                // A triangle clipped by one plane has at most four vertices.
                var n = 0;
                var previous = input[2];
                foreach (var current in input)
                {
                    var wasInside = previous.Z >= camera.NearPlane;
                    var inside = current.Z >= camera.NearPlane;
                    if (wasInside != inside && previous.Z != camera.NearPlane && current.Z != camera.NearPlane)
                    {
                        var fraction = (camera.NearPlane - (double)previous.Z) / ((double)current.Z - previous.Z);
                        clipped[n++] = new(
                            (float)(previous.X * (1 - fraction) + current.X * fraction),
                            (float)(previous.Y * (1 - fraction) + current.Y * fraction), camera.NearPlane);
                    }
                    if (inside) clipped[n++] = current;
                    previous = current;
                }
                for (var i = 1; i + 1 < n; i++) Raster(clipped[0], clipped[i], clipped[i + 1], color, id);
            }
        }

        var labels = new List<ProjectedLabel>();
        foreach (var label in scene.Labels)
        {
            var v = View(label.Position);
            if (!Finite(v) || v.Z < camera.NearPlane) continue;
            var p = Project(v);
            if (p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                labels.Add(new(label, new((float)p.X, (float)p.Y), v.Z));
        }
        return (nodes.Select(n => n.Shape).ToArray(), labels.ToArray(), rasterSamples);

        Vector3 View(Vector3 p)
        {
            var relative = p - basis.Eye;
            return new(Vector3.Dot(relative, basis.Right), Vector3.Dot(relative, basis.Up), Vector3.Dot(relative, basis.Forward));
        }

        // Double projection keeps tiny near planes and orthographic heights finite.
        (double X, double Y) Project(Vector3 p)
        {
            var s = perspective ? scale / p.Z : orthoScale;
            return (width * .5 + p.X * s, height * .5 - p.Y * s);
        }

        void Raster(Vector3 a, Vector3 b, Vector3 c, uint color, int id)
        {
            var pa = Project(a);
            var pb = Project(b);
            var pc = Project(c);
            var area = Edge(pa, pb, pc.X, pc.Y);
            if (!double.IsFinite(area) || Math.Abs(area) < 1e-12) return;
            var minX = Math.Max(0, Math.Ceiling(Math.Min(pa.X, Math.Min(pb.X, pc.X)) - .5));
            var maxX = Math.Min(width - 1, Math.Floor(Math.Max(pa.X, Math.Max(pb.X, pc.X)) - .5));
            var minY = Math.Max(0, Math.Ceiling(Math.Min(pa.Y, Math.Min(pb.Y, pc.Y)) - .5));
            var maxY = Math.Min(height - 1, Math.Floor(Math.Max(pa.Y, Math.Max(pb.Y, pc.Y)) - .5));
            if (minX > maxX || minY > maxY || !double.IsFinite(minX + maxX + minY + maxY)) return;
            rasterSamples += ((long)maxX - (long)minX + 1) * ((long)maxY - (long)minY + 1);
            if (rasterSamples > MaximumRasterSamples)
                throw new RasterBudgetExceededException();
            for (var y = (int)minY; y <= (int)maxY; y++)
                for (var x = (int)minX; x <= (int)maxX; x++)
                {
                    var w0 = Edge(pb, pc, x + .5, y + .5) / area;
                    var w1 = Edge(pc, pa, x + .5, y + .5) / area;
                    var w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    // Perspective depth is reciprocal, unlike orthographic depth.
                    var z = (float)(perspective ? 1 / (w0 / a.Z + w1 / b.Z + w2 / c.Z) : w0 * a.Z + w1 * b.Z + w2 * c.Z);
                    var index = y * width + x;
                    if (z >= camera.NearPlane && z < _depth[index])
                    {
                        _depth[index] = z;
                        pixels[index] = color;
                        ids[index] = id;
                    }
                }
        }
    }

    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static double Edge((double X, double Y) a, (double X, double Y) b, double x, double y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
    private static uint Shade(uint color, float light) => 0xFF000000 |
        ((uint)(((color >> 16) & 255) * light) << 16) |
        ((uint)(((color >> 8) & 255) * light) << 8) | (uint)((color & 255) * light);
}

using System.Numerics;

namespace Simple3D.Core;

internal static class PrimitiveTextures
{
    private static readonly Lazy<Mesh> Box = new(() => Create(Meshes.Box, false, false));
    private static readonly Lazy<Mesh> Sphere = new(() => Create(Meshes.Sphere, true, false));
    private static readonly Lazy<Mesh> Cylinder = new(() => Create(Meshes.Cylinder, false, true));
    private static readonly Lazy<Mesh> Pyramid = new(() => Create(Meshes.Pyramid, false, false));

    internal static Mesh? Get(IReadOnlyList<Triangle3> triangles)
    {
        if (ReferenceEquals(triangles, Meshes.Box)) return Box.Value;
        if (ReferenceEquals(triangles, Meshes.Sphere)) return Sphere.Value;
        if (ReferenceEquals(triangles, Meshes.Cylinder)) return Cylinder.Value;
        if (ReferenceEquals(triangles, Meshes.Pyramid)) return Pyramid.Value;
        return null;
    }

    private static Mesh Create(Triangle3[] triangles, bool sphere, bool cylinder)
    {
        var vertices = new Vector3[triangles.Length * 3];
        var uv = new Vector2[vertices.Length];
        var indices = new int[vertices.Length];
        for (var i = 0; i < triangles.Length; i++)
        {
            var triangle = triangles[i];
            var normal = Vector3.Normalize(Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A));
            var wrap = sphere || (cylinder && MathF.Abs(normal.Y) < .5f);
            for (var j = 0; j < 3; j++)
            {
                var p = j == 0 ? triangle.A : j == 1 ? triangle.B : triangle.C;
                var index = i * 3 + j; vertices[index] = p; indices[index] = index;
                if (wrap)
                    uv[index] = new(.5f + MathF.Atan2(p.Z, p.X) / (2 * MathF.PI),
                        sphere ? .5f + MathF.Asin(Math.Clamp(p.Y * 2, -1, 1)) / MathF.PI : p.Y + .5f);
                else if (MathF.Abs(normal.Y) >= MathF.Max(MathF.Abs(normal.X), MathF.Abs(normal.Z)))
                    uv[index] = new(p.X + .5f, p.Z + .5f);
                else if (MathF.Abs(normal.X) > MathF.Abs(normal.Z)) uv[index] = new(p.Z + .5f, p.Y + .5f);
                else uv[index] = new(p.X + .5f, p.Y + .5f);
            }
            if (wrap)
            {
                var start = i * 3;
                var min = MathF.Min(uv[start].X, MathF.Min(uv[start+1].X, uv[start+2].X));
                var max = MathF.Max(uv[start].X, MathF.Max(uv[start+1].X, uv[start+2].X));
                if (max - min > .5f)
                    for (var j = 0; j < 3; j++) if (uv[start+j].X < .5f) uv[start+j].X += 1;
                // At the poles longitude is undefined; use the other two corners' mean.
                if (sphere)
                    for (var j = 0; j < 3; j++)
                        if (MathF.Abs(vertices[start+j].Y) >= .49999f)
                            uv[start+j].X = (uv[start+(j+1)%3].X + uv[start+(j+2)%3].X) * .5f;
            }
        }
        return new Mesh(vertices, indices, uv);
    }
}

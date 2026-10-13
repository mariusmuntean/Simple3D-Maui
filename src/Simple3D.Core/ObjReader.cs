using System.Globalization;
using System.Numerics;
using System.Text;

namespace Simple3D.Core;

/// <summary>Reads a bounded OBJ geometry subset without loading files or material libraries.</summary>
public static class ObjReader
{
    /// <summary>Maximum source characters, including comments and line endings.</summary>
    public const int MaximumTextLength = 16 * 1024 * 1024;
    /// <summary>Maximum characters in one line, excluding its line ending.</summary>
    public const int MaximumLineLength = 16 * 1024;
    /// <summary>Maximum corners in a single convex planar face.</summary>
    public const int MaximumFaceVertices = 256;

    /// <summary>Parses OBJ text. Invalid or unsupported geometry throws <see cref="FormatException"/> with a line number.</summary>
    public static Mesh Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Read(reader);
    }

    /// <summary>
    /// Reads triangles and strictly convex planar polygons; leaves the reader open. Supports v x y z [1],
    /// vt u [v [0]], vn x y z, and independent positive/negative face indices. Every face must use the same
    /// attribute layout. Named objects/groups, material references and smoothing directives are ignored.
    /// Source attribute counts, output vertices and triangles are limited to <see cref="Mesh.MaximumElements"/>.
    /// </summary>
    public static Mesh Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var parser = new Parser();
        var line = new StringBuilder();
        var characters = 0;
        var afterCarriageReturn = false;
        int value;
        while ((value = reader.Read()) != -1)
        {
            if (++characters > MaximumTextLength) throw parser.Error("Source exceeds the character limit.");
            var c = (char)value;
            if (c == '\n' && afterCarriageReturn) { afterCarriageReturn = false; continue; }
            afterCarriageReturn = c == '\r';
            if (c is '\r' or '\n')
            {
                if (line.Length != 0) parser.ParseLine(line.ToString());
                line.Clear();
                parser.Line++;
            }
            else
            {
                if (line.Length == MaximumLineLength) throw parser.Error("Line exceeds the character limit.");
                line.Append(c);
            }
        }
        if (line.Length != 0) parser.ParseLine(line.ToString());
        return parser.Build();
    }

    private sealed class Parser
    {
        private readonly List<Vector3> positions = [], sourceNormals = [], vertices = [], normals = [];
        private readonly List<Vector2> sourceUvs = [], uvs = [];
        private readonly List<int> indices = [];
        private readonly Dictionary<Corner, int> vertexIndices = [];
        private int attributeLayout = -1;
        internal int Line = 1;
        internal FormatException Error(string message) => new($"Line {Line}: {message}");

        internal void ParseLine(string text)
        {
            var comment = text.IndexOf('#');
            if (comment >= 0) text = text[..comment];
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return;
            switch (words[0])
            {
                case "v":
                    if (words.Length is not (4 or 5) || (words.Length == 5 && Number(words[4]) != 1))
                        throw Error("Positions require x y z and optional homogeneous weight 1.");
                    AddSource(positions, new(Number(words[1]), Number(words[2]), Number(words[3])));
                    break;
                case "vt":
                    if (words.Length is < 2 or > 4 || (words.Length == 4 && Number(words[3]) != 0))
                        throw Error("Texture coordinates require u, optional v, and optional third coordinate 0.");
                    AddSource(sourceUvs, new(Number(words[1]), words.Length >= 3 ? Number(words[2]) : 0));
                    break;
                case "vn":
                    if (words.Length != 4) throw Error("Normals require three coordinates.");
                    var normal = new Vector3(Number(words[1]), Number(words[2]), Number(words[3]));
                    if (normal == Vector3.Zero) throw Error("Normals must be nonzero.");
                    AddSource(sourceNormals, Validation.UnitVector(normal, nameof(normal)));
                    break;
                case "f": Face(words); break;
                case "o": case "g": case "s": case "usemtl": case "mtllib": break;
                default: throw Error($"Unsupported record '{words[0]}'.");
            }
        }

        internal Mesh Build() => new(vertices, indices, attributeLayout is 1 or 3 ? uvs : null,
            attributeLayout is 2 or 3 ? normals : null);

        private void AddSource<T>(List<T> target, T value)
        {
            if (target.Count == Mesh.MaximumElements) throw Error("Source attribute count exceeds the mesh limit.");
            target.Add(value);
        }

        private float Number(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
                throw Error($"Invalid finite number '{value}'.");
            return number;
        }

        private int Index(string value, int count)
        {
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index) || index == 0)
                throw Error($"Invalid index '{value}'.");
            var resolved = index > 0 ? (long)index - 1 : (long)count + index;
            if (resolved < 0 || resolved >= count) throw Error($"Index '{value}' is outside the available attributes.");
            return (int)resolved;
        }

        private Corner ParseCorner(string value)
        {
            var parts = value.Split('/');
            if (parts.Length is < 1 or > 3 || parts[0].Length == 0 ||
                (parts.Length == 2 && parts[1].Length == 0) || (parts.Length == 3 && parts[2].Length == 0))
                throw Error($"Invalid face corner '{value}'.");
            var corner = new Corner(Index(parts[0], positions.Count),
                parts.Length >= 2 && parts[1].Length > 0 ? Index(parts[1], sourceUvs.Count) : -1,
                parts.Length == 3 ? Index(parts[2], sourceNormals.Count) : -1);
            var layout = (corner.Uv >= 0 ? 1 : 0) | (corner.Normal >= 0 ? 2 : 0);
            if (attributeLayout != -1 && attributeLayout != layout) throw Error("Every face corner must use the same UV and normal attributes.");
            attributeLayout = layout;
            return corner;
        }

        private void Face(string[] words)
        {
            var count = words.Length - 1;
            if (count < 3 || count > MaximumFaceVertices) throw Error($"Faces require 3 to {MaximumFaceVertices} corners.");
            if (indices.Count / 3 > Mesh.MaximumElements - (count - 2)) throw Error("Triangle count exceeds the mesh limit.");
            var corners = new Corner[count];
            for (var i = 0; i < count; i++) corners[i] = ParseCorner(words[i + 1]);
            ValidatePolygon(corners);
            var face = new int[count];
            for (var i = 0; i < count; i++)
            {
                var corner = corners[i];
                if (!vertexIndices.TryGetValue(corner, out var index))
                {
                    if (vertices.Count == Mesh.MaximumElements) throw Error("Vertex count exceeds the mesh limit.");
                    index = vertices.Count;
                    vertexIndices.Add(corner, index);
                    vertices.Add(positions[corner.Position]);
                    if (corner.Uv >= 0) uvs.Add(sourceUvs[corner.Uv]);
                    if (corner.Normal >= 0) normals.Add(sourceNormals[corner.Normal]);
                }
                face[i] = index;
            }
            for (var i = 1; i < count - 1; i++)
            {
                indices.Add(face[0]); indices.Add(face[i]); indices.Add(face[i + 1]);
            }
        }

        private void ValidatePolygon(Corner[] corners)
        {
            var origin = positions[corners[0].Position];
            // Check the same float triangle-area contract as Mesh before constructing the final asset.
            for (var i = 1; i < corners.Length - 1; i++)
            {
                var area = Vector3.Cross(positions[corners[i].Position] - origin, positions[corners[i + 1].Position] - origin).LengthSquared();
                if (!float.IsFinite(area) || area <= 0) throw Error("Face contains a degenerate or overflowing triangle.");
            }
            if (corners.Length == 3) return;
            var points = new Point[corners.Length];
            double extent = 0;
            for (var i = 0; i < points.Length; i++)
            {
                var v = positions[corners[i].Position];
                points[i] = new((double)v.X - origin.X, (double)v.Y - origin.Y, (double)v.Z - origin.Z);
                extent = Math.Max(extent, Math.Sqrt(Point.Dot(points[i], points[i])));
            }
            var normal = Point.Cross(points[1], points[2]);
            var normalLength = Math.Sqrt(Point.Dot(normal, normal));
            for (var i = 1; i < points.Length; i++)
                if (Math.Abs(Point.Dot(points[i], normal)) > extent * normalLength * 1e-6)
                    throw Error("Face must be planar.");
            // Every other corner must lie strictly inside each directed edge. The corner limit bounds this quadratic check.
            for (var i = 0; i < points.Length; i++)
            {
                var next = (i + 1) % points.Length;
                var edge = points[next] - points[i];
                for (var j = 0; j < points.Length; j++)
                    if (j != i && j != next && Point.Dot(Point.Cross(edge, points[j] - points[i]), normal) <= 0)
                        throw Error("Face must be strictly convex with no repeated or collinear corners.");
            }
        }
    }

    private readonly record struct Corner(int Position, int Uv, int Normal);
    private readonly record struct Point(double X, double Y, double Z)
    {
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        internal static Point Cross(Point a, Point b) => new(a.Y*b.Z - a.Z*b.Y, a.Z*b.X - a.X*b.Z, a.X*b.Y - a.Y*b.X);
        internal static double Dot(Point a, Point b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;
    }
}

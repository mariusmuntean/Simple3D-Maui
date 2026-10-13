using System.Globalization;
using System.Numerics;
using Simple3D.Core;

internal static class ObjReaderTests
{
    private const string Triangle = "v 0 0 0\nv 1 0 0\nv 0 1 0\n";
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("OBJ triangulates convex polygons with consistent winding", () => {
            var mesh = ObjReader.Parse("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n");
            Check(mesh.TriangleCount == 2 && mesh.Vertices.Count == 4, "quad topology");
            for (var i = 0; i < mesh.Indices.Count; i += 3)
                Check(Vector3.Cross(mesh.Vertices[mesh.Indices[i+1]] - mesh.Vertices[mesh.Indices[i]],
                    mesh.Vertices[mesh.Indices[i+2]] - mesh.Vertices[mesh.Indices[i]]).Z > 0, "winding changed");
            Check(mesh.TextureCoordinates is null && mesh.Normals is null, "invented attributes");
        }),
        ("OBJ resolves independent negative indices and preserves seams", () => {
            var mesh = ObjReader.Parse(Triangle + "vt 0 0\nvt 1 0\nvt 0 1\nvt 0.5 0.5\nvn 0 0 2\n" +
                "f -3/-4/-1 -2/-3/-1 -1/-2/-1\nf 1/4/1 2/2/1 3/3/1\n");
            Check(mesh.Vertices.Count == 4 && mesh.TriangleCount == 2, "tuple deduplication lost seam");
            Check(mesh.TextureCoordinates![0] == Vector2.Zero && mesh.TextureCoordinates[3] == new Vector2(.5f), "UV index resolved against wrong list");
            Check(mesh.Normals!.All(n => n == Vector3.UnitZ), "normal index or normalization");
            Check(mesh.Indices.SequenceEqual(new[] { 0, 1, 2, 3, 1, 2 }), "shared tuples were duplicated");
        }),
        ("OBJ handles normal-only faces and invariant decimal values", () => {
            var previous = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var mesh = ObjReader.Parse("v 0.5 0 0\nv 1.5 0 0\nv 0.5 1e0 0\nvn 0 0 -1\nf 1//1 2//1 3//1");
                Check(mesh.Vertices[0].X == .5f && mesh.Normals![0] == -Vector3.UnitZ && mesh.TextureCoordinates is null, "locale or normal-only face");
            } finally { CultureInfo.CurrentCulture = previous; }
        }),
        ("OBJ comments names CRLF and reader ownership are preserved", () => {
            using var reader = new StringReader("# comment\r\no part\rg A\nmtllib absent.mtl\nusemtl paint\ns 1\n" + Triangle + "f 1 2 3 # end");
            Check(ObjReader.Read(reader).TriangleCount == 1, "metadata parsing");
            Check(reader.Read() == -1, "reader was closed");
            Reject("# comment\r\nv 0 0 0\r\nf 0 1 2", 3);
        }),
        ("OBJ rejects invalid and inconsistent face attributes with line diagnostics", () => {
            foreach (var face in new[] { "f 0 2 3", "f 1 2 4", "f -4 -2 -1", "f 1/1 2 3", "f 1//1 2 3", "f 1/ 2/ 3/", "f 1///1 2 3", "f 1 2", "f 1 1 3" })
                Reject(Triangle + face, 4);
            Reject(Triangle + "vt 0 0\nf 1/1 2/1 3/1\nf 1 2 3", 6);
            Reject(Triangle + "vn 0 0 1\nf 1//1 2//1 3//1\nf 1 2 3", 6);
        }),
        ("OBJ rejects nonfinite data and unsupported geometry", () => {
            foreach (var record in new[] { "v NaN 0 0", "v Infinity 0 0", "v 1e99 0 0", "v 0 0", "vt 0 NaN", "vn 0 0 0", "vn 0 0 Infinity", "l 1 2", "p 1", "curv 0 1 1 2", "v 0 0 0 2", "unknown 1" })
                Reject(record, 1);
            Reject("v 0 0 0\nv 1e20 0 0\nv 0 1e20 0\nf 1 2 3", 4);
        }),
        ("OBJ rejects concave nonplanar and crossed polygons", () => {
            Reject("v 0 0 0\nv 2 0 0\nv 1 0.5 0\nv 2 2 0\nv 0 2 0\nf 1 2 3 4 5", 6);
            Reject("v 0 0 0\nv 1 0 0\nv 1 1 0.1\nv 0 1 0\nf 1 2 3 4", 5);
            Reject("v 0 0 0\nv 1 1 0\nv 0 1 0\nv 1 0 0\nf 1 2 3 4", 5);
        }),
        ("OBJ input line and face bounds stop oversized records", () => {
            using var reader = new RepeatingReader('#', ObjReader.MaximumLineLength + 100);
            Reject(() => ObjReader.Read(reader), 1);
            Check(reader.ReadCount <= ObjReader.MaximumLineLength + 1, "read past bounded line");
            Reject(Triangle + "f " + string.Join(' ', Enumerable.Repeat("1", ObjReader.MaximumFaceVertices + 1)), 4);
            using var source = new RepeatingReader('\n', ObjReader.MaximumTextLength + 100);
            Reject(() => ObjReader.Read(source), ObjReader.MaximumTextLength + 1);
            Check(source.ReadCount <= ObjReader.MaximumTextLength + 1, "read past bounded source");
        }),
        ("OBJ enforces source vertex and generated triangle budgets", () => {
            using var vertices = new RepeatedRecordsReader("", "v 0 0 0\n", Mesh.MaximumElements + 1);
            Reject(() => ObjReader.Read(vertices), Mesh.MaximumElements + 1);
            using var faces = new RepeatedRecordsReader(Triangle, "f 1 2 3\n", Mesh.MaximumElements + 1);
            Reject(() => ObjReader.Read(faces), Mesh.MaximumElements + 4);
        }),
        ("OBJ empty source returns empty immutable geometry", () => {
            var mesh = ObjReader.Parse("# empty\n");
            Check(mesh.Vertices.Count == 0 && mesh.TriangleCount == 0, "empty input");
            try { ObjReader.Parse(null!); throw new Exception("null accepted"); } catch (ArgumentNullException) { }
            try { ObjReader.Read(null!); throw new Exception("null reader accepted"); } catch (ArgumentNullException) { }
        }),
    ];

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(string input, int line) => Reject(() => ObjReader.Parse(input), line);
    private static void Reject(Action action, int line)
    {
        try { action(); throw new Exception("invalid OBJ accepted"); }
        catch (FormatException error) { Check(error.Message.StartsWith($"Line {line}:", StringComparison.Ordinal), error.Message); }
    }
    private sealed class RepeatedRecordsReader(string prefix, string line, int repetitions) : TextReader
    {
        private long offset;
        public override int Read()
        {
            var i = offset++;
            return i < prefix.Length ? prefix[(int)i] : i < prefix.Length + (long)line.Length * repetitions
                ? line[(int)((i - prefix.Length) % line.Length)] : -1;
        }
    }
    private sealed class RepeatingReader(char value, int count) : TextReader
    {
        public int ReadCount { get; private set; }
        public override int Read() => ReadCount++ < count ? value : -1;
    }
}

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json;

namespace Simple3D.Core;

/// <summary>An embedded encoded image. Core leaves image decoding to the caller.</summary>
public sealed class GltfImage
{
    /// <summary>Encoded PNG, JPEG, or other image bytes.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
    /// <summary>The declared image MIME type.</summary>
    public string MimeType { get; }
    internal GltfImage(byte[] bytes, string mimeType) => (Bytes, MimeType) = (bytes, mimeType);
}

/// <summary>A VRM expression's morph contributions. Material and texture binds are ignored.</summary>
public sealed class GltfExpression
{
    /// <summary>Target weights in [0,1], resolved to every affected skinned primitive.</summary>
    public IReadOnlyList<(SkinnedMesh Mesh, int Target, float Weight)> Binds { get; }
    /// <summary>Whether intensity is 1 above 0.5, and 0 otherwise.</summary>
    public bool IsBinary { get; }
    internal GltfExpression((SkinnedMesh Mesh, int Target, float Weight)[] binds, bool isBinary) =>
        (Binds, IsBinary) = (Array.AsReadOnly(binds), isBinary);
}

/// <summary>A binary glTF asset, with reusable geometry, skeletons and embedded images.</summary>
public sealed class GltfModel
{
    private readonly Shape _scene;
    private readonly IReadOnlyDictionary<Material, int> _textures;
    /// <summary>Skeletons, one per armature: skins whose shared joints have equal inverse bind matrices are merged. Joints are ordered parents-first.</summary>
    public IReadOnlyList<Skeleton> Skeletons { get; }
    /// <summary>VRM bone names mapped to joint indices in the skeleton of the first skin; unmapped nodes are omitted.</summary>
    public IReadOnlyDictionary<string, int> HumanoidBones { get; }
    /// <summary>Encoded images in glTF order.</summary>
    public IReadOnlyList<GltfImage> Images { get; }

    /// <summary>VRM expressions, keyed by lowercase preset or exact custom name. VRM 1 presets also expose VRM 0 aliases.</summary>
    public IReadOnlyDictionary<string, GltfExpression> Expressions { get; }

    internal GltfModel(Shape scene, Skeleton[] skeletons, Dictionary<string, int> bones,
        GltfImage[] images, Dictionary<Material, int> textures, Dictionary<string, GltfExpression> expressions)
    {
        _scene = scene; _textures = textures;
        Skeletons = Array.AsReadOnly(skeletons);
        HumanoidBones = new ReadOnlyDictionary<string, int>(bones);
        Images = Array.AsReadOnly(images);
        Expressions = new ReadOnlyDictionary<string, GltfExpression>(expressions);
    }

    /// <summary>Blends named expressions into every node of <paramref name="shape"/> (at any depth) that uses a bound mesh,
    /// keeping the pose and transforms. Unknown names and nonfinite intensities are ignored.</summary>
    /// <remarks>Intensities clamp to [0,1]; binary expressions snap at 0.5. Contributions sum and clamp per target. Replaces all
    /// weights (including <c>mesh.weights</c> defaults) on meshes bound by the supplied expressions, so pass every expression each
    /// frame; a zero intensity still resets its meshes. Other meshes keep their weights. Meshes absent from the shape are skipped.</remarks>
    public Shape ApplyExpressions(Shape shape, IReadOnlyDictionary<string, float> intensities)
    {
        ArgumentNullException.ThrowIfNull(shape); ArgumentNullException.ThrowIfNull(intensities);
        var weights = new Dictionary<SkinnedMesh, float[]>();
        foreach (var (name, value) in intensities)
        {
            if (!float.IsFinite(value) || !Expressions.TryGetValue(name, out var expression)) continue;
            var intensity = expression.IsBinary ? (value > .5f ? 1f : 0f) : Math.Clamp(value, 0, 1);
            foreach (var (mesh, target, weight) in expression.Binds)
            {
                if (!weights.TryGetValue(mesh, out var values)) weights[mesh] = values = new float[mesh.MorphTargets.Count];
                values[target] = Math.Min(1, values[target] + intensity * weight);
            }
        }
        foreach (var (mesh, values) in weights) shape = shape.WithMorphWeights(mesh, values, required: false);
        return shape;
    }

    /// <summary>Creates the default scene. Without a decoder, textures use only their base color.</summary>
    /// <remarks>Decoded textures are cached per image for this call. Coordinates remain right-handed, +Y up,
    /// with counterclockwise front faces. UV V is flipped to the bottom-left convention of Texture2D.</remarks>
    public Shape CreateShape(Func<GltfImage, Texture2D>? decodeImage = null)
    {
        if (decodeImage is null) return _scene;
        var cache = new Dictionary<int, Texture2D>();
        return Decode(_scene);
        Shape Decode(Shape shape)
        {
            if (shape.Children.Count != 0) shape = shape.WithChildren(shape.Children.Select(Decode).ToArray());
            if (_textures.TryGetValue(shape.Material, out var index) && shape.Geometry?.TextureCoordinates is not null)
            {
                if (!cache.TryGetValue(index, out var texture)) cache[index] = texture = decodeImage(Images[index]);
                var m = shape.Material;
                shape = shape.WithMaterial(new Material(texture, m.Color, m.Lit, m.DoubleSided));
            }
            return shape;
        }
    }
}

/// <summary>Reads a bounded binary glTF 2.0 / VRM subset without external resources or image codecs.</summary>
public static class GltfReader
{
    /// <summary>Maximum GLB length, including JSON, binary data and padding.</summary>
    public const int MaximumFileSize = 64 * 1024 * 1024;
    /// <summary>Maximum number of nodes in the asset.</summary>
    public const int MaximumNodes = 16_384;
    /// <summary>Maximum node hierarchy depth, leaving room for scene and primitive groups.</summary>
    public const int MaximumHierarchyDepth = 60;

    /// <summary>Reads a GLB with one JSON and one BIN chunk, leaving the stream open.</summary>
    /// <remarks>Supports triangles, base color, UV0, vertex colors, normals, node TRS/matrices and skins.
    /// Supports skinned morph targets and VRM expressions. Rigid morphs, animations, cameras, lights, PBR, texture transforms,
    /// nontriangle modes and influences beyond JOINTS_0/WEIGHTS_0 are ignored.
    /// Alpha is opaque. External/data URIs, sparse accessors outside morph targets and required geometry compression are rejected.</remarks>
    /// <exception cref="InvalidDataException">Malformed, oversized or unsupported input.</exception>
    public static GltfModel Read(Stream glb)
    {
        ArgumentNullException.ThrowIfNull(glb);
        try
        {
            Span<byte> header = stackalloc byte[12];
            glb.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46546C67)
                throw Error("Expected binary glTF (GLB); JSON .gltf and external buffers are unsupported.");
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 2 || length < 28 || length > MaximumFileSize)
                throw Error("Invalid GLB version or length (maximum 64 MiB).");
            var bytes = new byte[(int)length - 12];
            glb.ReadExactly(bytes);
            if (glb.ReadByte() != -1) throw Error("Data follows the declared GLB length.");
            var jsonLength = Chunk(bytes, 0, 0x4E4F534A);
            var binStart = 8 + jsonLength;
            var binLength = Chunk(bytes, binStart, 0x004E4942);
            if ((long)binStart + 8 + binLength != bytes.Length) throw Error("Expected exactly one JSON and one BIN chunk.");
            using var json = JsonDocument.Parse(bytes.AsMemory(8, jsonLength), new JsonDocumentOptions { MaxDepth = 64 });
            return new Parser(json.RootElement, bytes.AsMemory(binStart + 8, binLength)).Read();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or EndOfStreamException)
        {
            throw new InvalidDataException("Invalid glTF: " + ex.Message, ex);
        }
    }

    private static int Chunk(byte[] bytes, int offset, uint type)
    {
        if (offset < 0 || offset > bytes.Length - 8) throw Error("Truncated GLB chunk header.");
        var span = bytes.AsSpan(offset);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(span);
        // The spec pads chunks to 4 bytes, but some exporters (for example some VRM tools) do not; reads here are unaligned anyway.
        if (length > bytes.Length - offset - 8 || BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) != type)
            throw Error("Invalid GLB chunk type or length.");
        return (int)length;
    }

    private static InvalidDataException Error(string message) => new(message);
    private static JsonElement[] Array(JsonElement parent, string name, int maximum = MaximumNodes)
    {
        if (!parent.TryGetProperty(name, out var value)) return [];
        if (value.GetArrayLength() > maximum) throw Error($"Too many {name}.");
        return value.EnumerateArray().ToArray();
    }
    private static int Int(JsonElement value, string name, int fallback = 0) =>
        value.TryGetProperty(name, out var property) ? property.GetInt32() : fallback;
    private static T At<T>(T[] values, int index)
    {
        if ((uint)index >= values.Length) throw Error("Reference outside array.");
        return values[index];
    }
    private static float[] Floats(JsonElement value, int count)
    {
        if (value.GetArrayLength() != count) throw Error("Invalid vector or matrix length.");
        var result = value.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (result.Any(x => !float.IsFinite(x))) throw Error("Nonfinite transform or color.");
        return result;
    }
    private static Matrix4x4 Matrix(float[] f) => new(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
        f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
    private static uint Color(float r, float g, float b) => 0xFF000000u | Channel(r) << 16 | Channel(g) << 8 | Channel(b);
    private static uint Channel(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw Error("Color components must be in [0,1].");
        return (uint)MathF.Round(value * 255);
    }

    private sealed record Node(string? Name, int Mesh, int Skin, int[] Children, Vector3 Translation, Quaternion Rotation, Vector3 Scale)
    {
        internal Matrix4x4 Local => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
    }
    private sealed record Part(Mesh Mesh, Material Material, Int4[]? Joints, Vector4[]? Weights, MorphTarget[] Targets, float[]? Defaults);
    private readonly record struct View(int Offset, int Length, int Stride);
    private sealed record Accessor(ReadOnlyMemory<byte> Data, int Count, int Components, int ComponentType, int Size, int Stride, bool Normalized, Sparse? Sparse = null)
    {
        internal double Get(int row, int column)
        {
            if (Data.IsEmpty) return 0;
            var bytes = Data.Span[(row * Stride + column * Size)..];
            double value = ComponentType switch
            {
                5121 => bytes[0], 5123 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                _ => BinaryPrimitives.ReadSingleLittleEndian(bytes)
            };
            return Normalized ? value / (ComponentType == 5121 ? 255 : 65535) : value;
        }
        internal float F(int row, int column) => (float)Get(row, column);
        internal int Index(int row, int column = 0)
        {
            var value = Get(row, column);
            if (value < 0 || value > int.MaxValue) throw Error("Index exceeds supported range.");
            return (int)value;
        }
        internal Vector3 V3(int row) => new(F(row, 0), F(row, 1), F(row, 2));
        internal Vector4 V4(int row) => new(F(row, 0), F(row, 1), F(row, 2), F(row, 3));
        internal void Require(int components, bool integer = false, bool morph = false)
        {
            if ((!morph && Sparse is not null) || Components != components || (integer && (ComponentType == 5126 || Normalized)))
                throw Error("Invalid accessor format.");
        }
    }

    private sealed record Sparse(Accessor Indices, Accessor Values);

    private sealed class Parser(JsonElement root, ReadOnlyMemory<byte> binary)
    {
        private View[] views = [];
        private Accessor[] accessors = [];
        private Node[] nodes = [];
        private int[] parents = [], depths = [];
        private Matrix4x4[] worlds = [];
        private Skeleton[] skeletons = [];
        private Dictionary<int, int>[] jointMaps = [];  // per skeleton: glTF node → joint
        private int[][] remaps = [];
        private readonly Dictionary<Material, int> textures = [];
        private int vertexBudget, indexBudget, morphBudget;
        private readonly HashSet<int> morphAccessors = [];
        private readonly Dictionary<int, List<SkinnedMesh>> meshInstances = [], nodeInstances = [];

        internal GltfModel Read()
        {
            if (root.GetProperty("asset").GetProperty("version").GetString() != "2.0") throw Error("Expected glTF 2.0.");
            foreach (var extension in Array(root, "extensionsRequired"))
            {
                var name = extension.GetString();
                if (name is not ("KHR_materials_unlit" or "KHR_texture_transform" or "VRM" or "VRMC_vrm") &&
                    !(name?.StartsWith("KHR_materials_", StringComparison.Ordinal) ?? false))
                    throw Error($"Unsupported required extension: {name}.");
            }
            var buffers = Array(root, "buffers");
            if (buffers.Length != 1 || buffers[0].TryGetProperty("uri", out _)) throw Error("Only one embedded GLB buffer is supported; external/data URIs are unsupported.");
            var bufferLength = Int(buffers[0], "byteLength", -1);
            if (bufferLength < 0 || bufferLength > binary.Length || binary.Length - bufferLength > 3) throw Error("Invalid buffer length.");
            binary = binary[..bufferLength];
            views = Array(root, "bufferViews").Select(ReadView).ToArray();
            foreach (var mesh in Array(root, "meshes"))
                foreach (var primitive in Array(mesh, "primitives"))
                    foreach (var target in Array(primitive, "targets", SkinnedMesh.MaximumMorphTargets))
                        foreach (var attribute in target.EnumerateObject())
                            if (attribute.Name is "POSITION" or "NORMAL" or "TANGENT") morphAccessors.Add(attribute.Value.GetInt32());
            accessors = Array(root, "accessors").Select((a, i) => ReadAccessor(a, morphAccessors.Contains(i))).ToArray();
            nodes = Array(root, "nodes").Select(ReadNode).ToArray();
            ReadHierarchy();
            ReadSkins();
            var images = ReadImages();
            var materials = ReadMaterials(images);
            var meshes = Array(root, "meshes").Select(m => Array(m, "primitives").Where(p => Int(p, "mode", 4) == 4)
                .Select(p => ReadPart(p, m, materials)).ToArray()).ToArray();
            var scene = ReadScene(meshes);
            ValidateBounds(scene, Matrix4x4.Identity);
            return new(scene, skeletons, ReadBones(), images, textures, ReadExpressions(meshes));
        }

        // Rejects transforms that overflow, like Scene.GetBounds, but uses per-joint bounds so loading never skins on the CPU.
        private static void ValidateBounds(Shape shape, Matrix4x4 parent)
        {
            var world = shape.Transform * parent;
            if (shape.SkinnedMesh is { } skinned)
            {
                var (min, max) = skinned.PosedBounds(skinned.Skeleton.RestPose);
                if (min.X <= max.X) { Check(Vector3.Transform(min, world)); Check(Vector3.Transform(max, world)); }
            }
            else if (shape.Geometry is { } mesh) foreach (var v in mesh.Vertices) Check(Vector3.Transform(v, world));
            foreach (var child in shape.Children) ValidateBounds(child, world);
            static void Check(Vector3 p) { if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw Error("Transformed geometry overflows."); }
        }

        private View ReadView(JsonElement v)
        {
            var offset = Int(v, "byteOffset"); var length = Int(v, "byteLength", -1); var stride = Int(v, "byteStride");
            if (Int(v, "buffer", -1) != 0 || offset < 0 || length < 0 || (long)offset + length > binary.Length ||
                (stride != 0 && (stride < 4 || stride > 252 || stride % 4 != 0))) throw Error("Invalid buffer view range or stride.");
            return new(offset, length, stride);
        }

        private Accessor ReadAccessor(JsonElement a, bool morph)
        {
            var hasSparse = a.TryGetProperty("sparse", out var sparse);
            if (hasSparse && !morph) throw Error("Sparse accessors are supported only for morph targets.");
            var hasView = a.TryGetProperty("bufferView", out var viewIndex);
            var view = hasView ? At(views, viewIndex.GetInt32()) : default;
            var type = Int(a, "componentType");
            var size = type switch { 5121 => 1, 5123 => 2, 5125 or 5126 => 4, _ => throw Error("Unsupported accessor component type.") };
            var components = a.GetProperty("type").GetString() switch
            {
                "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16,
                _ => throw Error("Unsupported accessor shape.")
            };
            var count = Int(a, "count", -1); var offset = Int(a, "byteOffset");
            var stride = view.Stride == 0 ? components * size : view.Stride;
            var normalized = a.TryGetProperty("normalized", out var n) && n.GetBoolean();
            if (count < 1 || count > Mesh.MaximumElements * (components == 1 ? 3 : 1) || offset < 0 ||
                offset % size != 0 || (view.Offset + (long)offset) % size != 0 || stride < components * size || stride % size != 0 ||
                (hasView ? (long)offset + (long)(count - 1) * stride + components * size > view.Length : !hasSparse || offset != 0) ||
                (normalized && type is not (5121 or 5123))) throw Error("Invalid accessor count, offset, stride or normalization.");
            Sparse? replacements = null;
            if (hasSparse)
            {
                if (type != 5126 || components != 3) throw Error("Morph deltas must be float VEC3 accessors.");
                var sparseCount = Int(sparse, "count", -1);
                if (sparseCount < 1 || sparseCount > count) throw Error("Invalid sparse count.");
                var indices = sparse.GetProperty("indices"); var indexType = Int(indices, "componentType");
                var indexSize = indexType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw Error("Invalid sparse index type.") };
                var ix = Packed(indices, sparseCount, 1, indexType, indexSize);
                var values = Packed(sparse.GetProperty("values"), sparseCount, 3, 5126, 4);
                var previous = -1;
                for (var i = 0; i < sparseCount; i++)
                {
                    var index = ix.Index(i);
                    if (index <= previous || index >= count) throw Error("Sparse indices must increase and stay within the accessor.");
                    previous = index;
                }
                replacements = new(ix, values);
            }
            return new(hasView ? binary.Slice(view.Offset + offset, view.Length - offset) : default,
                count, components, type, size, stride, normalized, replacements);
        }

        private Accessor Packed(JsonElement a, int count, int components, int type, int size)
        {
            var view = At(views, Int(a, "bufferView", -1)); var offset = Int(a, "byteOffset");
            if (view.Stride != 0 || offset < 0 || (view.Offset + (long)offset) % size != 0 || offset % size != 0 ||
                (long)offset + (long)count * components * size > view.Length) throw Error("Invalid sparse buffer range or alignment.");
            return new(binary.Slice(view.Offset + offset, count * components * size), count, components, type, size, components * size, false);
        }

        private static Node ReadNode(JsonElement n)
        {
            var translation = Vector3.Zero; var rotation = Quaternion.Identity; var scale = Vector3.One;
            if (n.TryGetProperty("matrix", out var matrix))
            {
                if (n.TryGetProperty("translation", out _) || n.TryGetProperty("rotation", out _) || n.TryGetProperty("scale", out _))
                    throw Error("Nodes must use either matrix or TRS.");
                // glTF's column-major sequence is the transposed row-vector matrix, without reordering fields.
                Decompose(Matrix(Floats(matrix, 16)), out scale, out rotation, out translation);
            }
            else
            {
                if (n.TryGetProperty("translation", out var t)) { var f = Floats(t, 3); translation = new(f[0], f[1], f[2]); }
                if (n.TryGetProperty("scale", out var s)) { var f = Floats(s, 3); scale = new(f[0], f[1], f[2]); }
                if (n.TryGetProperty("rotation", out var r)) { var f = Floats(r, 4); rotation = Skeleton.Normalized(new(f[0], f[1], f[2], f[3]), "rotation"); }
            }
            return new(n.TryGetProperty("name", out var name) ? name.GetString() : null, Int(n, "mesh", -1), Int(n, "skin", -1),
                Array(n, "children").Select(c => c.GetInt32()).ToArray(), translation, rotation, scale);
        }

        private static void Decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
        {
            if (!Matrix4x4.Decompose(matrix, out scale, out rotation, out translation)) throw Error("Node/joint matrix cannot be represented as TRS.");
            var rebuilt = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            var delta = matrix - rebuilt;
            if (!Skeleton.Finite(matrix) || MathF.Abs(delta.M11) + MathF.Abs(delta.M12) + MathF.Abs(delta.M13) + MathF.Abs(delta.M14) +
                MathF.Abs(delta.M21) + MathF.Abs(delta.M22) + MathF.Abs(delta.M23) + MathF.Abs(delta.M24) +
                MathF.Abs(delta.M31) + MathF.Abs(delta.M32) + MathF.Abs(delta.M33) + MathF.Abs(delta.M34) + MathF.Abs(delta.M44) > .0001f)
                throw Error("Sheared or non-affine joint/node matrices are unsupported.");
        }

        private void ReadHierarchy()
        {
            parents = Enumerable.Repeat(-1, nodes.Length).ToArray();
            depths = new int[nodes.Length]; worlds = new Matrix4x4[nodes.Length];
            for (var i = 0; i < nodes.Length; i++)
                foreach (var child in nodes[i].Children)
                {
                    _ = At(nodes, child);
                    if (parents[child] != -1) throw Error("Node has multiple parents or repeated children.");
                    parents[child] = i;
                }
            var visited = 0;
            for (var i = 0; i < nodes.Length; i++) if (parents[i] == -1) Visit(i, Matrix4x4.Identity, 1);
            if (visited != nodes.Length) throw Error("Cyclic node hierarchy.");
            void Visit(int index, Matrix4x4 parent, int depth)
            {
                if (depth > MaximumHierarchyDepth) throw Error("Node hierarchy exceeds depth limit.");
                visited++; depths[index] = depth;
                var world = worlds[index] = nodes[index].Local * parent;
                if (!Skeleton.Finite(world)) throw Error("Node world transform overflows.");
                foreach (var child in nodes[index].Children) Visit(child, world, depth + 1);
            }
        }

        // Exporters often write one skin per mesh over a single armature. Skins whose shared joints have the same
        // inverse bind matrices merge into one skeleton, so one pose moves every part.
        private int[] skinGroups = [];
        private void ReadSkins()
        {
            var skins = Array(root, "skins");
            remaps = new int[skins.Length][]; skinGroups = new int[skins.Length];
            var sources = new int[skins.Length][];
            var groups = new List<Dictionary<int, Matrix4x4>>();
            for (var s = 0; s < skins.Length; s++)
            {
                var skin = skins[s];
                var source = sources[s] = Array(skin, "joints", Skeleton.MaximumJoints).Select(j => j.GetInt32()).ToArray();
                if (source.Length == 0 || source.Distinct().Count() != source.Length) throw Error("Skin joints must be nonempty and unique.");
                foreach (var node in source) _ = At(nodes, node);
                if (skin.TryGetProperty("skeleton", out var skeletonRoot)) _ = At(nodes, skeletonRoot.GetInt32());
                Accessor? inverse = null;
                if (skin.TryGetProperty("inverseBindMatrices", out var ib))
                {
                    inverse = At(accessors, ib.GetInt32()); inverse.Require(16);
                    if (inverse.ComponentType != 5126 || inverse.Count < source.Length) throw Error("Invalid inverse bind accessor.");
                }
                var binds = new Dictionary<int, Matrix4x4>(source.Length);
                for (var j = 0; j < source.Length; j++)
                {
                    var bind = inverse is null ? Matrix4x4.Identity : Matrix(Enumerable.Range(0, 16).Select(k => inverse.F(j, k)).ToArray());
                    // Skinning drops w on the CPU but not on the GPU: only affine binds keep both renderers in agreement.
                    if (MathF.Abs(bind.M14) + MathF.Abs(bind.M24) + MathF.Abs(bind.M34) + MathF.Abs(bind.M44 - 1) > 1e-4f) throw Error("Inverse bind matrices must be affine.");
                    binds[source[j]] = bind;
                }
                var group = groups.FindIndex(g => binds.Keys.Any(g.ContainsKey) && binds.All(b => !g.TryGetValue(b.Key, out var other) || Close(other, b.Value)) &&
                    g.Keys.Union(binds.Keys).Count() <= Skeleton.MaximumJoints);
                if (group < 0) { groups.Add(new(binds)); group = groups.Count - 1; }
                else foreach (var (node, bind) in binds) groups[group][node] = bind;
                skinGroups[s] = group;
            }
            skeletons = new Skeleton[groups.Count]; jointMaps = new Dictionary<int, int>[groups.Count];
            for (var g = 0; g < groups.Count; g++)
            {
                var binds = groups[g];
                var ordered = binds.Keys.OrderBy(j => depths[j]).ThenBy(j => j).ToArray();
                var map = jointMaps[g] = ordered.Select((node, i) => (node, i)).ToDictionary(x => x.node, x => x.i);
                var joints = new Joint[ordered.Length]; var names = new HashSet<string>(StringComparer.Ordinal);
                // A shared nonjoint ancestor belongs above all roots; other nonjoint chains fold into rest TRS.
                var ancestor = parents[ordered[0]];
                while (ancestor >= 0 && !ordered.All(j => IsAncestor(ancestor, j))) ancestor = parents[ancestor];
                var rootTransform = ancestor < 0 ? Matrix4x4.Identity : worlds[ancestor];
                for (var j = 0; j < ordered.Length; j++)
                {
                    var nodeIndex = ordered[j]; var node = nodes[nodeIndex];
                    var parent = parents[nodeIndex]; var local = node.Local;
                    while (parent >= 0 && parent != ancestor && !map.ContainsKey(parent)) { local *= nodes[parent].Local; parent = parents[parent]; }
                    var parentJoint = parent >= 0 && map.TryGetValue(parent, out var pj) ? pj : -1;
                    Decompose(local, out var scale, out var rotation, out var translation);
                    var name = string.IsNullOrEmpty(node.Name) ? $"joint_{nodeIndex}" : node.Name;
                    var unique = name; var suffix = 1;
                    while (!names.Add(unique)) unique = $"{name}_{suffix++}";
                    joints[j] = new(unique, parentJoint, translation, rotation, scale, binds[nodeIndex]);
                }
                skeletons[g] = new Skeleton(joints, rootTransform);
            }
            for (var s = 0; s < skins.Length; s++) remaps[s] = sources[s].Select(j => jointMaps[skinGroups[s]][j]).ToArray();
        }

        private static bool Close(Matrix4x4 a, Matrix4x4 b)
        {
            var d = a - b;
            return MathF.Abs(d.M11) + MathF.Abs(d.M12) + MathF.Abs(d.M13) + MathF.Abs(d.M21) + MathF.Abs(d.M22) + MathF.Abs(d.M23) +
                MathF.Abs(d.M31) + MathF.Abs(d.M32) + MathF.Abs(d.M33) + MathF.Abs(d.M41) + MathF.Abs(d.M42) + MathF.Abs(d.M43) < 1e-3f;
        }

        private bool IsAncestor(int ancestor, int node)
        {
            for (var p = parents[node]; p >= 0; p = parents[p]) if (p == ancestor) return true;
            return false;
        }

        private GltfImage[] ReadImages()
        {
            var total = 0;
            return Array(root, "images").Select(i =>
            {
                if (i.TryGetProperty("uri", out _)) throw Error("Image URIs are unsupported; embed images in buffer views.");
                var view = At(views, Int(i, "bufferView", -1));
                if (total > MaximumFileSize - view.Length) throw Error("Embedded images exceed size budget.");
                total += view.Length;
                var mime = i.GetProperty("mimeType").GetString() ?? throw Error("Missing image MIME type.");
                return new GltfImage(binary.Slice(view.Offset, view.Length).ToArray(), mime);
            }).ToArray();
        }

        private Material[] ReadMaterials(GltfImage[] images)
        {
            var imageCount = images.Length;
            var supported = images.Select(i => i.MimeType is "image/png" or "image/jpeg").ToArray();
            var textureSources = Array(root, "textures").Select(t => Int(t, "source", -1)).ToArray();
            return Array(root, "materials").Select(m =>
            {
                var color = 0xFFFFFFFFu; var image = -1;
                if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var factor)) { var f = Floats(factor, 4); color = Color(f[0], f[1], f[2]); }
                    if (pbr.TryGetProperty("baseColorTexture", out var texture))
                    {
                        var source = At(textureSources, Int(texture, "index", -1));
                        if (source < -1 || source >= imageCount) throw Error("Invalid texture image reference.");
                        if (Int(texture, "texCoord") == 0) image = source;
                    }
                }
                var material = new Material(color, !(m.TryGetProperty("extensions", out var ex) && ex.TryGetProperty("KHR_materials_unlit", out _)),
                    m.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean());
                if (image >= 0 && supported[image]) textures.Add(material, image);
                return material;
            }).ToArray();
        }

        private Part ReadPart(JsonElement p, JsonElement source, Material[] materials)
        {
            var attributes = p.GetProperty("attributes");
            var position = At(accessors, attributes.GetProperty("POSITION").GetInt32()); position.Require(3);
            if (position.ComponentType != 5126) throw Error("POSITION must contain floats.");
            var count = position.Count;
            if (vertexBudget > Mesh.MaximumElements - count) throw Error("Aggregate vertex budget exceeded.");
            vertexBudget += count;
            var vertices = Enumerable.Range(0, count).Select(position.V3).ToArray();
            var normal = Attribute("NORMAL", 3); var uv = Attribute("TEXCOORD_0", 2); var color = Attribute("COLOR_0", 0);
            var joints = Attribute("JOINTS_0", 4, true); var weights = Attribute("WEIGHTS_0", 4);
            if ((joints is null) != (weights is null)) throw Error("JOINTS_0 and WEIGHTS_0 must appear together.");
            if (color is not null && color.Components is not (3 or 4)) throw Error("COLOR_0 must be VEC3 or VEC4.");
            var normals = normal is null ? null : Enumerable.Range(0, count).Select(normal.V3).ToArray();
            if (normals is not null)
                for (var i = 0; i < normals.Length; i++)
                {
                    var length = normals[i].Length();
                    if (!(length > 0) || !float.IsFinite(length)) { normals = null; break; }
                    normals[i] /= length;
                }
            Accessor? indices = null;
            if (p.TryGetProperty("indices", out var ix)) { indices = At(accessors, ix.GetInt32()); indices.Require(1, true); }
            var indexCount = indices?.Count ?? count;
            if (indexBudget > Mesh.MaximumElements * 3 - indexCount) throw Error("Aggregate index budget exceeded.");
            indexBudget += indexCount;
            var valid = new List<int>();
            // Validate trailing indices too, but only complete, finite-area triangles become geometry.
            for (var i = 0; i < indexCount; i++) if ((uint)(indices?.Index(i) ?? i) >= count) throw Error("Index outside vertex array.");
            for (var i = 0; i + 2 < indexCount; i += 3)
            {
                var a = indices?.Index(i) ?? i; var b = indices?.Index(i + 1) ?? i + 1; var c = indices?.Index(i + 2) ?? i + 2;
                var area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).LengthSquared();
                if (area > 0 && float.IsFinite(area)) valid.AddRange([a, b, c]);
            }
            var mesh = new Mesh(vertices, valid,
                uv is null ? null : Enumerable.Range(0, count).Select(i => new Vector2(uv.F(i, 0), 1 - uv.F(i, 1))), normals,
                color is null ? null : Enumerable.Range(0, count).Select(i => Color(color.F(i, 0), color.F(i, 1), color.F(i, 2))));
            if (normals is null) mesh = mesh.WithCalculatedNormals();
            var material = p.TryGetProperty("material", out var mat) ? At(materials, mat.GetInt32()) : new Material(0xFFFFFFFF, doubleSided: false);
            var targets = joints is null ? [] : ReadTargets(p, source, count);
            var defaults = targets.Length != 0 && source.TryGetProperty("weights", out var dw) ? Floats(dw, targets.Length) : null;
            if (defaults?.Any(w => w < 0 || w > 1) == true) throw Error("Morph weights must be in [0,1].");
            return new(mesh, material,
                joints is null ? null : Enumerable.Range(0, count).Select(i => new Int4(joints.Index(i, 0), joints.Index(i, 1), joints.Index(i, 2), joints.Index(i, 3))).ToArray(),
                weights is null ? null : Enumerable.Range(0, count).Select(weights.V4).ToArray(), targets, defaults);

            Accessor? Attribute(string name, int components, bool integer = false)
            {
                if (!attributes.TryGetProperty(name, out var a)) return null;
                var accessor = At(accessors, a.GetInt32());
                accessor.Require(components == 0 ? accessor.Components : components, integer);
                if (accessor.Count != count || (!integer && accessor.ComponentType != 5126 && !accessor.Normalized))
                    throw Error($"Invalid {name} accessor.");
                return accessor;
            }
        }

        private MorphTarget[] ReadTargets(JsonElement primitive, JsonElement mesh, int count)
        {
            var targets = Array(primitive, "targets", SkinnedMesh.MaximumMorphTargets);
            var names = mesh.TryGetProperty("extras", out var extras) ? Array(extras, "targetNames", SkinnedMesh.MaximumMorphTargets) : [];
            if (names.Length != 0 && names.Length != targets.Length) throw Error("Morph target names must match target count.");
            // Sparse zero bases can expand far beyond file size. Bound retained deltas plus affected indices.
            var entries = checked(targets.Length * count);
            if (entries > MaximumFileSize / 32 - morphBudget) throw Error("Aggregate morph delta budget exceeded.");
            morphBudget += entries;
            return targets.Select((t, i) => new MorphTarget(names.Length == 0 ? null : names[i].GetString(),
                Deltas(t, "POSITION") ?? new Vector3[count], Deltas(t, "NORMAL"))).ToArray();
            Vector3[]? Deltas(JsonElement target, string semantic)
            {
                if (!target.TryGetProperty(semantic, out var a)) return null;
                var accessor = At(accessors, a.GetInt32()); accessor.Require(3, morph: true);
                if (accessor.ComponentType != 5126 || accessor.Count != count) throw Error("Invalid morph delta accessor.");
                var result = Enumerable.Range(0, count).Select(accessor.V3).ToArray();
                if (accessor.Sparse is { } sparse)
                    for (var i = 0; i < sparse.Indices.Count; i++) result[sparse.Indices.Index(i)] = sparse.Values.V3(i);
                return result;
            }
        }

        private Shape ReadScene(Part[][] meshes)
        {
            var scenes = Array(root, "scenes");
            var roots = scenes.Length == 0 ? Enumerable.Range(0, nodes.Length).Where(i => parents[i] == -1).ToArray()
                : Array(At(scenes, Int(root, "scene")), "nodes").Select(n => n.GetInt32()).ToArray();
            var visited = new HashSet<int>(); var skinned = new List<Shape>(); var triangles = 0; var shapeCount = 1;
            var children = roots.Select(Build).ToList(); children.AddRange(skinned);
            return Shape.Group(children.ToArray());
            Shape Build(int index)
            {
                var node = At(nodes, index);
                if (!visited.Add(index)) throw Error("Repeated scene node.");
                if (++shapeCount > Scene.MaximumNodes) throw Error("Scene node budget exceeded.");
                var parts = new List<Shape>();
                if (node.Skin != -1) _ = At(remaps, node.Skin);
                if (node.Mesh != -1)
                    foreach (var part in At(meshes, node.Mesh))
                    {
                        triangles += part.Mesh.TriangleCount;
                        if (triangles > Mesh.MaximumElements || ++shapeCount > Scene.MaximumNodes) throw Error("Scene geometry budget exceeded.");
                        if (node.Skin == -1) parts.Add(Shape.FromMesh(part.Mesh, part.Material));
                        else
                        {
                            if (part.Joints is null || part.Weights is null) throw Error("Skinned primitive requires joints and weights.");
                            var map = remaps[node.Skin];
                            var influences = new VertexInfluence[part.Joints.Length];
                            for (var v = 0; v < influences.Length; v++)
                            {
                                var j = part.Joints[v]; var w = part.Weights[v];
                                // Some exporters leave unweighted vertices; they follow the root joint instead of failing the file.
                                influences[v] = w == Vector4.Zero ? new(default, Vector4.UnitX)
                                    : new(new(Remap(j.X, w.X), Remap(j.Y, w.Y), Remap(j.Z, w.Z), Remap(j.W, w.W)), w);
                            }
                            var mesh = new SkinnedMesh(part.Mesh, skeletons[skinGroups[node.Skin]], influences, part.Targets);
                            var shape = Shape.FromSkinnedMesh(mesh, part.Material).Named(node.Name);
                            if (part.Defaults is { } weights) shape = shape.WithMorphWeights(mesh, weights);
                            skinned.Add(shape);
                            if (!meshInstances.TryGetValue(node.Mesh, out var instances)) meshInstances[node.Mesh] = instances = [];
                            instances.Add(mesh);
                            if (!nodeInstances.TryGetValue(index, out var nodeParts)) nodeInstances[index] = nodeParts = [];
                            nodeParts.Add(mesh);
                            int Remap(int joint, float weight) => weight == 0 ? 0 : At(map, joint);
                        }
                    }
                parts.AddRange(node.Children.Select(Build));
                return Shape.Group(parts.ToArray()).Named(node.Name).WithLocalTransform(node.Translation, node.Rotation, node.Scale);
            }
        }

        private Dictionary<string, GltfExpression> ReadExpressions(Part[][] meshes)
        {
            var result = new Dictionary<string, GltfExpression>(StringComparer.Ordinal);
            if (!root.TryGetProperty("extensions", out var extensions)) return result;
            var bindBudget = 0;
            if (extensions.TryGetProperty("VRM", out var vrm) && vrm.TryGetProperty("blendShapeMaster", out var master))
                foreach (var group in Array(master, "blendShapeGroups"))
                {
                    var preset = group.TryGetProperty("presetName", out var p) ? p.GetString() : null;
                    var name = !string.IsNullOrEmpty(preset) && !preset.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                        ? preset.ToLowerInvariant() : group.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (!string.IsNullOrEmpty(name)) result[name] = Read(group, false);
                }
            if (extensions.TryGetProperty("VRMC_vrm", out var vrm1) && vrm1.TryGetProperty("expressions", out var expressions))
            {
                Add("preset", true); Add("custom", false);
                void Add(string category, bool preset)
                {
                    if (!expressions.TryGetProperty(category, out var values)) return;
                    foreach (var property in values.EnumerateObject())
                    {
                        if (result.Count >= MaximumNodes) throw Error("Too many expressions.");
                        var name = preset ? property.Name.ToLowerInvariant() : property.Name;
                        var expression = result[name] = Read(property.Value, true);
                        var alias = preset ? name switch
                        {
                            "happy" => "joy", "sad" => "sorrow", "relaxed" => "fun", "aa" => "a", "ih" => "i",
                            "ou" => "u", "ee" => "e", "oh" => "o", "blinkleft" => "blink_l", "blinkright" => "blink_r", _ => name
                        } : name;
                        result[alias] = expression;
                    }
                }
            }
            return result;
            GltfExpression Read(JsonElement value, bool version1)
            {
                var binds = new List<(SkinnedMesh Mesh, int Target, float Weight)>();
                foreach (var bind in Array(value, version1 ? "morphTargetBinds" : "binds"))
                {
                    var reference = Int(bind, version1 ? "node" : "mesh", -1);
                    var meshIndex = version1 ? At(nodes, reference).Mesh : reference;
                    var parts = At(meshes, meshIndex);
                    var target = Int(bind, "index", -1);
                    var weight = bind.GetProperty("weight").GetSingle() / (version1 ? 1 : 100);
                    if (target < 0 || !float.IsFinite(weight) || weight < 0 || weight > 1 ||
                        parts.Any(p => p.Joints is not null && target >= p.Targets.Length)) throw Error("Invalid expression bind.");
                    var instances = version1 ? nodeInstances : meshInstances;
                    if (!instances.TryGetValue(reference, out var affected)) continue;
                    if (bindBudget > Mesh.MaximumElements - affected.Count) throw Error("Expression bind budget exceeded.");
                    bindBudget += affected.Count;
                    foreach (var mesh in affected) binds.Add((mesh, target, weight));
                }
                return new(binds.ToArray(), value.TryGetProperty("isBinary", out var binary) && binary.GetBoolean());
            }
        }

        private Dictionary<string, int> ReadBones()
        {
            var bones = new Dictionary<string, int>(StringComparer.Ordinal);
            if (skeletons.Length == 0 || !root.TryGetProperty("extensions", out var extensions)) return bones;
            if (extensions.TryGetProperty("VRM", out var vrm) && vrm.TryGetProperty("humanoid", out var humanoid))
                foreach (var bone in Array(humanoid, "humanBones")) Add(bone.GetProperty("bone").GetString()!, Int(bone, "node", -1));
            if (extensions.TryGetProperty("VRMC_vrm", out var vrm1) && vrm1.TryGetProperty("humanoid", out var h1) && h1.TryGetProperty("humanBones", out var b1))
                foreach (var bone in b1.EnumerateObject()) Add(bone.Name, Int(bone.Value, "node", -1));
            return bones;
            void Add(string name, int node)
            {
                _ = At(nodes, node);
                if (jointMaps[skinGroups[0]].TryGetValue(node, out var joint)) bones[name] = joint;
            }
        }
    }
}

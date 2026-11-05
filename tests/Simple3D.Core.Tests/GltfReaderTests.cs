using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Simple3D.Core;

internal static class GltfReaderTests
{
    internal static void Run()
    {
        Triangle();
        TexturedQuad();
        Hierarchy();
        Skin();
        Morphs();
        Expressions();
        Malformed();
        Smoke();
        Console.WriteLine("PASS binary glTF geometry, materials, transforms, skins, malformed input and VRM mapping");
    }

    private static void Triangle()
    {
        var f = Basic();
        // Zero normals trigger calculated normals; a trailing vertex and degenerate face are harmless.
        var normal = f.Accessor(new float[9], "VEC3");
        f.Attributes["NORMAL"] = normal;
        var shape = f.Read().CreateShape();
        var mesh = Leaves(shape).Single().Geometry!;
        Check(mesh.TriangleCount == 1 && mesh.Vertices[1] == Vector3.UnitX, "non-indexed triangle");
        Check(mesh.Indices.SequenceEqual([0, 1, 2]) && mesh.Normals!.All(n => n == Vector3.UnitZ), "calculated normals and CCW winding");
        f.Primitive["indices"] = f.Accessor(new byte[] { 0, 1, 2, 0, 0, 1, 2 }, 5121, "SCALAR");
        mesh = Leaves(f.Read().CreateShape()).Single().Geometry!;
        Check(mesh.TriangleCount == 1, "degenerate triangles and trailing indices dropped");
        f.Primitive["mode"] = 1;
        Check(!Leaves(f.Read().CreateShape()).Any(), "nontriangle primitive skipped");

        var extreme = Basic();
        extreme.Attributes["POSITION"] = extreme.Accessor(new float[] { 0, 0, 0, 1e30f, 0, 0, 0, 1e30f, 0 }, "VEC3");
        Check(!Leaves(extreme.Read().CreateShape()).Any(), "overflowing triangle dropped");
    }

    private static void TexturedQuad()
    {
        var f = Basic();
        // Interleaved positions: view offset, accessor offset and byteStride all participate.
        var data = new List<byte>();
        data.AddRange(new byte[4]);
        foreach (var p in new[] { Vector3.Zero, Vector3.UnitX, new Vector3(1, 1, 0), Vector3.UnitY })
        {
            data.AddRange(new byte[4]);
            foreach (var v in new[] { p.X, p.Y, p.Z }) data.AddRange(BitConverter.GetBytes(v));
        }
        var view = f.View(data.ToArray(), 16);
        f.Attributes["POSITION"] = f.AddAccessor(view, 5126, "VEC3", 4, 8);
        f.Attributes["TEXCOORD_0"] = f.Accessor(new byte[] { 0, 0, 255, 0, 255, 255, 0, 255 }, 5121, "VEC2", true);
        f.Attributes["COLOR_0"] = f.Accessor(new ushort[] { 65535, 0, 0, 0, 65535, 65535, 65535, 0, 65535, 65535, 65535, 0, 65535, 65535, 65535, 0 }, "VEC4", true);
        f.Attributes["NORMAL"] = f.Accessor(Enumerable.Repeat(new float[] { 0, 0, 7 }, 4).SelectMany(x => x).ToArray(), "VEC3");
        f.Primitive["indices"] = f.Accessor(new uint[] { 0, 1, 2, 0, 2, 3 }, "SCALAR");
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        f.Root["images"] = new JsonArray(new JsonObject { ["bufferView"] = f.View(image), ["mimeType"] = "image/png" });
        f.Root["textures"] = new JsonArray(new JsonObject { ["source"] = 0 });
        f.Root["materials"] = JsonNode.Parse("""[{"doubleSided":true,"extensions":{"KHR_materials_unlit":{}},"pbrMetallicRoughness":{"baseColorFactor":[0.5,1,0,0.1],"baseColorTexture":{"index":0}}}]""");
        f.Primitive["material"] = 0;
        // Shared image across two primitives must decode once.
        ((JsonArray)f.Mesh["primitives"]!).Add(f.Primitive.DeepClone());
        var model = f.Read();
        var plain = Leaves(model.CreateShape()).First();
        Check(plain.Material.Texture is null && plain.Material.Color == 0xFF80FF00 && !plain.Material.Lit && plain.Material.DoubleSided, "material mapping and alpha");
        var calls = 0;
        var texture = new Texture2D(1, 2, [0xFFFF0000, 0xFF0000FF]);
        var textured = model.CreateShape(i => { calls++; Check(i.Bytes.Span.SequenceEqual(image) && i.MimeType == "image/png", "encoded image"); return texture; });
        Check(calls == 1 && Leaves(textured).All(p => ReferenceEquals(p.Material.Texture, texture)), "per-image texture cache");
        var mesh = Leaves(textured).First().Geometry!;
        Check(mesh.TriangleCount == 2 && mesh.TextureCoordinates![0] == new Vector2(0, 1), "UV top-left to bottom-left conversion");
        Check(texture.Sample(mesh.TextureCoordinates![0].X, mesh.TextureCoordinates[0].Y) == 0xFFFF0000, "top row texture orientation");
        Check(mesh.Colors![0] == 0xFFFF0000 && mesh.Normals!.All(n => n == Vector3.UnitZ), "normalized colors, ignored alpha and normalized normals");
    }

    private static void Hierarchy()
    {
        var f = Basic();
        var q = Quaternion.CreateFromYawPitchRoll(.7f, MathF.PI / 2, .3f);
        var t = new Vector3(2, 3, 4); var s = new Vector3(2, 3, 4);
        var matrix = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .5f)) * Matrix4x4.CreateTranslation(1, 2, 3);
        f.Root["nodes"] = new JsonArray(
            new JsonObject { ["children"] = Numbers(1), ["translation"] = Numbers(2, 3, 4), ["rotation"] = Numbers(q.X, q.Y, q.Z, q.W), ["scale"] = Numbers(2, 3, 4) },
            new JsonObject { ["mesh"] = 0, ["matrix"] = MatrixJson(matrix) });
        f.Root["scenes"] = JsonNode.Parse("""[{"nodes":[0]},{"nodes":[]}]""");
        var shape = f.Read().CreateShape();
        var parent = shape.Children[0]; var child = parent.Children[0];
        var expected = matrix * Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
        Near(Vector3.Transform(Vector3.UnitX, child.Transform * parent.Transform), Vector3.Transform(Vector3.UnitX, expected), "matrix order and quaternion singularity");
        Near(Vector3.Transform(Vector3.UnitX, parent.Named("copy").WithMaterial(new Material()).Scaled(2, 3, 4).At(2, 3, 4).Transform), Vector3.Transform(Vector3.UnitX, parent.Transform), "imported rotation survives copies");
        Near(Vector3.Transform(Vector3.UnitX, parent.Rotated(0, 0, 0).Transform), new Vector3(4, 3, 4), "Euler edit replaces imported rotation");
        f.Root["scene"] = 1;
        Check(!Leaves(f.Read().CreateShape()).Any(), "default scene selection");
    }

    private static void Skin()
    {
        var f = Skinned();
        var model = f.Read(); var skeleton = model.Skeletons.Single();
        Check(skeleton.Joints.Select(j => j.Parent).SequenceEqual([-1, 0]), "parents-first reorder");
        Check(skeleton.Joints[0].Name == "bone" && skeleton.Joints[1].Name == "bone_1", "unique names");
        Near(skeleton.RootTransform.Translation, new(3, 0, 0), "nonjoint ancestor root transform");
        Near(skeleton.Joints[1].InverseBind.Translation, new(-3, -1, 0), "column-major inverse bind fields");
        Check(model.HumanoidBones["hips"] == 0 && model.HumanoidBones["leftUpperArm"] == 1 && model.HumanoidBones["rightLowerLeg"] == 1, "VRM 0 and 1 mapping after reorder");
        var root = model.CreateShape(); var leaf = Leaves(root).Single();
        Check(root.Children.Contains(leaf), "skin moved out of transformed mesh node to scene root");
        Check(leaf.SkinnedMesh!.Influences[0].Joints.X == 0 && leaf.SkinnedMesh.Influences[2].Joints.X == 1, "JOINTS remapped");
        for (var i = 0; i < leaf.Mesh.Count; i++)
        {
            var bind = leaf.Geometry!.Triangles[i]; var rest = leaf.Mesh[i];
            Near(bind.A, rest.A, "rest bind A"); Near(bind.B, rest.B, "rest bind B"); Near(bind.C, rest.C, "rest bind C");
        }
        var bend = SkeletonPose.Create(skeleton, [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)]);
        var posed = root.WithPose(bend);
        Check(Leaves(posed).Single().Mesh[1].B != leaf.Mesh[1].B, "loaded mesh responds to pose");
        // A second skin over the same armature (joints listed in another order, equal binds) shares the skeleton,
        // so one pose moves both meshes; a skin with conflicting binds gets its own skeleton.
        var two = Skinned();
        var nodes = (JsonArray)two.Root["nodes"]!;
        nodes.Add(new JsonObject { ["mesh"] = 0, ["skin"] = 1 });
        ((JsonArray)nodes[0]!["children"]!).Add(4);
        var reversed = two.Accessor(MatrixValues(Matrix4x4.CreateTranslation(-3, 0, 0)).Concat(MatrixValues(Matrix4x4.CreateTranslation(-3, -1, 0))).ToArray(), "MAT4");
        ((JsonArray)two.Root["skins"]!).Add(new JsonObject { ["joints"] = Numbers(1, 2), ["inverseBindMatrices"] = reversed });
        var merged = two.Read();
        Check(merged.Skeletons.Count == 1, "compatible skins merge into one skeleton");
        var parts = Leaves(merged.CreateShape().WithPose(SkeletonPose.Create(merged.Skeletons[0], [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)]))).ToArray();
        Check(parts.Length == 2 && parts.All(p => p.Pose is not null && ReferenceEquals(p.Pose.Skeleton, merged.Skeletons[0])), "one pose reaches both skins");
        Check(parts[0].SkinnedMesh!.Influences[0].Joints.X != parts[1].SkinnedMesh!.Influences[0].Joints.X, "each skin's JOINTS remapped to the shared skeleton");
        two.Root["skins"]![1]!["inverseBindMatrices"] = two.Accessor(MatrixValues(Matrix4x4.CreateTranslation(5, 0, 0)).Concat(MatrixValues(Matrix4x4.CreateTranslation(5, 0, 0))).ToArray(), "MAT4");
        Check(two.Read().Skeletons.Count == 2, "conflicting binds keep separate skeletons");
        // Unweighted vertices follow the root joint rather than failing the file.
        var unweighted = Skinned();
        unweighted.Attributes["WEIGHTS_0"] = unweighted.Accessor(new ushort[] { 0, 0, 0, 0, 65535, 0, 0, 0, 65535, 0, 0, 0, 65535, 0, 0, 0 }, "VEC4", true);
        Check(Leaves(unweighted.Read().CreateShape()).Single().SkinnedMesh!.Influences[0].Weights == Vector4.UnitX, "unweighted vertex bound to root");
        // Projective inverse binds would render differently on the CPU and GPU.
        var projective = Skinned();
        projective.Root["skins"]![0]!["inverseBindMatrices"] = projective.Accessor(MatrixValues(Matrix4x4.CreatePerspective(1, 1, 1, 2)).Concat(MatrixValues(Matrix4x4.Identity)).ToArray(), "MAT4");
        Bad(projective.Build(), "projective inverse bind");
        // Missing inverse binds use identity exactly.
        ((JsonObject)f.Root["skins"]![0]!).Remove("inverseBindMatrices");
        Check(f.Read().Skeletons[0].Joints.All(j => j.InverseBind == Matrix4x4.Identity), "absent inverse binds");
    }

    private static void Morphs()
    {
        var f = MorphFixture();
        var model = f.Read(); var shape = Leaves(model.CreateShape()).Single();
        var targets = shape.SkinnedMesh!.MorphTargets;
        Check(targets.Count == 2 && targets[0].Name == "smile" && targets[1].Name == "blink", "named morph targets");
        Near(targets[0].PositionDeltas[2], new(.4f, 0, 0), "dense morph positions");
        Near(targets[0].NormalDeltas![2], new(.1f, 0, 0), "dense morph normals");
        Near(targets[1].PositionDeltas[2], new(0, -.2f, 0), "sparse morph positions");
        Check(targets[1].PositionDeltas[0] == Vector3.Zero, "sparse base is zero");
        Check(shape.MorphWeights!.SequenceEqual([.5f, .25f]), "default mesh weights");
        Near(shape.Mesh[0].C, new(.1f, 1.95f, 0), "default weights deform rest geometry");
        Check(shape.TriangleCount == 2, "degenerate index removal preserves morph vertex indexing");
        foreach (var mutate in new Action<Fixture>[] {
            x => x.Accessors[^1]!["sparse"]!["count"] = 5,
            x => x.Accessors[^1]!["sparse"]!["indices"]!["componentType"] = 5126,
            x => x.Accessors[^1]!["sparse"]!["values"]!["byteOffset"] = 4,
            x => x.Accessors[^1]!["count"] = 3,
            x => x.Mesh["weights"] = Numbers(1),
            x => x.Mesh["weights"] = Numbers(1.1f, 0),
            x => x.Attributes["NORMAL"] = x.Accessors.Count - 1 })
        { var bad = MorphFixture(); mutate(bad); Bad(bad.Build(), "malformed morph"); }
        foreach (var indices in new byte[][] { [2, 2], [3, 2], [2, 4] })
        {
            var bad = MorphFixture();
            bad.Accessors[^1]!["sparse"]!["indices"]!["bufferView"] = bad.View(indices);
            Bad(bad.Build(), "sparse indices must be increasing and in range");
        }
        // Sparse replacements also overlay a dense base.
        var denseBase = MorphFixture();
        denseBase.Accessors[^1]!["bufferView"] = denseBase.View(Enumerable.Repeat(.05f, 12).SelectMany(BitConverter.GetBytes).ToArray());
        Near(Leaves(denseBase.Read().CreateShape()).Single().SkinnedMesh!.MorphTargets[1].PositionDeltas[0], new(.05f), "sparse dense base");
    }

    private static void Expressions()
    {
        var f = MorphFixture();
        f.Root["extensions"]!["VRM"]!["blendShapeMaster"] = JsonNode.Parse("""
            {"blendShapeGroups":[
            {"presetName":"Joy","name":"Smile","binds":[{"mesh":0,"index":0,"weight":75}]},
            {"presetName":"unknown","name":"MySmile","binds":[{"mesh":0,"index":0,"weight":50}]},
            {"presetName":"blink","isBinary":true,"binds":[{"mesh":0,"index":1,"weight":100}]}]}
            """);
        // One expression bind reaches every primitive and every instance of that mesh.
        ((JsonArray)f.Mesh["primitives"]!).Add(f.Primitive.DeepClone());
        ((JsonArray)f.Root["nodes"]!).Add(new JsonObject { ["mesh"] = 0, ["skin"] = 0 });
        var model = f.Read();
        Check(model.Expressions["joy"].Binds.Count == 4 && model.Expressions["joy"].Binds.All(b => b.Target == 0 && b.Weight == .75f), "VRM0 normalized mesh binds across primitives and nodes");
        Check(model.Expressions.ContainsKey("MySmile") && model.Expressions["blink"].IsBinary, "custom name and binary flag");
        var pose = SkeletonPose.Create(model.Skeletons[0], [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .7f)]);
        var shape = model.CreateShape().WithPose(pose);
        var changed = model.ApplyExpressions(shape, new Dictionary<string, float> { ["joy"] = .5f, ["MySmile"] = .5f, ["blink"] = .5f });
        Check(Leaves(changed).All(p => ReferenceEquals(p.Pose, pose) && p.MorphWeights!.SequenceEqual([.625f, 0])), "expression contributions sum while preserving pose");
        changed = model.ApplyExpressions(changed, new Dictionary<string, float> { ["joy"] = 1, ["MySmile"] = 1, ["blink"] = .5001f });
        Check(Leaves(changed).All(p => p.MorphWeights!.SequenceEqual([1f, 1f])), "expression sum clamps and binary threshold");
        changed = model.ApplyExpressions(changed, new Dictionary<string, float> { ["joy"] = 0 });
        Check(Leaves(changed).All(p => p.MorphWeights is null && ReferenceEquals(p.Pose, pose)), "zero intensity resets affected mesh weights");
        changed = model.ApplyExpressions(model.ApplyExpressions(changed, new Dictionary<string, float> { ["joy"] = 1 }), new Dictionary<string, float> { ["blink"] = 1 });
        Check(Leaves(changed).All(p => p.MorphWeights!.SequenceEqual([0f, 1f])), "expressions left out return to neutral");
        changed = model.ApplyExpressions(changed, new Dictionary<string, float>());
        Check(Leaves(changed).All(p => p.MorphWeights is null), "no expressions is the neutral face");
        Check(ReferenceEquals(changed, model.ApplyExpressions(changed, new Dictionary<string, float> { ["missing"] = 1 })), "unknown expressions ignored");
        Check(Leaves(model.ApplyExpressions(shape, new Dictionary<string, float> { ["joy"] = float.NaN })).All(p => p.MorphWeights is null), "nonfinite intensity ignored");
        // As MirrorStage places a character: an extra transformed group around the model.
        var placed = Shape.Group(shape).Named("Character").Scaled(.5f).Rotated(0, 30, 0).At(1, 2, 3);
        var wrapped = model.ApplyExpressions(placed, new Dictionary<string, float> { ["joy"] = 1, ["blink_l"] = 1 });
        Check(wrapped.Position == placed.Position && wrapped.Size == placed.Size && wrapped.Rotation == placed.Rotation && wrapped.Name == "Character" &&
            Leaves(wrapped).All(p => ReferenceEquals(p.Pose, pose) && p.MorphWeights!.SequenceEqual([.75f, 0])), "expressions reach wrapped groups and keep transforms");
        var box = Shape.Box();
        Check(ReferenceEquals(box, model.ApplyExpressions(box, new Dictionary<string, float> { ["joy"] = 1 })), "shapes without bound meshes are left alone");

        var one = MorphFixture();
        var preset = new JsonObject();
        foreach (var key in new[] { "happy", "sad", "relaxed", "aa", "ih", "ou", "ee", "oh", "blinkLeft", "blinkRight", "surprised", "lookUp" })
            preset[key] = JsonNode.Parse("""{"morphTargetBinds":[{"node":3,"index":0,"weight":0.6}]}""");
        one.Root["extensions"]!["VRMC_vrm"]!["expressions"] = new JsonObject { ["preset"] = preset,
            ["custom"] = JsonNode.Parse("""{"CustomFace":{"morphTargetBinds":[{"node":3,"index":1,"weight":0.25}]}}""") };
        ((JsonArray)one.Root["nodes"]!).Add(new JsonObject { ["mesh"] = 0, ["skin"] = 0 });
        var vrm1 = one.Read();
        foreach (var (source, alias) in new[] { ("happy", "joy"), ("sad", "sorrow"), ("relaxed", "fun"), ("aa", "a"), ("ih", "i"), ("ou", "u"), ("ee", "e"), ("oh", "o"), ("blinkleft", "blink_l"), ("blinkright", "blink_r") })
            Check(ReferenceEquals(vrm1.Expressions[source], vrm1.Expressions[alias]), "VRM1 alias " + alias);
        Check(vrm1.Expressions.ContainsKey("surprised") && vrm1.Expressions.ContainsKey("lookup") && vrm1.Expressions.ContainsKey("CustomFace"), "VRM1 preset case and custom name");
        Check(vrm1.Expressions["joy"].Binds.Count == 1 && vrm1.Expressions["joy"].Binds[0].Weight == .6f, "VRM1 node binds only selected instance");
        var expressions = vrm1.ApplyExpressions(vrm1.CreateShape(), new Dictionary<string, float> { ["joy"] = .5f, ["CustomFace"] = 1 });
        Check(Leaves(expressions).First().MorphWeights!.SequenceEqual([.3f, .25f]), "VRM1 contributions");
        Check(Leaves(expressions).Last().MorphWeights!.SequenceEqual([.5f, .25f]), "unaffected instance preserves defaults");
        preset["happy"]!["morphTargetBinds"]![0]!["node"] = 999;
        Bad(one.Build(), "expression node reference");
        preset["happy"]!["morphTargetBinds"]![0]!["node"] = 3;
        preset["happy"]!["morphTargetBinds"]![0]!["index"] = 2;
        Bad(one.Build(), "expression target reference");
    }

    private static Fixture MorphFixture()
    {
        var f = Skinned();
        var position = f.Accessor(new float[] { 0, 0, 0, 0, 0, 0, .4f, 0, 0, .4f, 0, 0 }, "VEC3");
        var normal = f.Accessor(new float[] { 0, 0, 0, 0, 0, 0, .1f, 0, 0, .1f, 0, 0 }, "VEC3");
        f.Primitive["indices"] = f.Accessor(new ushort[] { 0, 0, 1, 0, 1, 2, 1, 3, 2 }, "SCALAR");
        var sparse = new JsonObject { ["componentType"] = 5126, ["type"] = "VEC3", ["count"] = 4,
            ["sparse"] = new JsonObject { ["count"] = 2,
                ["indices"] = new JsonObject { ["bufferView"] = f.View([2, 3]), ["componentType"] = 5121 },
                ["values"] = new JsonObject { ["bufferView"] = f.View(new float[] { 0, -.2f, 0, 0, -.2f, 0 }.SelectMany(BitConverter.GetBytes).ToArray()) } } };
        f.Accessors.Add(sparse);
        f.Primitive["targets"] = new JsonArray(new JsonObject { ["POSITION"] = position, ["NORMAL"] = normal }, new JsonObject { ["POSITION"] = f.Accessors.Count - 1 });
        f.Mesh["extras"] = new JsonObject { ["targetNames"] = new JsonArray("smile", "blink") };
        f.Mesh["weights"] = Numbers(.5f, .25f);
        return f;
    }

    private static void Malformed()
    {
        var bytes = Basic().Build();
        for (var i = 0; i < bytes.Length; i++) Bad(bytes[..i], "truncation");
        Bad(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\"}]}"), "external JSON");
        Mutate(f => f.Accessors[0]!["byteOffset"] = int.MaxValue, "accessor offset");
        Mutate(f => f.Views[0]!["byteStride"] = 8, "short stride");
        Mutate(f => f.Views[0]!["byteStride"] = 255, "unaligned stride");
        Mutate(f => f.Views[0]!["byteOffset"] = -1, "negative view offset");
        Mutate(f => f.Accessors[0]!["count"] = int.MaxValue, "accessor count");
        Mutate(f => f.Accessors[0]!["sparse"] = new JsonObject(), "sparse");
        Mutate(f => f.Primitive["indices"] = f.Accessor(new byte[] { 0, 1, 3 }, 5121, "SCALAR"), "out of range index");
        Mutate(f => f.Primitive["indices"] = f.Accessor(new uint[] { 0, 1, uint.MaxValue }, "SCALAR"), "overflow index");
        Mutate(f => f.Root["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression"), "Draco");
        Mutate(f => f.Root["extensionsRequired"] = new JsonArray("EXT_meshopt_compression"), "meshopt");
        Mutate(f => f.Root["nodes"]![0]!["children"] = Numbers(0), "cycle");
        Mutate(f => f.Root["nodes"]![0]!["children"] = Numbers(-1), "negative child");
        Mutate(f => f.Root["nodes"] = new JsonArray(Enumerable.Range(0, GltfReader.MaximumHierarchyDepth + 1).Select(i => (JsonNode)new JsonObject { ["children"] = i == GltfReader.MaximumHierarchyDepth ? new JsonArray() : Numbers(i + 1) }).ToArray()), "depth limit");
        Mutate(f => f.Root["nodes"] = new JsonArray(Enumerable.Range(0, GltfReader.MaximumNodes + 1).Select(_ => (JsonNode)new JsonObject()).ToArray()), "node limit");
        var skin = Skinned();
        skin.Root["skins"]![0]!["joints"] = Numbers(Enumerable.Range(0, Skeleton.MaximumJoints + 1).Select(i => (float)i).ToArray());
        Bad(skin.Build(), "joint limit");
        var header = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), GltfReader.MaximumFileSize + 1);
        Bad(header, "file size limit");
        var chunk = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), uint.MaxValue);
        Bad(chunk, "chunk overflow");
        var invalidJson = bytes.ToArray(); invalidJson[20] = (byte)'!'; Bad(invalidJson, "malformed JSON");
        // A deterministic mutation sweep must either parse safely or produce InvalidDataException.
        var random = new Random(43);
        for (var i = 0; i < 300; i++)
        {
            var mutated = bytes.ToArray(); mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            try { using var stream = new MemoryStream(mutated); _ = GltfReader.Read(stream); } catch (InvalidDataException) { }
        }
        void Mutate(Action<Fixture> mutate, string message) { var f = Basic(); mutate(f); Bad(f.Build(), message); }
    }

    private static void Smoke()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "froggy.vrm");
        using var input = File.OpenRead(path);
        var model = GltfReader.Read(input); var shape = model.CreateShape();
        var triangles = Leaves(shape).Sum(s => s.TriangleCount);
        Check(triangles is >= 3300 and <= 3360 && model.Skeletons.Count == 1 && model.Skeletons[0].Joints.Count == 65, "Froggy geometry/skin counts");
        var skeleton = model.Skeletons[0];
        foreach (var bone in new[] { "hips", "leftUpperArm", "rightLowerLeg" }) Check(model.HumanoidBones.ContainsKey(bone), "Froggy " + bone);
        var scene = new Scene().Add(shape); var bounds = scene.GetBounds()!.Value;
        Check(bounds.Size.Y is > 1 and < 3 && bounds.Size.Y > bounds.Size.Z, "Froggy upright human-scale bounds");
        var camera = new Camera(5, MathF.PI, 0); camera.FitToScene(scene);
        var frame = new DepthRenderer().Render(scene, camera, 512, 512, 0xFF123456);
        var coverage = frame.Pixels.Span.ToArray().Count(p => p != 0xFF123456);
        Check(coverage > 1000, "Froggy visible pixels");
        var outputPath = Path.Combine(Path.GetTempPath(), "froggy-gltf.ppm");
        using (var output = File.Create(outputPath))
        {
            output.Write(Encoding.ASCII.GetBytes("P6\n512 512\n255\n"));
            foreach (var pixel in frame.Pixels.Span) output.Write([(byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel]);
        }
        var foot = skeleton.RestPose.WorldTransforms[model.HumanoidBones["leftFoot"]].Translation;
        var toes = skeleton.RestPose.WorldTransforms[model.HumanoidBones["leftToes"]].Translation;
        Console.WriteLine($"FROGGY triangles={triangles} joints={skeleton.Joints.Count} bounds={bounds.Min}..{bounds.Max} size={bounds.Size} facing={(toes.Z < foot.Z ? "-Z" : "+Z")} (foot-to-toes={toes - foot}) coverage={coverage}/262144 ({coverage / 2621.44:F2}%) render={outputPath}");
        Console.WriteLine("FROGGY bones=" + string.Join(", ", model.HumanoidBones.Select(b => $"{b.Key}:{b.Value}={skeleton.Joints[b.Value].Name}")));
        ExpressionSmoke("froggy", 16, ["a", "i", "u", "e", "o", "blink"]);
        ExpressionSmoke("cat", 17, ["joy", "angry", "sorrow", "fun", "blink_l", "blink_r"]);
    }

    // Real VRM 0.x assets: presets resolve and move face vertices; renders each to a PPM for inspection.
    private static void ExpressionSmoke(string name, int targetCount, string[] presets)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", name + ".vrm"));
        var model = GltfReader.Read(input); var rest = model.CreateShape();
        Check(Leaves(rest).Max(s => s.SkinnedMesh?.MorphTargets.Count ?? 0) == targetCount, name + " morph target count");
        foreach (var preset in presets) Check(model.Expressions.ContainsKey(preset), $"{name} preset {preset}");
        var report = new List<string>();
        foreach (var expression in new[] { "joy", "a", "blink" }.Where(model.Expressions.ContainsKey).Append("blink_l").Distinct().Where(model.Expressions.ContainsKey))
        {
            var applied = model.ApplyExpressions(rest, new Dictionary<string, float> { [expression] = 1 });
            var moved = 0; var largest = 0f;
            foreach (var (before, after) in Leaves(rest).Zip(Leaves(applied)))
                for (var t = 0; t < before.TriangleCount; t++)
                    foreach (var (p, q) in new[] { (before.Mesh[t].A, after.Mesh[t].A), (before.Mesh[t].B, after.Mesh[t].B), (before.Mesh[t].C, after.Mesh[t].C) })
                    { var d = Vector3.Distance(p, q); if (d > 1e-5f) moved++; largest = MathF.Max(largest, d); }
            Check(moved > 0 && largest > 1e-3f, $"{name} {expression} moves face vertices");
            var scene = new Scene { Light = new(new(.3f, -.4f, 1)), AmbientLight = .35f }.Add(applied); var fit = new Camera(5, MathF.PI, 0); fit.FitToScene(scene);
            var bounds = scene.GetBounds()!.Value;  // framed on the head
            var camera = new Camera(fit.Distance * .4f, MathF.PI, 0) { Target = new(0, bounds.Max.Y - .2f * bounds.Size.Y, 0) };
            var frame = new DepthRenderer().Render(scene, camera, 256, 256, 0xFF123456);
            var path = Path.Combine(Path.GetTempPath(), $"{name}-{expression}.ppm");
            using (var output = File.Create(path))
            {
                output.Write(Encoding.ASCII.GetBytes("P6\n256 256\n255\n"));
                foreach (var pixel in frame.Pixels.Span) output.Write([(byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel]);
            }
            report.Add($"{expression}: {moved} corners, max {largest:0.0000}");
        }
        Console.WriteLine($"{name.ToUpperInvariant()} expressions={string.Join(",", model.Expressions.Keys)} | {string.Join("; ", report)} | renders={Path.GetTempPath()}{name}-*.ppm");
    }

    private static Fixture Basic()
    {
        var f = new Fixture();
        f.Attributes["POSITION"] = f.Accessor(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, "VEC3");
        return f;
    }

    private static Fixture Skinned()
    {
        var f = Basic();
        f.Attributes["POSITION"] = f.Accessor(new float[] { -.1f, 0, 0, .1f, 0, 0, -.1f, 2, 0, .1f, 2, 0 }, "VEC3");
        f.Primitive["indices"] = f.Accessor(new ushort[] { 0, 1, 2, 1, 3, 2 }, "SCALAR");
        f.Attributes["JOINTS_0"] = f.Accessor(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 5121, "VEC4");
        f.Attributes["WEIGHTS_0"] = f.Accessor(new ushort[] { 65535, 0, 0, 0, 65535, 0, 0, 0, 65535, 0, 0, 0, 65535, 0, 0, 0 }, "VEC4", true);
        f.Root["nodes"] = JsonNode.Parse("""[{"translation":[3,0,0],"children":[1,3]},{"name":"bone","children":[2]},{"name":"bone","translation":[0,1,0]},{"mesh":0,"skin":0,"translation":[100,0,0]}]""");
        var bind = f.Accessor(MatrixValues(Matrix4x4.CreateTranslation(-3, -1, 0)).Concat(MatrixValues(Matrix4x4.CreateTranslation(-3, 0, 0))).ToArray(), "MAT4");
        f.Root["skins"] = new JsonArray(new JsonObject { ["joints"] = Numbers(2, 1), ["inverseBindMatrices"] = bind });
        f.Root["extensions"] = JsonNode.Parse("""{"VRM":{"humanoid":{"humanBones":[{"bone":"hips","node":1},{"bone":"leftUpperArm","node":2}]}},"VRMC_vrm":{"humanoid":{"humanBones":{"rightLowerLeg":{"node":2}}}}}""");
        return f;
    }

    private sealed class Fixture
    {
        internal readonly JsonObject Root = new() { ["asset"] = new JsonObject { ["version"] = "2.0" } };
        internal readonly JsonObject Attributes = new(), Primitive = new(), Mesh = new();
        internal readonly JsonArray Views = [], Accessors = [];
        private readonly MemoryStream bin = new();
        internal Fixture()
        {
            Primitive["attributes"] = Attributes; Mesh["primitives"] = new JsonArray(Primitive);
            Root["meshes"] = new JsonArray(Mesh); Root["nodes"] = JsonNode.Parse("""[{"mesh":0}]""");
            Root["bufferViews"] = Views; Root["accessors"] = Accessors;
        }
        internal int View(byte[] bytes, int stride = 0)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = bytes.Length };
            if (stride != 0) view["byteStride"] = stride;
            Views.Add(view); bin.Write(bytes); return Views.Count - 1;
        }
        internal int AddAccessor(int view, int type, string shape, int count, int offset = 0, bool normalized = false)
        {
            Accessors.Add(new JsonObject { ["bufferView"] = view, ["componentType"] = type, ["type"] = shape, ["count"] = count, ["byteOffset"] = offset, ["normalized"] = normalized });
            return Accessors.Count - 1;
        }
        internal int Accessor(float[] values, string shape) => Accessor(values.SelectMany(BitConverter.GetBytes).ToArray(), 5126, shape);
        internal int Accessor(ushort[] values, string shape, bool normalized = false) => Accessor(values.SelectMany(BitConverter.GetBytes).ToArray(), 5123, shape, normalized);
        internal int Accessor(uint[] values, string shape) => Accessor(values.SelectMany(BitConverter.GetBytes).ToArray(), 5125, shape);
        internal int Accessor(byte[] bytes, int type, string shape, bool normalized = false)
        {
            var components = shape switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
            var size = type == 5121 ? 1 : type == 5123 ? 2 : 4;
            return AddAccessor(View(bytes), type, shape, bytes.Length / (components * size), normalized: normalized);
        }
        internal byte[] Build()
        {
            Root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length });
            var json = Encoding.UTF8.GetBytes(Root.ToJsonString());
            var jsonLength = (json.Length + 3) & ~3; var binLength = ((int)bin.Length + 3) & ~3;
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(0x46546C67u); writer.Write(2u); writer.Write(28 + jsonLength + binLength);
            writer.Write(jsonLength); writer.Write(0x4E4F534Au); writer.Write(json);
            for (var i = json.Length; i < jsonLength; i++) writer.Write((byte)' ');
            writer.Write(binLength); writer.Write(0x004E4942u); writer.Write(bin.ToArray());
            for (var i = bin.Length; i < binLength; i++) writer.Write((byte)0);
            return stream.ToArray();
        }
        internal GltfModel Read()
        {
            using var stream = new MemoryStream(Build()); var model = GltfReader.Read(stream);
            Check(stream.CanRead, "input left open"); return model;
        }
    }

    private static IEnumerable<Shape> Leaves(Shape shape)
    {
        if (shape.TriangleCount > 0) yield return shape;
        foreach (var child in shape.Children) foreach (var leaf in Leaves(child)) yield return leaf;
    }
    private static JsonArray Numbers(params float[] values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    private static float[] MatrixValues(Matrix4x4 m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    private static JsonArray MatrixJson(Matrix4x4 m) => Numbers(MatrixValues(m));
    private static void Bad(byte[] bytes, string message)
    {
        using var stream = new MemoryStream(bytes);
        try { _ = GltfReader.Read(stream); } catch (InvalidDataException) { return; }
        throw new Exception("Accepted malformed GLB: " + message);
    }
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < 1e-4f, $"{message}: {actual} != {expected}");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

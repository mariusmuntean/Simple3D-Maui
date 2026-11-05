using System.Numerics;
using Simple3D.Core;

/// <summary>Measures faces for choosing a model's <see cref="Simple3D.Shared.Mirror.MirrorFace"/> table.</summary>
/// <remarks><c>--face-report a.vrm b.vrm</c> prints how far each expression moves vertices, as a share of head height, and
/// where; <c>--face-report combo a.vrm a+o a+o+e</c> does the same for blends; <c>--face-report stats face-46379</c> prints
/// the tracked face values of a fixture per half second.</remarks>
internal static class MirrorFaceReport
{
    internal static int Run(string[] files)
    {
        if (files[0] == "combo")
        {
            using var asset = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", files[1]));
            var model = GltfReader.Read(asset);
            var h = HeadBounds(model); var height = h.Max.Y - h.Min.Y;
            foreach (var combo in files[2..])
            {
                var sums = new Dictionary<(SkinnedMesh, int), Vector3>();
                foreach (var name in combo.Split('+'))
                    foreach (var (mesh, target, weight) in model.Expressions[name].Binds)
                        for (var v = 0; v < mesh.Mesh.Vertices.Count; v++)
                            sums[(mesh, v)] = sums.GetValueOrDefault((mesh, v)) + mesh.MorphTargets[target].PositionDeltas[v] * weight;
                Console.WriteLine($"{files[1]} {combo}: {sums.Values.Max(d => d.Length()) / height:P1}");
            }
            return 0;
        }
        if (files[0] == "stats")
        {
            var session = new Simple3D.Shared.Mirror.MirrorSession(MirrorRender.Character("cat.vrm"));
            var rows = new List<(double T, Simple3D.Shared.Mirror.FaceFrame F)>();
            foreach (var frame in MirrorRender.Frames(files[1]))
            {
                session.Apply(frame, false);
                if (session.Tracker.Face is { } f) rows.Add((frame.Timestamp.TotalSeconds, f));
            }
            for (var t = 0.0; t < rows[^1].T; t += .5)
            {
                var seg = rows.Where(r => r.T >= t && r.T < t + .5).Select(r => r.F).ToList();
                if (seg.Count == 0) continue;
                Console.WriteLine($"{t,5:0.0}s mouth {seg.Average(f => f.MouthOpen):0.00} max {seg.Max(f => f.MouthOpen):0.00} smile {seg.Average(f => f.Smile):0.00} brows {seg.Average(f => f.Brows):0.00} blinkL {seg.Average(f => f.BlinkLeft):0.00} max {seg.Max(f => f.BlinkLeft):0.00} blinkR {seg.Average(f => f.BlinkRight):0.00} max {seg.Max(f => f.BlinkRight):0.00}");
            }
            return 0;
        }
        foreach (var file in files)
        {
            using var asset = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", file));
            var model = GltfReader.Read(asset);
            var head = HeadBounds(model);
            var height = head.Max.Y - head.Min.Y;
            var ch = Simple3D.Shared.Mirror.MirrorCharacter.FromVrm(model, model.CreateShape());
            var sb = new Scene().Add(ch.Shape).GetBounds()!.Value; var hb = ch.HeadBounds()!.Value;
            foreach (var leaf in Leaves(ch.Shape).Where(l => l.SkinnedMesh is not null)) Console.WriteLine($"   leaf '{leaf.Name}' targets {leaf.SkinnedMesh!.MorphTargets.Count}");
            Console.WriteLine($"   scene {sb.Min} {sb.Max}; body {hb.Body.Min} {hb.Body.Max}; head {hb.Head.Min} {hb.Head.Max}");
            Console.WriteLine($"== {file}: head y {head.Min.Y:0.000}..{head.Max.Y:0.000} (height {height:0.000}), x {head.Min.X:0.000}..{head.Max.X:0.000}");
            foreach (var (name, expression) in model.Expressions.OrderBy(e => e.Key))
            {
                float max = 0, maxY = 0; var low = new Vector3(float.MaxValue); var high = new Vector3(float.MinValue); var moved = 0;
                foreach (var group in expression.Binds.GroupBy(b => b.Mesh))
                {
                    var mesh = group.Key;
                    for (var v = 0; v < mesh.Mesh.Vertices.Count; v++)
                    {
                        var d = Vector3.Zero;
                        foreach (var (_, target, weight) in group) d += mesh.MorphTargets[target].PositionDeltas[v] * weight;
                        var length = d.Length();
                        if (length < height * .005f) continue;
                        moved++;
                        var p = mesh.Mesh.Vertices[v];
                        low = Vector3.Min(low, p); high = Vector3.Max(high, p);
                        if (length > max) { max = length; maxY = MathF.Abs(d.Y); }
                    }
                }
                var region = moved == 0 ? "-" : $"y {(low.Y - head.Min.Y) / height:0.00}..{(high.Y - head.Min.Y) / height:0.00} x {low.X:0.000}..{high.X:0.000}";
                Console.WriteLine($"  {name,-14} binds={expression.Binds.Count} binary={expression.IsBinary} max={max / height * 100:0.0}% (vertical {maxY / height * 100:0.0}%) moved={moved} {region}");
            }
        }
        return 0;
    }

    /// <summary>Rest-pose bounds of vertices mostly weighted to the head or its descendants.</summary>
    internal static (Vector3 Min, Vector3 Max) HeadBounds(GltfModel model)
    {
        var skeleton = model.Skeletons[0];
        var headJoint = model.HumanoidBones["head"];
        var inHead = new bool[skeleton.Joints.Count];
        for (var j = 0; j < inHead.Length; j++) inHead[j] = j == headJoint || (skeleton.Joints[j].Parent >= 0 && inHead[skeleton.Joints[j].Parent]);
        var ys = new List<Vector3>();
        foreach (var mesh in Leaves(model.CreateShape()).Select(l => l.SkinnedMesh).OfType<SkinnedMesh>().Distinct())
            for (var v = 0; v < mesh.Mesh.Vertices.Count; v++)
            {
                var (joints, weights) = mesh.Influences[v];
                float w = 0;
                for (var i = 0; i < 4; i++) if (joints[i] < inHead.Length && inHead[joints[i]]) w += i switch { 0 => weights.X, 1 => weights.Y, 2 => weights.Z, _ => weights.W };
                if (w > .5f) ys.Add(mesh.Mesh.Vertices[v]);
            }
        return (ys.Aggregate(Vector3.Min), ys.Aggregate(Vector3.Max));
    }
    static IEnumerable<Shape> Leaves(Shape s) => s.Children.Count == 0 ? [s] : s.Children.SelectMany(Leaves);
}

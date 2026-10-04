using System.IO.Compression;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Simple3D.Core;

var box = Shape.Box().Named("Package box");
var scene = new Scene().Add(box);
var camera = new Camera();
camera.FitToScene(scene, 1);
var renderer = new DepthRenderer();
var frame = renderer.Render(scene, camera, 128, 128);
var retained = frame.Pixels.ToArray();
var target = new RenderTarget(128, 128);
renderer.RenderInto(scene, camera, target);
Require(frame.Pixels.Span.SequenceEqual(target.Pixels.Span), "Owned and reusable pixels differ");
Require(ReferenceEquals(target.Pick(64, 64), box), "Packaged renderer cannot pick the visible box");
scene.Replace(box, box.At(10, 0, 0));
renderer.RenderInto(scene, camera, target);
Require(frame.Pixels.Span.SequenceEqual(retained), "Owned snapshot changed after render");
Console.WriteLine("PACKAGE_CORE_PASS: pixels, picking and retained snapshots");

if (args.Length == 0) return;
foreach (var name in new[] { "Simple3D.Core", "Simple3D.Maui" })
{
    var path = Path.Combine(args[0], $"{name}.0.1.0-preview.1.nupkg");
    using var package = ZipFile.OpenRead(path);
    var nuspec = XDocument.Load(package.Entries.Single(e => e.FullName.EndsWith(".nuspec")).Open());
    var metadata = nuspec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    var repository = metadata.Elements().Single(e => e.Name.LocalName == "repository");
    var commit = (string?)repository.Attribute("commit");
    Require(commit?.Length == 40, "Repository commit is missing");
    Require((string?)repository.Attribute("url") == "https://github.com/mariusmuntean/Simple3D-Maui", "Repository URL differs");
    Require(metadata.Elements().Any(e => e.Name.LocalName == "license" && e.Value == "MIT"), "MIT metadata missing");
    Require(package.GetEntry("README.md") != null && package.GetEntry("package-icon.png") != null, "Package assets missing");
    var assemblies = package.Entries.Where(e => e.FullName.StartsWith("lib/") && e.FullName.EndsWith(".dll")).ToArray();
    Require(assemblies.Length == (name.EndsWith("Core") ? 1 : 3), "Unexpected framework asset count");
    var frameworks = assemblies.Select(e => e.FullName.Split('/')[1]).ToArray();
    Require(name.EndsWith("Core")
        ? frameworks.SequenceEqual(new[] { "net10.0" })
        : new[] { "net10.0-android", "net10.0-ios", "net10.0-maccatalyst" }
            .All(tfm => frameworks.Count(actual => actual.StartsWith(tfm, StringComparison.Ordinal)) == 1),
        "Package framework identities differ from supported platforms");
    using var symbols = ZipFile.OpenRead(Path.ChangeExtension(path, ".snupkg"));
    foreach (var assembly in assemblies)
    {
        Require(package.GetEntry(Path.ChangeExtension(assembly.FullName, ".xml")) != null, "XML documentation missing");
        var entry = symbols.GetEntry(Path.ChangeExtension(assembly.FullName, ".pdb"));
        Require(entry != null, "Portable symbols missing");
        using var stream = new MemoryStream();
        using (var source = entry!.Open()) source.CopyTo(stream);
        stream.Position = 0;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var info = reader.CustomDebugInformation.Select(reader.GetCustomDebugInformation)
            .Single(d => reader.GetGuid(d.Kind) == new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"));
        using var mapping = JsonDocument.Parse(Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value)));
        var mappings = mapping.RootElement.GetProperty("documents").EnumerateObject().ToArray();
        Require(mappings.Any(m => m.Value.GetString()!.Contains($"/Simple3D-Maui/{commit}/")), "Source Link does not match package commit");
        Require(reader.Documents.All(h => !reader.GetString(reader.GetDocument(h).Name).Contains("/Users/")), "PDB leaks local source paths");
        var document = reader.Documents.Select(reader.GetDocument).First(d => reader.GetString(d.Name).EndsWith(name.EndsWith("Core") ? "/DepthRenderer.cs" : "/SceneView.cs"));
        var sourcePath = reader.GetString(document.Name);
        var sourceMapping = mappings.Single(m => sourcePath.StartsWith(m.Name.TrimEnd('*'), StringComparison.Ordinal));
        var url = sourceMapping.Value.GetString()!.Replace("*", sourcePath[sourceMapping.Name.TrimEnd('*').Length..]);
        using var client = new HttpClient();
        var sourceBytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
        Require(reader.GetGuid(document.HashAlgorithm) == new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), "Unexpected source checksum algorithm");
        Require(SHA256.HashData(sourceBytes).SequenceEqual(reader.GetBlobBytes(document.Hash)), "Source Link bytes differ from the compiled source");
    }
    if (name.EndsWith("Maui"))
    {
        var groups = metadata.Descendants().Where(e => e.Name.LocalName == "group").ToArray();
        Require(groups.Length == 3, "MAUI dependency groups missing");
        foreach (var group in groups)
            foreach (var dependency in new[] { "Simple3D.Core", "Microsoft.Maui.Controls", "SkiaSharp.Views.Maui.Controls" })
                Require(group.Elements().Any(e => e.Name.LocalName == "dependency" && (string?)e.Attribute("id") == dependency),
                    $"Dependency {dependency} missing from {group.Attribute("targetFramework")}");
    }
    Console.WriteLine($"PACKAGE_METADATA_PASS: {name}, symbols, Source Link, docs and assets");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

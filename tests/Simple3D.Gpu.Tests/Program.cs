using System.Diagnostics;
using System.Numerics;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;

const int width = 256, height = 256;
const uint background = 0xFF18243B;
using var context = new EglContext(width, height);
using var renderer = new GlesRenderer();
Console.WriteLine("Renderer: " + renderer.Name);
Console.WriteLine("GLES API validation; a Mesa software driver does NOT establish physical GPU performance.");
var prepared = new GpuScene();
var camera = new Camera(5, 0, 0);
var near = Shape.Box(0xFFFF0000).WithMaterial(new Material(0xFFFF0000, lit: false)).At(0, 0, 1);
var far = Shape.Box(0xFF0000FF).WithMaterial(new Material(0xFF0000FF, lit: false));
var scene = new Scene().Add(near).Add(far);
var pixels = Render(scene, camera);
Check(pixels[128 * width + 128] == 0xFFFF0000, "GPU depth must keep the near shape");
Check(pixels[0] == background, "GPU clear color/channel order");
Check(ReferenceEquals(prepared.Pick(128.5, 128.5), near), "GPU geometric pick");
Render(scene, camera);
Check(renderer.GeometryUploads == 0 && renderer.TextureUploads == 0, "unchanged frame re-uploaded resources");
scene.Replace(near, near.Rotated(.2f, .3f, 0)); Render(scene, camera);
Check(renderer.GeometryUploads == 0, "animation re-uploaded immutable geometry");
Console.WriteLine("PASS depth, clear color, picking, geometry and texture reuse");

var texture = new Texture2D(2, 2, new uint[] { 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00 });
var mesh = new Mesh(new Vector3[] { new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0) }, new[] { 0, 1, 2, 0, 2, 3 },
    new Vector2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
scene = new Scene().Add(Shape.FromMesh(mesh, new Material(texture, lit: false)));
pixels = Render(scene, camera);
Check(pixels[100 * width + 100] == 0xFFFF0000 && pixels[150 * width + 100] == 0xFF0000FF, "texture row orientation");
Check(pixels[100 * width + 150] == 0xFF00FF00, "texture RGB channels");
Compare(scene, camera, "nearest texture");
scene = new Scene().Add(Shape.FromMesh(mesh, new Material(new Texture2D(2, 2, texture.Pixels, TextureFilter.Bilinear), lit: false)));
Compare(scene, camera, "bilinear texture");
scene = new Scene().Add(Shape.FromMesh(mesh, new Material(0xFFFFFFFF, doubleSided: false)).Rotated(0, MathF.PI, 0));
Check(Render(scene, camera).All(p => p == background), "single-sided backfaces must be culled");
camera.Projection = CameraProjection.Orthographic; Compare(new Scene().Add(far), camera, "orthographic");
camera.Projection = CameraProjection.Perspective;
var clipped = new Mesh(new Vector3[] { new(-2, -1, 4.8f), new(2, -1, -1), new(0, 2, 0) }, new[] { 0, 1, 2 });
camera.NearPlane = .5f;
Compare(new Scene().Add(Shape.FromMesh(clipped, new Material(0xFFABCDEF, lit: false))), camera, "near clipping");
camera.NearPlane = .05f;
var selected = Shape.Box(); scene = new Scene().Add(selected);
var ordinary = Render(scene, camera); var highlighted = Render(scene, camera, selected);
Check(!ordinary.SequenceEqual(highlighted), "selection tint/contour missing");
Check(ordinary[0] == highlighted[0], "selection changed unrelated background");
Console.WriteLine("PASS texture orientation/filtering, culling, projections, clipping and selection");

foreach (var sample in DemoScenes.All) Compare(sample.Scene, sample.Camera, sample.Name);
// Non-invertible transform (collapsed axis): must render and light like software instead of failing.
var panel = Shape.Box(0xFFE0A030).Scaled(1e-24f, 1.5f, 1.5f).Rotated(0, .6f, 0);
Compare(new Scene().Add(panel), new Camera(5, .3f, .2f), "collapsed-axis lit panel");
Check(Render(new Scene().Add(panel), new Camera(5, .3f, .2f)).Count(p => p != background) > 2000, "collapsed-axis panel must be visible");
// Many items with distinct per-item uniforms share one per-frame uniform upload.
var grid = new Scene();
for (var i = 0; i < 64; i++)
    grid.Add(Shape.Box((uint)(0xFF000000 | (i * 0x030507 & 0xFFFFFF) | 0x404040)).WithMaterial(new Material((uint)(0xFF000000 | (i * 0x030507 & 0xFFFFFF) | 0x404040), lit: false)).At(i % 8 - 3.5f, i / 8 - 3.5f, 0).Scaled(.4f));
Compare(grid, new Camera(12, 0, 0), "64 items, per-item uniform ranges");
// Resizing must recreate attachments without leaking stale content.
prepared.Update(scene, camera, 64, 64); renderer.Render(new(prepared, scene, camera, null, background), 64, 64);
Check(renderer.ReadPixels().Length == 64 * 64, "resized readback");
Render(new Scene(), camera); Check(renderer.ReadPixels().All(p => p == background), "empty scene retained geometry");
Console.WriteLine("PASS gallery comparisons, resize and empty scene");
if (args.Contains("--performance"))
{
    foreach (var sample in DemoScenes.All)
    {
        for (var i = 0; i < 5; i++) Render(sample.Scene, sample.Camera, readback: false);
        var completed = new double[30]; var submissions = new double[30];
        for (var i = 0; i < 30; i++)
        {
            sample.Camera.Orbit(.01f, 0); prepared.Update(sample.Scene, sample.Camera, width, height);
            var start = Stopwatch.GetTimestamp();
            submissions[i] = renderer.Render(new(prepared, sample.Scene, sample.Camera, null, background), width, height);
            Gles.glFinish(); completed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Check(renderer.GeometryUploads == 0 && renderer.TextureUploads == 0, "warm benchmark uploaded unchanged assets");
        }
        Array.Sort(completed); Array.Sort(submissions);
        Console.WriteLine($"PERF {sample.Name}: CPU submit median={submissions[15]:F3}ms; completed median={completed[15]:F3}ms p95={completed[28]:F3}ms; {width}x{height}; not displayed FPS");
    }
}
Console.WriteLine("GPU_TESTS_PASS");
return 0;

uint[] Render(Scene s, Camera c, Shape? selection = null, bool readback = true)
{
    prepared.Update(s, c, width, height); renderer.Render(new(prepared, s, c, selection, background), width, height);
    return readback ? renderer.ReadPixels() : Array.Empty<uint>();
}
void Compare(Scene s, Camera c, string name)
{
    var actual = Render(s, c); var expected = new DepthRenderer().Render(s, c, width, height, background).Pixels.Span;
    var mismatches = 0;
    for (var i = 0; i < actual.Length; i++)
    {
        var a = actual[i]; var e = expected[i];
        if (Math.Abs((int)((a >> 16) & 255) - (int)((e >> 16) & 255)) > 4 || Math.Abs((int)((a >> 8) & 255) - (int)((e >> 8) & 255)) > 4 || Math.Abs((int)(a & 255) - (int)(e & 255)) > 4) mismatches++;
    }
    // Raster edge rules and per-vertex lighting rounding differ; do not require bitwise equality.
    Check(mismatches < actual.Length * .03, $"{name}: {mismatches}/{actual.Length} GPU pixels disagree with software");
    Console.WriteLine($"PASS {name}: {mismatches}/{actual.Length} pixels beyond channel tolerance 4");
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

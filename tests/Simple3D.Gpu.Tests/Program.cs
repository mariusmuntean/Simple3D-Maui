using System.Numerics;
using Simple3D.Core;
using Simple3D.Maui;

// GLES-specific checks here; the contract shared with the on-device Metal/GLES probes (gallery
// comparisons, direct vs composite, selection outline, empty scene, timings) is NativeGpuValidation.
const int width = NativeGpuValidation.Size, height = NativeGpuValidation.Size;
const uint background = NativeGpuValidation.Background;
using var context = new EglContext(width, height);
using var renderer = new GlesRenderer();
Console.WriteLine("Renderer: " + renderer.Name);
Console.WriteLine("GLES API validation; a Mesa software driver does NOT establish physical GPU performance.");
var prepared = new GpuScene();
var camera = new Camera(5, 0, 0);
var near = Shape.Box(0xFFFF0000).WithMaterial(new Material(0xFFFF0000, lit: false)).At(0, 0, 1);
var far = Shape.Box(0xFF0000FF).WithMaterial(new Material(0xFF0000FF, lit: false));
var scene = new Scene().Add(near).Add(far);
Render(scene, camera);
Check(ReferenceEquals(prepared.Pick(128.5, 128.5), near), "GPU geometric pick");
Render(scene, camera);
Check(renderer.GeometryUploads == 0 && renderer.TextureUploads == 0, "unchanged frame re-uploaded resources");
scene.Replace(near, near.Rotated(.2f, .3f, 0)); Render(scene, camera);
Check(renderer.GeometryUploads == 0, "animation re-uploaded immutable geometry");
Check(renderer.SupportsDirect && renderer.LastFrameDirect, "frames without a visible selection must render directly to the window surface");
Console.WriteLine("PASS picking, geometry and texture reuse, direct path");

var texture = new Texture2D(2, 2, new uint[] { 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00 });
var mesh = new Mesh(new Vector3[] { new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0) }, new[] { 0, 1, 2, 0, 2, 3 },
    new Vector2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
scene = new Scene().Add(Shape.FromMesh(mesh, new Material(texture, lit: false)));
var pixels = Render(scene, camera);
Check(pixels[100 * width + 100] == 0xFFFF0000 && pixels[150 * width + 100] == 0xFF0000FF, "texture row orientation");
Check(pixels[100 * width + 150] == 0xFF00FF00, "texture RGB channels");
Compare(scene, camera, "nearest texture");
Compare(new Scene().Add(Shape.FromMesh(mesh, new Material(new Texture2D(2, 2, texture.Pixels, TextureFilter.Bilinear), lit: false))), camera, "bilinear texture");
scene = new Scene().Add(Shape.FromMesh(mesh, new Material(0xFFFFFFFF, doubleSided: false)).Rotated(0, MathF.PI, 0));
Check(Render(scene, camera).All(p => p == background), "single-sided backfaces must be culled");
camera.Projection = CameraProjection.Orthographic; Compare(new Scene().Add(far), camera, "orthographic");
camera.Projection = CameraProjection.Perspective;
var clipped = new Mesh(new Vector3[] { new(-2, -1, 4.8f), new(2, -1, -1), new(0, 2, 0) }, new[] { 0, 1, 2 });
camera.NearPlane = .5f;
Compare(new Scene().Add(Shape.FromMesh(clipped, new Material(0xFFABCDEF, lit: false))), camera, "near clipping");
camera.NearPlane = .05f;
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
prepared.Update(scene, camera, 64, 64); renderer.Render(new(prepared, scene, camera, null, background), 64, 64, 64, 64);
Check(renderer.ReadPixels(64, 64).Length == 64 * 64, "resized readback");
Console.WriteLine("PASS texture orientation/filtering, culling, projections, clipping, uniforms and resize");

NativeGpuValidation.Run(renderer.Name, (s, c, options) =>
{
    renderer.ForceComposite = options.ForceComposite;
    prepared.Update(s, c, width, height);
    renderer.Render(new(prepared, s, c, options.Selected, background, options.OutlineRadius), width, height, width, height);
    if (options.SkipReadback) { Gles.glFinish(); return []; }
    return renderer.ReadPixels(width, height);
}, () => renderer.LastFrameDirect, () => (renderer.GeometryUploads, renderer.TextureUploads, renderer.PaletteUploads, renderer.PaletteUploadBytes));
Console.WriteLine("GPU_TESTS_PASS");
return 0;

uint[] Render(Scene s, Camera c)
{
    renderer.ForceComposite = false;
    prepared.Update(s, c, width, height); renderer.Render(new(prepared, s, c, null, background), width, height, width, height);
    return renderer.ReadPixels(width, height);
}
void Compare(Scene s, Camera c, string name)
{
    var mismatches = NativeGpuValidation.Mismatches(Render(s, c), new DepthRenderer().Render(s, c, width, height, background).Pixels.Span);
    Check(mismatches < width * height * .03, $"{name}: {mismatches}/{width * height} GPU pixels disagree with software");
    Console.WriteLine($"PASS {name}: {mismatches}/{width * height} pixels beyond channel tolerance 4");
}
void Check(bool value, string message) { if (!value) throw new Exception(message); }

using System.Numerics;
using Simple3D.Core;

var mesh = new Mesh([new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)], [0, 1, 2]);
var first = Shape.FromMesh(mesh, new Material(0xFF80B2FF)).Named("sample");
var scene = new Scene().Add(first);
var camera = new Camera(5, 0, 0);
var renderer = new DepthRenderer();
var owned = renderer.Render(scene, camera, 96, 80);
var target = new RenderTarget(96, 80);
renderer.RenderInto(scene, camera, target);
if (!owned.Pixels.Span.SequenceEqual(target.Pixels.Span) || target.Pick(48, 40) != first)
    throw new InvalidOperationException("Packaged rendering or picking failed.");
var next = first.At(.2f, 0, 0);
scene.Replace(first, next);
renderer.RenderInto(scene, camera, target);
if (target.Pick(48, 40) != next || owned.Pick(48, 40) != first)
    throw new InvalidOperationException("Packaged target and snapshot semantics failed.");
camera.FitToScene(scene, 4f / 3);
Console.WriteLine("Packaged Core consumer passed.");

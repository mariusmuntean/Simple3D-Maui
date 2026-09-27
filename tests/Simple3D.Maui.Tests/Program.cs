using Microsoft.Maui.Controls;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;

var tests = new (string Name, Action Run)[]
{
    ("frame caching follows scene and camera mutations", () =>
    {
        var scene = new Scene().Add(Shape.Box());
        var camera = new Camera(5, 0, 0);
        var view = new SceneView { Scene = scene, Camera = camera };
        var initial = view.CaptureFrame(96, 80);
        Assert(ReferenceEquals(initial, view.CaptureFrame(96, 80)), "unchanged frame was rerendered");
        scene.Add(Shape.Sphere().At(1, 0, 0));
        Assert(!ReferenceEquals(initial, view.CaptureFrame(96, 80)), "scene mutation kept stale frame");
        var second = view.CaptureFrame(96, 80);
        camera.Orbit(.2f, 0);
        Assert(!ReferenceEquals(second, view.CaptureFrame(96, 80)), "camera mutation kept stale frame");
    }),
    ("replaced scene and camera no longer invalidate the view", () =>
    {
        var oldScene = new Scene().Add(Shape.Box());
        var oldCamera = new Camera();
        var view = new SceneView { Scene = oldScene, Camera = oldCamera };
        view.Scene = new Scene().Add(Shape.Sphere());
        view.Camera = new Camera();
        var frame = view.CaptureFrame(48, 48);
        oldScene.Add(Shape.Pyramid());
        oldCamera.Orbit(.5f, 0);
        Assert(ReferenceEquals(frame, view.CaptureFrame(48, 48)), "old objects still subscribed");
    }),
    ("picking maps layout coordinates to rendered pixels", () =>
    {
        var box = Shape.Box().Named("center");
        var view = new SceneView { Scene = new Scene().Add(box), Camera = new Camera(5, 0, 0) };
        view.CaptureFrame(120, 80);
        Assert(ReferenceEquals(view.PickAt(60, 40, 120, 80), box), "center was not picked");
        Assert(ReferenceEquals(view.PickAt(30, 20, 60, 40), box), "half-size layout mapped incorrectly");
        Assert(view.PickAt(-1, 20, 60, 40) is null, "outside layout picked a shape");
    }),
    ("picking refreshes a scene changed since the last paint", () =>
    {
        var scene = new Scene().Add(Shape.Box());
        var view = new SceneView { Scene = scene, Camera = new Camera(5, 0, 0) };
        view.CaptureFrame(80, 80);
        Assert(view.PickAt(40, 40, 80, 80) is not null, "initial pick missed");
        scene.Clear();
        Assert(view.PickAt(40, 40, 80, 80) is null, "stale shape was picked");
    }),
    ("pinch applies incremental updates", () =>
    {
        var actual = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        var expected = new SceneView { Scene = actual.Scene };
        var pinch = (IPinchGestureController)actual.GestureRecognizers.OfType<PinchGestureRecognizer>().Single();
        pinch.SendPinchStarted(actual, new Microsoft.Maui.Graphics.Point(.5, .5));
        foreach (var factor in new[] { 1.1, 1.1, 1.1 })
        {
            pinch.SendPinch(actual, factor, new Microsoft.Maui.Graphics.Point(.5, .5));
            expected.Zoom((float)factor);
        }
        Assert(actual.CaptureFrame(96, 96).Pixels.Span.SequenceEqual(expected.CaptureFrame(96, 96).Pixels.Span), "pinch changed scale incorrectly");
    }),
    ("trackpad pinch keeps zooming throughout each gesture", () =>
    {
        var view = new SceneView { Camera = new Camera(10) };
        view.ApplyMacPinch(GestureStatus.Started, 1);
        view.ApplyMacPinch(GestureStatus.Running, 1.1);
        view.ApplyMacPinch(GestureStatus.Running, 1.2);
        view.ApplyMacPinch(GestureStatus.Running, 1.5);
        Assert(Math.Abs(view.Camera.Distance - 10f / 1.5f) < .0001f, "later updates did not continue zooming");
        view.ApplyMacPinch(GestureStatus.Completed, 1.5);
        view.ApplyMacPinch(GestureStatus.Started, 1);
        view.ApplyMacPinch(GestureStatus.Running, .8);
        Assert(Math.Abs(view.Camera.Distance - 10f / 1.2f) < .0001f, "next gesture did not reset scale");
    }),
    ("orbit preview restores full resolution after mouse drag", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        var pan = (IPanGestureController)view.GestureRecognizers.OfType<PanGestureRecognizer>().Single();
        pan.SendPanStarted(view, 1);
        pan.SendPan(view, 24, 0, 1);
        var preview = view.CaptureSurfaceFrame(2048, 630);
        Assert(preview.Width == 1024 && preview.Height == 315, "drag preview still renders at full desktop resolution");
        pan.SendPanCompleted(view, 1);
        var final = view.CaptureSurfaceFrame(2048, 630);
        Assert(final.Width == 2048 && final.Height == 630, "full resolution was not restored after drag");
    }),
    ("oversize surfaces keep their aspect ratio", () =>
    {
        var size = SceneView.RenderSize(4000, 1000);
        Assert(size.Width == 2048 && size.Height == 512, "render size distorted the surface");
    }),
    ("zoomed gallery stays renderable within raster budget", () =>
    {
        var sample = DemoScenes.Packing();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
        for (var i = 0; i < 5; i++) view.Zoom(1.25f);
        var frame = view.CaptureSurfaceFrame(1170, 1320);
        Assert(frame.Width < 1170 && frame.Height < 1320, "budget did not lower resolution");
        Assert(frame.Pixels.Span.ToArray().Any(p => p != 0xFFF4F6FA), "gallery disappeared");
    }),
    ("label size follows display density", () =>
    {
        Assert(Math.Abs(SceneView.LabelFontSize(1170, 390) - 42) < .001f, "3x label is too small");
    })
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

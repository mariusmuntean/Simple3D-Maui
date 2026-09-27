using Microsoft.Maui.Controls;
using Simple3D.Core;
using Simple3D.Maui;

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

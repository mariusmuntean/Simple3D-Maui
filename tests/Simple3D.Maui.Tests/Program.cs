using Microsoft.Maui.Controls;
using System.Runtime.InteropServices;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;
using Simple3D.Demo;
using SkiaSharp;

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
    ("native paint bitmap is reused and tracks rendered pixels", () =>
    {
        var scene = new Scene().Add(Shape.Box());
        var view = new SceneView { Scene = scene };
        var first = view.CapturePaintTarget(96, 80);
        var bitmap = view.PaintBitmap(first);
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(first)), "unchanged paint allocated a new bitmap");
        Assert(bitmap.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(first.Pixels.Span)),
            "first bitmap pixels differ from frame");
        using var painted = new SKBitmap(new SKImageInfo(96, 80, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(painted);
        canvas.DrawBitmap(bitmap, 0, 0);
        var before = painted.GetPixelSpan().ToArray();
        scene.Add(Shape.Sphere().At(1, 0, 0));
        var second = view.CapturePaintTarget(96, 80);
        Assert(ReferenceEquals(first, second), "scene mutation replaced the render target");
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(second)), "same-size paint allocated a new bitmap");
        Assert(bitmap.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(second.Pixels.Span)),
            "updated bitmap pixels differ from frame");
        canvas.DrawBitmap(bitmap, 0, 0);
        Assert(!before.SequenceEqual(painted.GetPixelSpan().ToArray()), "native paint stayed stale after a scene update");
        Assert(painted.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(second.Pixels.Span)),
            "native paint differs from the updated frame");
        var resized = view.PaintBitmap(view.CapturePaintTarget(64, 64));
        Assert(!ReferenceEquals(bitmap, resized), "resized paint kept the old bitmap");
        view.ReleasePaintBitmap();
        Assert(!ReferenceEquals(resized, view.PaintBitmap(view.CapturePaintTarget(64, 64))),
            "released native bitmap was reused");
        view.ReleasePaintBitmap();
    }),
    ("native paints reuse a render target while captures stay owned", () =>
    {
        var scene = new Scene().Add(Shape.Box().Named("box"));
        var view = new SceneView { Scene = scene, Camera = new Camera(5, 0, 0) };
        var target = view.CapturePaintTarget(160, 120);
        var bitmap = view.PaintBitmap(target);
        var owned = view.CaptureFrame(160, 120);
        var original = owned.Pixels.ToArray();
        Assert(target.Pixels.Span.SequenceEqual(owned.Pixels.Span), "native target differs from owned capture");
        Assert(ReferenceEquals(target, view.CapturePaintTarget(160, 120)), "unchanged native paint replaced target");
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(target)), "unchanged native paint replaced bitmap");
        Assert(ReferenceEquals(view.PickAt(80, 60, 160, 120), scene.Shapes[0]), "native picking missed box");

        scene.Replace(scene.Shapes[0], Shape.Sphere(0xFFFFA66F).Named("sphere"));
        Assert(ReferenceEquals(view.PickAt(80, 60, 160, 120), scene.Shapes[0]),
            "native picking stayed stale before repaint");
        Assert(ReferenceEquals(target, view.CapturePaintTarget(160, 120)), "scene update replaced pixel storage");
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(target)), "scene update replaced native bitmap");
        Assert(!target.Pixels.Span.SequenceEqual(original), "native pixels stayed stale");
        Assert(ReferenceEquals(view.PickAt(80, 60, 160, 120), scene.Shapes[0]), "native picking stayed stale");
        Assert(owned.Pixels.Span.SequenceEqual(original), "owned capture was overwritten");
        Assert(view.CaptureFrame(160, 120).Pixels.Span.SequenceEqual(target.Pixels.Span),
            "owned capture remained stale after native paint");

        Assert(!ReferenceEquals(target, view.CapturePaintTarget(80, 80)), "resized native paint kept old target");
        view.ReleaseRenderResources();
    }),
    ("animated native paint keeps per-frame allocation bounded", () =>
    {
        var sample = DemoScenes.Surface();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera, MaximumRenderDimension = 768 };
        var target = view.CapturePaintTarget(768, 576);
        var bitmap = view.PaintBitmap(target);
        for (var i = 0; i < 5; i++)
        {
            sample.Animate(i / 60f);
            view.PaintBitmap(view.CapturePaintTarget(768, 576));
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 30; i++)
        {
            sample.Animate(i / 60f);
            Assert(ReferenceEquals(target, view.CapturePaintTarget(768, 576)), "animation replaced target");
            Assert(ReferenceEquals(bitmap, view.PaintBitmap(target)), "animation replaced bitmap");
        }
        var bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 30;
        Assert(bytesPerFrame < 100_000, $"animation allocated {bytesPerFrame:N0} bytes/frame");
        Console.WriteLine($"Native paint path (animated Surface, 768x576): {bytesPerFrame:N0} managed bytes/frame on this runner");
        view.ReleaseRenderResources();
    }),
    ("disconnected view releases buffers without invalidating owned snapshots", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        var frame = view.CaptureFrame(96, 80);
        var pixels = frame.Pixels.ToArray();
        var bitmap = view.PaintBitmap(view.CapturePaintTarget(96, 80));
        view.ReleaseRenderResources();
        Assert(frame.Pixels.Span.SequenceEqual(pixels), "retained snapshot changed after view release");
        Assert(!ReferenceEquals(frame, view.CaptureFrame(96, 80)), "view retained its old frame");
        Assert(!ReferenceEquals(bitmap, view.PaintBitmap(view.CapturePaintTarget(96, 80))),
            "view retained its native bitmap");
        view.ReleaseRenderResources();
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
        var preview = view.CapturePaintTarget(2048, 630);
        Assert(preview.Width == 1024 && preview.Height == 315, "drag preview still renders at full desktop resolution");
        pan.SendPanCompleted(view, 1);
        var final = view.CapturePaintTarget(2048, 630);
        Assert(final.Width == 2048 && final.Height == 630, "full resolution was not restored after drag");
    }),
    ("display only view ignores gestures and can enable them later", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()), Camera = new Camera(5, 0, 0), IsInteractive = false };
        Assert(view.GestureRecognizers.Count == 0, "display only view still captures gestures");
        var initialDistance = view.Camera.Distance;
        view.ApplyMacPinch(GestureStatus.Started, 1);
        view.ApplyMacPinch(GestureStatus.Running, 1.5);
        Assert(view.Camera.Distance == initialDistance, "display only view zoomed");
        view.IsInteractive = true;
        Assert(view.GestureRecognizers.OfType<PanGestureRecognizer>().Any(), "orbit gesture was not restored");
        Assert(view.GestureRecognizers.OfType<TapGestureRecognizer>().Any(), "picking gesture was not restored");
    }),
    ("surface render limit can be changed for animation and restored", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        view.MaximumRenderDimension = 768;
        var animation = view.CapturePaintTarget(2048, 1024);
        Assert(animation.Width == 768 && animation.Height == 384, "animation render limit was ignored");
        view.MaximumRenderDimension = DepthRenderer.MaximumDimension;
        var still = view.CapturePaintTarget(2048, 1024);
        Assert(still.Width == 2048 && still.Height == 1024, "still frame did not regain full resolution");
        Assert(!ReferenceEquals(animation, still), "resizing reused the old frame");
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
        var frame = view.CapturePaintTarget(1170, 1320);
        Assert(frame.Width < 1170 && frame.Height < 1320, "budget did not lower resolution");
        Assert(frame.Pixels.Span.ToArray().Any(p => p != 0xFFF4F6FA), "gallery disappeared");
        Assert(ReferenceEquals(frame, view.CapturePaintTarget(1170, 1320)),
            "unchanged paint retried a failing full-size render");
        sample.Animate(.5f);
        Assert(ReferenceEquals(frame, view.CapturePaintTarget(1170, 1320)),
            "scene animation retried a failing full-size render");
        RenderTarget recovered = frame;
        for (var i = 0; i < 8; i++)
        {
            view.Zoom(.8f);
            recovered = view.CapturePaintTarget(1170, 1320);
        }
        Assert(recovered.Width == 1170 && recovered.Height == 1320,
            "small zoom-out steps did not restore full resolution");
    }),
    ("fallback geometry counting enforces the scene node budget", () =>
    {
        var empty = Shape.Group();
        var repeated = empty;
        for (var i = 0; i < 17; i++) repeated = Shape.Group(repeated, repeated);
        try
        {
            SceneView.SceneGeometry(new Scene().Add(repeated));
        }
        catch (ArgumentException)
        {
            var box = Shape.Box();
            var valid = new Scene().Add(Shape.Group(box, box, empty));
            var count = SceneView.SceneGeometry(valid);
            Assert(count.Nodes == 4 && count.Triangles == 24,
                "valid repeated instances or empty groups were counted incorrectly");
            return;
        }
        throw new InvalidOperationException("Fallback traversal exceeded the Core node budget");
    }),
    ("fallback resolution recovers after scene simplification", () =>
    {
        var sample = DemoScenes.Packing();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
        for (var i = 0; i < 5; i++) view.Zoom(1.25f);
        Assert(view.CapturePaintTarget(1170, 1320).Width < 1170, "expected a budget fallback");
        sample.Scene.Remove(sample.Scene.Shapes.Single(shape => shape.Name == "Packages"));
        var recovered = view.CapturePaintTarget(1170, 1320);
        Assert(recovered.Width == 1170 && recovered.Height == 1320,
            "removing geometry did not restore full resolution");
    }),
    ("fallback resolution recovers after same-count geometry replacement", () =>
    {
        var sample = DemoScenes.Packing();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
        for (var i = 0; i < 5; i++) view.Zoom(1.25f);
        Assert(view.CapturePaintTarget(1170, 1320).Width < 1170, "expected a budget fallback");
        var packages = sample.Scene.Shapes.Single(shape => shape.Name == "Packages");
        sample.Scene.Replace(packages, Shape.Box(0xFF76DBC7).Named("One package"));
        var recovered = view.CapturePaintTarget(1170, 1320);
        Assert(recovered.Width == 1170 && recovered.Height == 1320,
            "same-count geometry replacement did not restore full resolution");
    }),
    ("fallback resolution recovers when same-triangle geometry shrinks", () =>
    {
        var sample = DemoScenes.Packing();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
        for (var i = 0; i < 5; i++) view.Zoom(1.25f);
        Assert(view.CapturePaintTarget(1170, 1320).Width < 1170, "expected a budget fallback");
        var packages = sample.Scene.Shapes.Single(shape => shape.Name == "Packages");
        var smaller = Shape.Group(packages.Children.Select(child =>
            child.Scaled(.1f, .1f, .1f)).ToArray()).Named("Packages");
        sample.Scene.Replace(packages, smaller);
        view.CapturePaintTarget(1170, 1320);
        view.Camera.Orbit(.001f, 0);
        var recovered = view.CapturePaintTarget(1170, 1320);
        Assert(recovered.Width == 1170 && recovered.Height == 1320,
            "shrinking same-triangle geometry did not restore full resolution");
    }),
    ("failed full-resolution probes do not repeat every animation frame", () =>
    {
        const int copies = 700_000;
        var indices = new int[copies * 3];
        for (var i = 0; i < copies; i++)
        {
            indices[i * 3] = 0;
            indices[i * 3 + 1] = 1;
            indices[i * 3 + 2] = 2;
        }
        var triangle = Shape.FromMesh(new Mesh([
            new(-.5f, -.5f, 0), new(3.6f, -.5f, 0), new(-.5f, 3.6f, 0)
        ], indices), new Material(0xFF80B2FF));
        var scene = new Scene().Add(triangle);
        var camera = new Camera(5, 0, 0) { Projection = CameraProjection.Orthographic, OrthographicHeight = 1024 };
        var view = new SceneView { Scene = scene, Camera = camera };
        var first = view.CapturePaintTarget(1024, 1024);
        Assert(first.Width < 1024, "expected a full-size raster budget failure");
        camera.Orbit(.00001f, 0);
        var probe = view.CapturePaintTarget(1024, 1024);
        Assert(!ReferenceEquals(first, probe), "test scene did not trigger a second full-size probe");
        camera.Orbit(.00001f, 0);
        Assert(ReferenceEquals(probe, view.CapturePaintTarget(1024, 1024)),
            "failed full-size probe repeated on the next animation frame");
    }),
    ("fallback resolution recovers after widening orthographic view", () =>
    {
        var sample = DemoScenes.Packing();
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
        for (var i = 0; i < 5; i++) view.Zoom(1.25f);
        Assert(view.CapturePaintTarget(1170, 1320).Width < 1170, "expected a budget fallback");
        view.Camera.OrthographicHeight *= 3;
        var recovered = view.CapturePaintTarget(1170, 1320);
        Assert(recovered.Width == 1170 && recovered.Height == 1320,
            "widening the camera view did not restore full resolution");
    }),
    ("all gallery scenes remain reachable on narrow screens", () =>
    {
        var page = new GalleryPage();
        var content = (VerticalStackLayout)((ScrollView)page.Content).Content;
        var scroller = content.Children.OfType<ScrollView>()
            .Single(view => view.Content is HorizontalStackLayout row &&
                row.Children.OfType<Button>().Any(button => button.Text == "City"));
        Assert(scroller.Orientation == ScrollOrientation.Horizontal, "scene catalogue cannot scroll horizontally");
        var buttons = ((HorizontalStackLayout)scroller.Content).Children.OfType<Button>().ToArray();
        Assert(buttons.Length == DemoScenes.All.Count, "not every example is in the gallery");
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

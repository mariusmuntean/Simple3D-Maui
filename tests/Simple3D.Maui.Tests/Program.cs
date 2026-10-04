using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using System.Runtime.InteropServices;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared;
using Simple3D.Demo;
using SkiaSharp;

if (args.Contains("--performance")) return PerformanceProbe.Run();

var tests = new (string Name, Action Run)[]
{
    ("diagonal antialiasing blends stair steps and preserves straight edges", () =>
    {
        var diagonal = Enumerable.Range(0, 64).Select(i => i % 8 > i / 8 ? 0xFFFFFFFFu : 0xFF000000u).ToArray();
        var result = diagonal.ToArray();
        SceneView.SmoothDiagonalEdges(diagonal, result, 8, 8);
        Assert(result.Any(pixel => pixel != 0xFFFFFFFFu && pixel != 0xFF000000u), "diagonal still has only hard pixel steps");
        Assert(result[1 * 8 + 6] == 0xFFFFFFFFu && result[6 * 8 + 1] == 0xFF000000u,
            "filter blurred flat interiors");
        var straight = Enumerable.Range(0, 64).Select(i => i % 8 >= 4 ? 0xFFFFFFFFu : 0xFF000000u).ToArray();
        result = straight.ToArray();
        SceneView.SmoothDiagonalEdges(straight, result, 8, 8);
        Assert(result.SequenceEqual(straight), "filter blurred a straight edge");
        Assert(diagonal.All(pixel => pixel == 0xFFFFFFFFu || pixel == 0xFF000000u), "filter mutated the source frame");
    }),
    ("native antialiasing can be toggled without rerendering or changing picks", () =>
    {
        var scene = new Scene().Add(Shape.Box());
        var view = new SceneView { Scene = scene };
        var target = view.CapturePaintTarget(96, 80);
        var original = target.Pixels.ToArray();
        var bitmap = view.PaintBitmap(target);
        var smooth = bitmap.GetPixelSpan().ToArray();
        Assert(!smooth.AsSpan().SequenceEqual(MemoryMarshal.AsBytes(target.Pixels.Span)), "native edges were not filtered");
        view.IsAntialiasEnabled = false;
        Assert(ReferenceEquals(target, view.CapturePaintTarget(96, 80)), "changing edge quality rerendered Core geometry");
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(target)), "changing edge quality replaced the bitmap");
        Assert(bitmap.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(target.Pixels.Span)), "disabling did not restore original pixels");
        view.IsAntialiasEnabled = true;
        Assert(view.PaintBitmap(target).GetPixelSpan().SequenceEqual(smooth), "reenabling left stale pixels");
        Assert(target.Pixels.Span.SequenceEqual(original), "native antialiasing mutated the target");
        Assert(ReferenceEquals(target.Pick(48, 40), scene.Shapes[0]), "native antialiasing changed picking");
        view.ReleaseRenderResources();
    }),
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
    ("selection highlights only visible pixels and restores original materials", () =>
    {
        var back = Shape.Box(0xFF4080C0).Scaled(2).Named("back");
        var front = Shape.Box(0xFFC08040).Scaled(.6f).At(0, 0, 1).Named("front");
        var scene = new Scene().Add(back).Add(front);
        var view = new SceneView { Scene = scene, Camera = new Camera(5, 0, 0) };
        var target = view.CapturePaintTarget(120, 120);
        var original = view.PaintBitmap(target).GetPixelSpan().ToArray();
        var pickX = -1;
        var pickY = -1;
        for (var y = 0; y < 120 && pickX < 0; y++)
            for (var x = 0; x < 120; x++)
                if (ReferenceEquals(target.Pick(x, y), back)) { pickX = x; pickY = y; break; }
        Assert(pickX >= 0, "fixture has no visible back pixels");
        var notifications = 0;
        view.SelectionChanged += (_, _) => notifications++;
        Assert(ReferenceEquals(view.SelectAt(pickX, pickY, 120, 120), back), "wrong selected object");
        var highlighted = view.PaintBitmap(target).GetPixelSpan();
        var changes = 0;
        for (var y = 0; y < 120; y++)
            for (var x = 0; x < 120; x++)
            {
                var offset = (y * 120 + x) * 4;
                var changed = !highlighted.Slice(offset, 4).SequenceEqual(original.AsSpan(offset, 4));
                Assert(changed == ReferenceEquals(target.Pick(x, y), back), "highlight leaked through occlusion or missed selected pixels");
                if (changed) changes++;
            }
        Assert(changes > 0, "no selected pixels changed");
        Assert(back.Color == 0xFF4080C0 && front.Color == 0xFFC08040, "highlight changed scene materials");
        Assert(target.Pixels.Span.SequenceEqual(view.CaptureFrame(120, 120).Pixels.Span), "highlight changed owned captures");
        view.SelectAt(60, 60, 120, 120);
        Assert(ReferenceEquals(view.SelectedShape, front), "selection did not switch to front object");
        view.SelectAt(0, 0, 120, 120);
        Assert(view.SelectedShape is null && notifications == 3, "background did not clear selection");
        Assert(view.PaintBitmap(target).GetPixelSpan().SequenceEqual(original), "deselection did not restore original pixels");
        view.SelectAt(60, 60, 120, 120);
        view.Scene = new Scene().Add(Shape.Sphere());
        Assert(view.SelectedShape is null, "scene replacement kept selection");
        view.ReleaseRenderResources();
    }),
    ("selection follows animated replacements and their visible outline", () =>
    {
        foreach (var sample in DemoScenes.RegressionScenes)
        {
            var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
            sample.Animate(0);
            var roots = sample.Scene.Shapes.ToArray();
            sample.Animate(.1f);
            var animatedIndex = Enumerable.Range(0, roots.Length)
                .Single(i => !ReferenceEquals(roots[i], sample.Scene.Shapes[i]));
            sample.Animate(0);
            var before = view.CapturePaintTarget(200, 200);
            var animated = sample.Scene.Shapes[animatedIndex];
            var parts = new Dictionary<Shape, int[]>();
            Collect(animated, []);
            var point = Enumerable.Range(0, 40000).FirstOrDefault(i =>
                before.Pick(i % 200, i / 200) is Shape candidate && parts.ContainsKey(candidate), -1);
            Assert(point >= 0, $"{sample.Name}: fixture has no visible animated part");
            var part = before.Pick(point % 200, point / 200)!;
            var path = parts[part];
            view.SelectAt(point % 200, point / 200, 200, 200);
            for (var tick = 1; tick <= 3; tick++)
            {
                sample.Animate(tick * .2f);
                var target = view.CapturePaintTarget(200, 200);
                var selected = view.SelectedShape;
                var expected = sample.Scene.Shapes[animatedIndex];
                foreach (var child in path) expected = expected.Children[child];
                Assert(ReferenceEquals(selected, expected), $"{sample.Name}: selection did not follow the animated part");
                var pixels = MemoryMarshal.Cast<byte, uint>(view.PaintBitmap(target).GetPixelSpan());
                var outlines = 0;
                for (var i = 0; i < pixels.Length; i++)
                    if (pixels[i] == 0xFFFFD27A)
                    {
                        Assert(ReferenceEquals(target.Pick(i % 200, i / 200), selected), "outline stayed at the old position");
                        outlines++;
                    }
                Assert(outlines > 0, $"{sample.Name}: moving selection lost its outline");
            }
            sample.Scene.Clear();
            Assert(view.SelectedShape is null, "removed object stayed selected");
            view.ReleaseRenderResources();

            void Collect(Shape shape, int[] path)
            {
                if (shape.Mesh.Count > 0) parts[shape] = path;
                for (var i = 0; i < shape.Children.Count; i++) Collect(shape.Children[i], [.. path, i]);
            }
        }
    }),
    ("selection bitmap matches picking for duplicate nodes and viewport edges", () =>
    {
        var shape = Shape.Box().Scaled(3);
        var view = new SceneView { Scene = new Scene().Add(shape).Add(shape), Camera = new Camera(3, 0, 0) };
        var target = view.CapturePaintTarget(80, 64);
        var original = MemoryMarshal.Cast<byte, uint>(view.PaintBitmap(target).GetPixelSpan()).ToArray();
        var point = Enumerable.Range(0, original.Length).First(i => ReferenceEquals(target.Pick(i % 80, i / 80), shape));
        view.SelectAt(point % 80, point / 80, 80, 64);
        var actual = MemoryMarshal.Cast<byte, uint>(view.PaintBitmap(target).GetPixelSpan());
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 80; x++)
            {
                var index = y * 80 + x;
                var expected = original[index];
                if (ReferenceEquals(target.Pick(x, y), shape))
                {
                    if (!ReferenceEquals(target.Pick(x - 2, y), shape) || !ReferenceEquals(target.Pick(x + 2, y), shape) ||
                        !ReferenceEquals(target.Pick(x, y - 2), shape) || !ReferenceEquals(target.Pick(x, y + 2), shape))
                        expected = 0xFFFFD27Au;
                    else
                    {
                        var red = (((expected >> 16) & 255) * 2 + 142) / 3;
                        var green = (((expected >> 8) & 255) * 2 + 225) / 3;
                        var blue = ((expected & 255) * 2 + 205) / 3;
                        expected = 0xFF000000u | (red << 16) | (green << 8) | blue;
                    }
                }
                Assert(actual[index] == expected, $"highlight differs from picking at {x},{y}");
            }
        view.ReleaseRenderResources();
    }),
    ("animated selection survives concurrent pan and detail recovery", () =>
    {
        var sample = DemoScenes.All.First(s => s.Name == "Equipment");
        sample.Animate(0);
        var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera, MaximumRenderDimension = 768 };
        var target = view.CapturePaintTarget(614, 768);
        var arrow = sample.Scene.Shapes[1];
        var point = Enumerable.Range(0, target.Width * target.Height)
            .First(i => ReferenceEquals(target.Pick(i % target.Width, i / target.Width), arrow));
        view.SelectAt(point % target.Width, point / target.Width, target.Width, target.Height);
        view.ApplyPan(GestureStatus.Started, 0, 0);
        for (var tick = 1; tick <= 4; tick++)
        {
            sample.Animate(tick * .1f);
            view.ApplyPan(GestureStatus.Running, tick * 2, tick);
            target = view.CapturePaintTarget(614, 768);
            Assert(target.Height == 512, "animation bypassed the active pan preview");
            Assert(ReferenceEquals(view.SelectedShape, sample.Scene.Shapes[1]), "pan lost the animated selection");
            Assert(MemoryMarshal.Cast<byte, uint>(view.PaintBitmap(target).GetPixelSpan()).Contains(0xFFFFD27Au),
                "concurrent animation and pan lost the contour");
        }
        view.ApplyPan(GestureStatus.Completed, 8, 4);
        target = view.CapturePaintTarget(614, 768);
        Assert(target.Height == 768, "pan completion did not restore detail");
        Assert(MemoryMarshal.Cast<byte, uint>(view.PaintBitmap(target).GetPixelSpan()).Contains(0xFFFFD27Au),
            "detail recovery lost the contour");
        view.ReleaseRenderResources();
    }),
    ("selection follows child positions despite duplicate names and clears missing children", () =>
    {
        var left = Shape.Box().At(-1, 0, 0).Named("duplicate");
        var right = Shape.Box().At(1, 0, 0).Named("duplicate");
        var old = Shape.Group(left, right);
        var scene = new Scene().Add(old);
        var view = new SceneView { Scene = scene, Camera = new Camera(6, 0, 0) };
        var target = view.CapturePaintTarget(120, 120);
        var point = Enumerable.Range(0, 14400).First(i => ReferenceEquals(target.Pick(i % 120, i / 120), right));
        view.SelectAt(point % 120, point / 120, 120, 120);
        var moved = right.At(1, .2f, 0);
        var next = Shape.Group(left, moved);
        scene.Replace(old, next);
        Assert(ReferenceEquals(view.SelectedShape, moved), "selection matched duplicate names instead of child position");
        scene.Replace(next, Shape.Group(left));
        Assert(view.SelectedShape is null, "missing replacement child stayed selected");
        view.ReleaseRenderResources();
    }),
    ("gallery scene has no scrolling ancestor", () =>
    {
        var page = new GalleryPage();
        var view = FindScene(page.Content);
        Assert(view is not null, "gallery has no scene");
        for (Element? parent = view!.Parent; parent is not null; parent = parent.Parent)
            Assert(parent is not ScrollView, "scene drag can scroll its parent");

        static SceneView? FindScene(IView root) => root switch
        {
            SceneView scene => scene,
            ContentView content => content.Content is null ? null : FindScene(content.Content),
            Border border => border.Content is null ? null : FindScene(border.Content),
            ScrollView scroll => FindScene(scroll.Content),
            Layout layout => layout.Children.Select(FindScene).FirstOrDefault(scene => scene is not null),
            _ => null
        };
    }),
    ("native paint bitmap is reused and tracks rendered pixels", () =>
    {
        var scene = new Scene().Add(Shape.Box());
        var view = new SceneView { Scene = scene, IsAntialiasEnabled = false };
        var first = view.CapturePaintTarget(96, 80);
        var bitmap = view.PaintBitmap(first);
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(first)), "unchanged paint allocated a new bitmap");
        Assert(bitmap.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(first.Pixels.Span)),
            "first bitmap pixels differ from frame");
        using var painted = new SKBitmap(new SKImageInfo(96, 80, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(painted);
        SceneView.DrawSceneBitmap(canvas, bitmap, 96, 80);
        var before = painted.GetPixelSpan().ToArray();
        scene.Add(Shape.Sphere().At(1, 0, 0));
        var second = view.CapturePaintTarget(96, 80);
        Assert(ReferenceEquals(first, second), "scene mutation replaced the render target");
        Assert(ReferenceEquals(bitmap, view.PaintBitmap(second)), "same-size paint allocated a new bitmap");
        Assert(bitmap.GetPixelSpan().SequenceEqual(MemoryMarshal.AsBytes(second.Pixels.Span)),
            "updated bitmap pixels differ from frame");
        SceneView.DrawSceneBitmap(canvas, bitmap, 96, 80);
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
    ("zoom uses a bounded preview for its entire gesture", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()), Camera = new Camera(10) };
        view.ApplyMacPinch(GestureStatus.Started, 1);
        foreach (var scale in new[] { 1.1, 1.2, 1.4 })
        {
            view.ApplyMacPinch(GestureStatus.Running, scale);
            Assert(view.CapturePaintTarget(2048, 1260).Width <= 512, "zoom rendered full desktop resolution during the gesture");
        }
        view.ApplyMacPinch(GestureStatus.Completed, 1.4);
        Assert(view.CapturePaintTarget(2048, 1260).Width == 2048, "zoom release did not restore detail");
        var pinch = (IPinchGestureController)view.GestureRecognizers.OfType<PinchGestureRecognizer>().Single();
        pinch.SendPinchStarted(view, new Microsoft.Maui.Graphics.Point(.5, .5));
        pinch.SendPinch(view, 1.1, new Microsoft.Maui.Graphics.Point(.5, .5));
        Assert(view.CapturePaintTarget(2048, 1260).Width <= 512, "touch pinch did not use the preview");
        pinch.SendPinchCanceled(view);
        Assert(view.CapturePaintTarget(2048, 1260).Width == 2048, "cancelled touch pinch left a preview");
    }),
    ("scaled scene edges use interpolated pixels", () =>
    {
        using var source = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Opaque));
        source.SetPixel(0, 0, SKColors.Black);
        source.SetPixel(0, 1, SKColors.Black);
        source.SetPixel(1, 0, SKColors.White);
        source.SetPixel(1, 1, SKColors.White);
        using var destination = new SKBitmap(new SKImageInfo(20, 20, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(destination);
        SceneView.DrawSceneBitmap(canvas, source, 20, 20);
        Assert(destination.GetPixel(1, 10).Red == 0 && destination.GetPixel(18, 10).Red == 255,
            "sampling changed solid interior colors");
        var edge = destination.GetPixel(9, 10).Red;
        Assert(edge > 0 && edge < 255, "magnified boundary still uses nearest-neighbor pixel steps");
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
    ("short mouse drags retain translation at beginning and release", () =>
    {
        var view = new SceneView { Camera = new Camera(5, 0, 0) };
        view.ApplyMacPan(GestureStatus.Started, 10, 5);
        view.ApplyMacPan(GestureStatus.Completed, 25, 10);
        Assert(Math.Abs(view.Camera.Yaw - .3f) < .0001f && Math.Abs(view.Camera.Pitch + .12f) < .0001f,
            "short drag lost its initial or final translation");
    }),
    ("native mouse pan applies every update and ends preview on cancellation", () =>
    {
        var view = new SceneView { Camera = new Camera(5, 0, 0), Scene = new Scene().Add(Shape.Box()) };
        view.ApplyPan(GestureStatus.Started, 0, 0);
        view.ApplyPan(GestureStatus.Running, 10, 5);
        view.ApplyPan(GestureStatus.Running, 25, 10);
        Assert(Math.Abs(view.Camera.Yaw - .3f) < .0001f && Math.Abs(view.Camera.Pitch + .12f) < .0001f,
            "mouse movement stopped or cumulative translation was applied twice");
        Assert(view.CapturePaintTarget(2048, 1260).Width == 512, "native pan did not enter preview");
        view.ApplyPan(GestureStatus.Canceled, 0, 0);
        Assert(view.CapturePaintTarget(2048, 1260).Width == 2048, "canceled pan left the view in preview");
        view.ApplyPan(GestureStatus.Running, 40, 20);
        Assert(Math.Abs(view.Camera.Yaw - .3f) < .0001f, "a late event changed the camera after cancellation");
        view.IsInteractive = false;
        view.ApplyPan(GestureStatus.Started, 0, 0);
        view.ApplyPan(GestureStatus.Running, 50, 20);
        Assert(Math.Abs(view.Camera.Yaw - .3f) < .0001f, "display-only view accepted native pan");
    }),
    ("dragging bounds raster work and restores detail on release", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()), Camera = new Camera(5, 0, 0) };
        var fullWork = view.CapturePaintTarget(2048, 1260).RasterSamples;
        var pan = (IPanGestureController)view.GestureRecognizers.OfType<PanGestureRecognizer>().Single();
        pan.SendPanStarted(view, 1);
        pan.SendPan(view, 1, 0, 1);
        var preview = view.CapturePaintTarget(2048, 1260);
        Assert(preview.RasterSamples < fullWork / 8, "drag still performs too much full-size raster work");
        pan.SendPanCompleted(view, 1);
        var final = view.CapturePaintTarget(2048, 1260);
        Assert(final.Width == 2048 && final.Height == 1260, "release failed to restore full detail");
        view.ReleaseRenderResources();
    }),
    ("gallery drag benchmark", () =>
    {
        foreach (var sample in DemoScenes.RegressionScenes)
        {
            var view = new SceneView { Scene = sample.Scene, Camera = sample.Camera };
            var pan = (IPanGestureController)view.GestureRecognizers.OfType<PanGestureRecognizer>().Single();
            pan.SendPanStarted(view, 1);
            for (var i = 0; i < 5; i++) { pan.SendPan(view, i, 0, 1); view.PaintBitmap(view.CapturePaintTarget(2048, 1260)); }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            long work = 0;
            for (var i = 0; i < 30; i++)
            {
                pan.SendPan(view, i * 2, i, 1);
                var target = view.CapturePaintTarget(2048, 1260);
                work += target.RasterSamples;
                view.PaintBitmap(target);
            }
            Console.WriteLine($"Drag {sample.Name}: {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / 30:F2} ms/frame; {work / 30:N0} raster samples/frame");
            view.ReleaseRenderResources();
        }
    }),
    ("orbit preview restores full resolution after mouse drag", () =>
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        var pan = (IPanGestureController)view.GestureRecognizers.OfType<PanGestureRecognizer>().Single();
        pan.SendPanStarted(view, 1);
        pan.SendPan(view, 24, 0, 1);
        var preview = view.CapturePaintTarget(2048, 630);
        Assert(preview.Width == 512 && preview.Height == 158, "drag preview still renders at full desktop resolution");
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
    ("interaction toggles preserve host gestures without duplicating built in gestures", () =>
    {
        var view = new SceneView();
        var builtIn = view.GestureRecognizers.ToArray();
        var hostTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        var hostSwipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        view.GestureRecognizers.Add(hostTap);
        view.GestureRecognizers.Add(hostSwipe);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            view.IsInteractive = false;
            Assert(view.GestureRecognizers.SequenceEqual(new IGestureRecognizer[] { hostTap, hostSwipe }),
                "display only mode removed host gestures or retained built in gestures");
            view.IsInteractive = true;
            Assert(view.GestureRecognizers.Count == builtIn.Length + 2, "interaction toggle duplicated gestures");
            Assert(view.GestureRecognizers.Contains(hostTap) && view.GestureRecognizers.Contains(hostSwipe), "enabling removed host gestures");
            Assert(builtIn.All(gesture => view.GestureRecognizers.Count(item => ReferenceEquals(item, gesture)) == 1),
                "built in gestures were recreated or duplicated");
        }
        view.ReleaseRenderResources();
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
    ("backgrounding the gallery pauses animation and ignores queued ticks", () =>
    {
        var previousProvider = DispatcherProvider.Current;
        var previousApp = Application.Current;
        var dispatcher = new TestDispatcher();
        DispatcherProvider.SetCurrent(dispatcher);
        SceneView? view = null;
        try
        {
            var app = new App();
            var window = ((Microsoft.Maui.IApplication)app).CreateWindow(null);
            var page = (GalleryPage)((Window)window).Page!;
            var grid = (Grid)page.Content;
            view = grid.Children.OfType<Border>().Single().Content as SceneView
                ?? throw new InvalidOperationException("gallery scene view missing");
            var footer = (VerticalStackLayout)grid.Children.OfType<ScrollView>().Single().Content;
            var tools = (HorizontalStackLayout)footer.Children.OfType<ScrollView>().Single().Content;
            var animate = tools.Children.OfType<Button>().Single(button => button.Text == "Animate");
            window.Created();
            window.Activated();
            ((IButtonController)animate).SendClicked();
            Assert(animate.Text == "Pause" && view.MaximumRenderDimension == 768, "animation did not start");
            var before = view.Scene.Shapes.ToArray();
            dispatcher.Timer.DeliverTick();
            Assert(!view.Scene.Shapes.SequenceEqual(before), "animation did not update its scene");
            window.Deactivated();
            window.Stopped();
            Assert(animate.Text == "Animate" && view.MaximumRenderDimension == DepthRenderer.MaximumDimension,
                "backgrounded gallery kept animating");
            var stopped = view.Scene.Shapes.ToArray();
            dispatcher.Timer.DeliverTick();
            Assert(view.Scene.Shapes.SequenceEqual(stopped), "queued tick animated the backgrounded scene");
            window.Resumed();
            window.Activated();
            Assert(animate.Text == "Animate", "resuming restarted animation without user input");
            ((IButtonController)animate).SendClicked();
            dispatcher.Timer.DeliverTick();
            Assert(animate.Text == "Pause" && !view.Scene.Shapes.SequenceEqual(stopped), "animation could not resume explicitly");
            window.Deactivated();
            window.Stopped();
            window.Destroying();
        }
        finally
        {
            view?.ReleaseRenderResources();
            Application.Current = previousApp;
            DispatcherProvider.SetCurrent(previousProvider);
        }
    }),
    ("gallery reset clears highlighting and notifies once even in display only mode", () =>
    {
        var page = new GalleryPage();
        var grid = (Grid)page.Content;
        var view = grid.Children.OfType<Border>().Single().Content as SceneView
            ?? throw new InvalidOperationException("gallery border does not contain its scene view");
        var footer = (VerticalStackLayout)grid.Children.OfType<ScrollView>().Single().Content;
        var tools = (HorizontalStackLayout)footer.Children.OfType<ScrollView>().Single().Content;
        var reset = tools.Children.OfType<Button>().Single(button => button.Text == "Reset");
        var target = view.CapturePaintTarget(200, 200);
        var original = view.PaintBitmap(target).GetPixelSpan().ToArray();
        var point = Enumerable.Range(0, 40000).First(i => target.Pick(i % 200, i / 200) is not null);
        view.SelectAt(point % 200, point / 200, 200, 200);
        Assert(view.SelectedShape is not null, "fixture did not select a shape");
        var cleared = 0;
        view.SelectionChanged += (_, shape) => { if (shape is null) cleared++; };
        view.IsInteractive = false;
        ((IButtonController)reset).SendClicked();
        Assert(view.SelectedShape is null, "Reset cleared the label but retained the selected shape");
        Assert(view.PaintBitmap(view.CapturePaintTarget(200, 200)).GetPixelSpan().SequenceEqual(original),
            "Reset retained highlighted pixels");
        ((IButtonController)reset).SendClicked();
        Assert(cleared == 1, "Reset omitted or repeated the selection-cleared notification");
        view.ReleaseRenderResources();
    }),
    ("all gallery scenes remain reachable on narrow screens", () =>
    {
        var page = new GalleryPage();
        var content = page.Content is Grid grid ? grid.Children.OfType<VerticalStackLayout>().Single() :
            (VerticalStackLayout)((ScrollView)page.Content).Content;
        var scroller = content.Children.OfType<ScrollView>()
            .Single(view => view.Content is HorizontalStackLayout row &&
                row.Children.OfType<Button>().Any(button => button.Text == "Gantry Crane"));
        Assert(scroller.Orientation == ScrollOrientation.Horizontal, "scene catalogue cannot scroll horizontally");
        var buttons = ((HorizontalStackLayout)scroller.Content).Children.OfType<Button>().ToArray();
        Assert(buttons.Length == DemoScenes.All.Count, "not every example is in the gallery");
    }),
    ("world labels stay readable inside a narrow viewport", () =>
    {
        using var font = new SKFont(SKTypeface.Default, 42);
        var labels = new[]
        {
            new ProjectedLabel(new WorldLabel("INDEXED MESH · 512 TRIANGLES", System.Numerics.Vector3.Zero), new(370, 8), 1),
            new ProjectedLabel(new WorldLabel("BOTTOM", System.Numerics.Vector3.Zero), new(370, 398), 1)
        };
        var placed = new SceneView().LayoutLabels(labels, font, 390, 400, 390, 400);
        Assert(placed.Count == 2, "labels were dropped despite available space");
        Assert(placed[0].Text.EndsWith("…"), "long label was clipped instead of abbreviated");
        foreach (var label in placed)
            Assert(label.Bounds.Left >= 0 && label.Bounds.Top >= 0 && label.Bounds.Right <= 390 && label.Bounds.Bottom <= 400,
                "label glyphs extend outside the viewport");
        Assert(new SceneView().LayoutLabels(labels, font, 390, 20, 390, 400).Count == 0,
            "labels were painted where their height cannot fit");
    }),
    ("overlapping projected labels use separate nearby rows", () =>
    {
        using var font = new SKFont(SKTypeface.Default, 28);
        var labels = new[] { "MOTOR", "OUTPUT", "SENSOR" }.Select(text =>
            new ProjectedLabel(new WorldLabel(text, System.Numerics.Vector3.Zero), new(100, 100), 1)).ToArray();
        var placed = new SceneView().LayoutLabels(labels, font, 400, 300, 400, 300);
        Assert(placed.Count == 3, "labels were dropped despite available nearby rows");
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                Assert(!placed[i].Bounds.IntersectsWith(placed[j].Bounds), "label glyphs overlap");
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

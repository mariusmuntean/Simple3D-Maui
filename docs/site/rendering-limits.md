# Rendering model and limits

`DepthRenderer` transforms opaque triangles, clips them at the camera near plane, shades them with a fixed light, rasterizes with per-pixel depth, and stores the visible leaf shape for picking. This resolves intersecting opaque geometry that a sorted-triangle painter cannot. `SceneRenderer` remains for older primitive/default-camera code and throws a diagnostic for newer features it cannot represent.

| Limit | Value | Why |
| --- | ---: | --- |
| Width or height | 2,048 physical pixels | Bounds frame storage |
| Scene node visits | 100,000 | Bounds grouping traversal |
| Aggregate triangles | 1,000,000 | Bounds and render traversal |
| Raster bounding-box samples | 16,000,000 per frame | Bounds overlapping screen-filling work |
| Group hierarchy | 64 levels | Bounds recursion |

The sample-work budget counts clipped triangle bounding boxes, including pixels outside the triangle but inside its box. Dense overlapping scenes can reach it before the triangle limit. Direct `DepthRenderer.Render` calls then throw `RasterBudgetExceededException`; reduce scene complexity, viewport size, or visible overlap. `SceneView` retries the native paint at half resolution when this happens, keeping interactive zoom usable at the cost of image sharpness. `Camera.FitToScene` calculates bounds before mutation and leaves the camera unchanged if a budget or overflow check fails.

Only opaque surfaces are supported. Labels overlay the scene and are not depth tested. There is no far clipping plane, transparency, shadowing, texture sampling, or GPU scene engine. The CPU renderer is deterministic for the tested inputs and uses a bounded scratch depth buffer. Device frame rate depends on scene, viewport, and native presentation, so profile the target device for animation.

`DepthRenderer.Render` returns an owned `RenderFrame`; its pixels and picks remain valid after later renders. For repeated frames when snapshots are unnecessary, create a fixed-size `RenderTarget` and call `RenderInto`. The target reuses its pixel and picking arrays, and the next render overwrites their content. Create a new target when the viewport size changes. If rendering throws, render successfully again before reading the target.

```csharp
var renderer = new DepthRenderer();
var target = new RenderTarget(768, 576);
renderer.RenderInto(scene, camera, target);
ReadOnlyMemory<uint> pixels = target.Pixels; // Changes on the next RenderInto call.
Shape? visible = target.Pick(384, 288);
```

In the checked-in animated Surface benchmark at 768×576 on one Mac, the owned path allocated about 3.54 MB per frame and the reusable target about 1.55 KB per frame. These are managed allocations in the Core renderer, not native presentation costs or a guaranteed device frame rate.

`SceneView.MaximumRenderDimension` controls the largest physical render dimension used during native painting. Its default is 2,048. Lower it while animating or while a scene is being manipulated, then restore it for still images. The view keeps the aspect ratio and upscales the rendered image to its layout size. Because both pixel and picking buffers scale with pixel count, changing the maximum dimension from 2,048 to 768 can substantially reduce allocation and raster work. This control does not change explicit `CaptureFrame(width, height)` requests. A display-only `SceneView` can set `IsInteractive = false` to release its built-in gestures.

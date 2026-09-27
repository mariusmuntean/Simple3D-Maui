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

Only opaque surfaces are supported. Labels overlay the scene and are not depth tested. There is no far clipping plane, transparency, shadowing, texture sampling, or GPU scene engine. The CPU renderer is deterministic for the tested inputs and uses a bounded scratch depth buffer; each returned frame owns its pixel and picking arrays. Device frame rate depends on scene, viewport, and native presentation, so profile the target device for animation.

`SceneView.MaximumRenderDimension` controls the largest physical render dimension used during native painting. Its default is 2,048. Lower it while animating or while a scene is being manipulated, then restore it for still images. The view keeps the aspect ratio and upscales the rendered image to its layout size. Because both pixel and picking buffers scale with pixel count, changing the maximum dimension from 2,048 to 768 can substantially reduce allocation and raster work. This control does not change explicit `CaptureFrame(width, height)` requests. A display-only `SceneView` can set `IsInteractive = false` to release its built-in gestures.

The view reuses its native Skia bitmap when successive paints have the same render dimensions. It copies pixels only when the owned `RenderFrame` changes and allocates a new bitmap when dimensions change. On native handler disconnect, it releases the bitmap, cached frame, and depth scratch buffer. Each `RenderFrame` still owns its pixel and picking arrays, so callers may retain snapshots safely.

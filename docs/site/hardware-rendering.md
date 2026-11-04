# Software and hardware rendering

`SceneView` supports native Metal on iOS and Mac Catalyst and OpenGL ES 3 on Android. The hardware path uploads immutable geometry and textures once, updates transforms and lighting per draw, depth-tests triangles, and composites a selection mask only while a selected shape is visible. Other frames render in a single pass straight into the drawable or window surface. It presents native textures directly; it does not render a CPU bitmap and merely upload that bitmap to a GPU.

## Choose a mode

The existing `.UseSkiaSharp()` registration remains required for software presentation and label overlays. No additional engine, browser, JavaScript bridge or registration is needed.

```csharp
var view = new SceneView
{
    Scene = scene,
    RenderingMode = RenderingMode.Auto
};
view.RenderingBackendChanged += (_, _) =>
    System.Diagnostics.Debug.WriteLine($"{view.ActiveRenderingBackend}: {view.RenderingDiagnostic}");
view.RenderingFailed += (_, error) =>
    System.Diagnostics.Debug.WriteLine($"{error.Message}; fallback={error.FellBackToSoftware}");
```

| Mode | Behavior |
| --- | --- |
| `Auto` (default) | Attempts the native GPU. Initialization or render failure selects software and records a diagnostic. |
| `Software` | Uses `DepthRenderer` and the existing adaptive CPU render resolution. |
| `Hardware` | Requires native GPU rendering. Failure reports `None` and raises `RenderingFailed`; no silent software fallback. |

Change the property on the UI thread to switch modes without rebuilding the scene or losing the camera/selection. After a failed attempt, switch to Software and back to Auto/Hardware to retry. `ActiveRenderingBackend` is `None` while waiting for a native surface or after disconnect. Merely selecting Hardware does not establish that a GPU frame succeeded. Subscribe before attaching the view to observe initialization failures.

Auto's fallback stays selected until a mode change or handler reconnection; it does not repeatedly retry a failing device every frame. Only device, driver, shader and allocation failures count: scene content that cannot be prepared (exceeding the triangle or node budget) skips GPU frames and is reported in `RenderingDiagnostic`, and hardware rendering resumes as soon as the scene is valid again. After a failure the native surface is disconnected and its GPU resources released. Old surfaces are likewise disconnected and released when modes change. Android surface destruction drops context-owned resources and reconstructs them on surface recreation. Metal command execution errors are reported asynchronously on the UI thread.

## What is shared and what differs

Scenes, hierarchy, materials, textures, lighting, cameras and gesture APIs are shared. Both backends support opaque geometry, Gouraud smooth lighting, vertex colors, texture addressing/filtering and selected-part highlighting. GPU selection uses a depth-tested mask and a contour one to three render pixels wide, scaled with screen density so it stays visible on high-density displays. When `MaximumRenderDimension` caps the render size, the frame is upscaled with linear filtering. Native graphics edge rules and rounding can differ from the software rasterizer; bitwise image equivalence is not promised.

GPU geometry is expanded at upload time to preserve flat face normals, cached by immutable geometry identity, and reused across transformed shape copies. The expanded vertex data (64 bytes per triangle corner) exists only during upload; resident meshes keep no CPU copy. Resources not used by the current scene are removed from the native cache. Resident scene vertex/texture resources are capped at 256 MiB, excluding frame attachments and driver overhead. Depth is cleared and discarded each frame without being stored (memoryless on Apple GPUs; invalidated on OpenGL ES). The offscreen color and selection attachments are created the first time a selection is shown, and recreated only on resize. Node and aggregate triangle limits still apply. The CPU raster-sample budget does not constrain the GPU path.

`MaximumRenderDimension` limits the internal GPU render target; presentation scales to the native surface. GPU rendering does not use the CPU path's 512-pixel drag limit. `IsAntialiasEnabled` controls software presentation only; the initial GPU backend does not enable MSAA.

Normals use the transform's cofactor matrix, so very small composed scales that become singular in floating-point arithmetic still render, lighting the surviving faces like the software renderer. A shape whose transformed bounds overflow to non-finite values is skipped as a whole on the GPU, whereas the software renderer skips only its non-finite triangles. Perspective GPU projection uses an infinite far plane. Metal uses reversed depth (near = 1) with a 32-bit float buffer for precision at distance; OpenGL ES, which lacks clip control, uses a 24-bit buffer with standard depth. Orthographic geometry uses a far plane computed from scene bounds; overlay labels retain near-plane-only visibility.

## Picking, labels and snapshots

Hardware picking performs an on-demand nearest-triangle ray query with near clipping and material backface culling. It does not read an ID buffer back every frame or run the full software rasterizer. Exact boundary pixels can differ from software pixel picking. Equal-depth hits prefer the earlier scene item. Large meshes can make a tap more expensive even when GPU drawing is fast.

Labels are projected into a transparent Skia overlay and use the existing layout/overlap handling. They remain independent of geometry depth. In hardware mode the overlay is composited and repainted only while the scene has labels, once per GPU frame: on Android from the labels of the frame just presented, on Apple platforms in the same Core Animation transaction as the Metal frame, so labels do not trail the geometry during orbit.

## Lifecycle

Frames requested while an iOS app is in the background are skipped and redrawn on activation; a submission rejected for background execution does not disable hardware rendering. On Android, a lost or invalidated EGL context is recreated on the same surface up to three times in a short window before the failure is reported like any other device failure. Switching `RenderingMode` releases the native view and all its GPU resources.

`CaptureFrame(width, height)` deliberately remains an **owned software snapshot**, regardless of the view's mode. Existing capture/picking ownership remains intact; capture is not proof that the GPU works and does not include the GPU selection composite. Normal GPU painting has no pixel readback. Native validation probes explicitly wait and read pixels for comparison.

## Compatibility

`SceneView` now derives from `ContentView` and contains its rendering surfaces. Normal Scene/Camera/selection APIs remain available, as does `InvalidateSurface()`. Code that cast the control to `SKCanvasView` or used inherited Skia paint events must be updated. There are no Windows GPU targets in this change.

## Validate and measure

See [Development and native validation](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/docs/DEVELOPMENT.md#gpu-validation). The gallery includes an Auto/Software/Hardware picker and displays the active backend or failure reason.

`LastHardwareSubmissionMilliseconds` measures CPU elapsed time preparing and submitting a native frame, including any driver blocking. It is not GPU execution time or displayed FPS. For comparisons, use Release builds, the same scene/resolution and actual target devices. Headless Mesa and emulators can execute graphics APIs through software drivers; their performance numbers must not be presented as physical GPU speedups.

## Why GLES3 on Android initially?

Vulkan is Android's primary low-level graphics API; OpenGL ES remains supported but is no longer under active feature development. This initial backend chooses GLES3 to keep native integration and maintenance small for the library's compact scenes. Vulkan offers more explicit control and lower driver overhead for demanding draw-call workloads, at the cost of explicit memory management, synchronization, swapchain handling and more device validation. The public rendering modes do not expose a graphics API and can accommodate another backend later.

A future Vulkan backend should query driver capabilities at runtime and evaluate Android 10+/Vulkan 1.1 as its baseline. Earlier Android Vulkan implementations can have driver limitations. No Vulkan performance advantage is claimed without matching device measurements. See Google's [Vulkan overview](https://developer.android.com/games/develop/vulkan/overview) and [native-engine guidance](https://developer.android.com/games/develop/vulkan/native-engine-support).

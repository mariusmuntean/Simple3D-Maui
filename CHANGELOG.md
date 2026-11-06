# Changelog

## 0.3.0-preview.1 · unreleased

Native GPU rendering and animated characters. `SceneView` now draws with Metal on iOS and Mac Catalyst and OpenGL ES 3 on Android, falls back to software when needed, and renders skinned glTF/VRM characters with morph targets.

### Breaking changes

- `SceneView` derives from `ContentView` instead of `SKCanvasView`. `CaptureFrame` still returns an owned software snapshot.
- The packages need iOS 15, Mac Catalyst 17 (macOS 14) or Android 5.0 (API 21) and later.

### Rendering

- Rendering modes `Auto` (default), `Software` and `Hardware`, switchable at runtime. Auto uses Metal or OpenGL ES 3 and falls back to software with a reason in `RenderingDiagnostic`; Hardware reports failure (`RenderingFailed`) instead of silently using software. `ActiveRenderingBackend` tells which one draws.
- GPU frames cache geometry and textures, depth-test opaque surfaces and render in one pass when nothing is selected; a selection draws an 8-bit mask and a density-scaled outline. Depth is never stored (memoryless on Apple GPUs, invalidated on OpenGL ES); Metal uses reversed float depth.
- Scene budget errors are reported in `RenderingDiagnostic` without disabling hardware rendering; near-singular transforms render on the GPU.
- Labels are composited in hardware mode only while the scene has any, once per GPU frame and in step with it.
- `SceneView.FrameSubmitted`, `LastFrameMilliseconds`, `LastFrameWidth` and `LastFrameHeight` report frame statistics.
- `FrameClock` ticks at the display's refresh rate (up to 240 Hz) for smooth animation.

### Characters

- Skinned meshes: `Skeleton`, `SkeletonPose`, `SkinnedMesh`, `Shape.FromSkinnedMesh`, `Shape.WithPose` and `SkeletonPose.Blend`. Metal and OpenGL ES skin in the vertex shader (one joint palette upload per pose); software rendering, picking and bounds skin on the CPU.
- Morph targets on the CPU, Metal and OpenGL ES: `MorphTarget`, `Shape.WithMorphWeights`.
- `GltfReader` (Core) and `GltfLoader` (MAUI) load binary glTF 2.0 and VRM: meshes, base color materials, the node hierarchy, skins, the VRM humanoid bone map and VRM expressions (`GltfModel.Expressions`, `GltfModel.ApplyExpressions`, which shows exactly the expressions passed and returns omitted ones to neutral).

### Fixes

- Switching the rendering mode on an attached view no longer crashes (the native surface was released before it left the layout).
- iOS and Mac Catalyst no longer freeze when the scene or camera changes while a Metal frame draws, for example from a `FrameSubmitted` handler.
- Background GPU submissions on iOS and EGL context loss on Android no longer disable hardware rendering for good. Failed native surfaces release their resources.
- Resident GPU meshes no longer keep an expanded CPU vertex copy, and per-frame renderer bookkeeping no longer allocates.

### Packages

- `Simple3D.Maui` depends on the oldest MAUI it supports, `Microsoft.Maui.Controls` 10.0.0 or later, instead of the build's MAUI version, and on SkiaSharp 3.119.2 or later.
- Each platform's assembly contains only its own renderer (OpenGL ES and its shaders only on Android, Metal only on Apple platforms). Both libraries are trimming and AOT compatible, so apps' Release builds drop unused library code.

### Gallery and repository

- A redesigned gallery: a scene grid opening full-screen viewers, a renderer chip and sheet, a banner when Auto falls back to software, a sidebar layout on tablets and Mac, and new app icon, colors and fonts.
- The Mirror scene: a VRM character (four to choose from) copies the body, face and head the camera sees, using Apple Vision or Google ML Kit on the device, with guided framing, camera switching, pinch zoom and haptics.
- `global.json` and `.xcode-version` pin the .NET SDK, workload set and Xcode for local builds and CI; `scripts/check-toolchain.sh` checks a machine against them. CI builds every platform and checks the packages against the minimum and current MAUI on each pull request.

## 0.2.0-preview.1 · 2026-10-14

- `Texture2D` image textures with nearest/bilinear filtering and clamp/repeat addressing; `Material(Texture2D)` and texture-ready box, sphere, cylinder and pyramid UVs.
- `TextureLoader.Load` decodes opaque PNG/JPEG and other Skia-supported images into textures, writing directly into texture storage.
- Configurable `Scene.Light` (`DirectionalLight` direction, color, intensity) and `Scene.AmbientLight`; defaults keep the previous appearance.
- Optional mesh normals enable smooth Gouraud shading; `Mesh.WithCalculatedNormals()` computes area-weighted normals.
- Optional per-vertex colors multiply material color and texture.
- `ObjReader` imports bounded OBJ geometry with UVs, normals, negative indices and convex polygons.
- Perspective-correct attribute interpolation, including across near-plane clipping; solid flat materials keep their separate fast pixel loop.
- Gallery: Equipment shows a textured imported controller; Surface compares flat/smooth shading and light from the left or right.

## 0.1.0-preview.1 · 2026-10-04

- Portable scene hierarchy, immutable indexed meshes, perspective and orthographic cameras.
- CPU depth rendering with visible-leaf picking, owned snapshots and reusable targets.
- MAUI control for Android, iOS and Mac Catalyst with orbit, zoom, selection feedback and labels.
- `SceneView.ClearSelection()` clears feedback from application commands; gallery Reset also clears the selected part.
- Toggling `IsInteractive` preserves gestures supplied by the host application.
- Five focused gallery examples: equipment, robot arm, telemetry, indexed surface and gantry crane. Each has its own source file; tests retain the broader regression corpus.
- Gallery animation pauses when its window enters the background and ignores queued ticks after stopping.
- Bounded raster work, reduced gesture preview resolution and optional diagonal-edge filtering.
- Indexed triangle traversal reduces allocation when rendering many small mesh instances.
- Reusable node preparation reduces per-frame allocation without sharing picking snapshots.
- MIT licensing, public API documentation, architecture diagrams and independent package consumers.
- Portable symbols, Source Link and SDK package validation for preview packages.

Install [Simple3D.Maui](https://www.nuget.org/packages/Simple3D.Maui/0.1.0-preview.1) for MAUI apps or [Simple3D.Core](https://www.nuget.org/packages/Simple3D.Core/0.1.0-preview.1) for headless use. Rendering supports opaque geometry and fixed directional lighting; image textures, transparency and shadows are outside this preview.

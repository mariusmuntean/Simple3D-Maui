# Changelog

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

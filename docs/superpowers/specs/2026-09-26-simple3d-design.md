# Simple3D design

## Intent

Create a small .NET MAUI library for interactive, shaded 3D illustrations on iOS, Android and macOS (Mac Catalyst). Consumers should be able to describe a scene with a few lines of C#, use built-in shapes, and place a view in a MAUI layout. The demo should teach the API by example.

## Approach

Use a portable .NET 10 scene and projection library with `System.Numerics` and a thin MAUI `GraphicsView` adapter. This has no native graphics dependencies and handles simple scenes consistently across platforms. An alternative native Metal/OpenGL renderer would handle large meshes and interpenetrating surfaces better but would add three platform implementations and more setup. A third-party scene engine would add weight and expose a larger API. Choose the portable renderer for the stated small-drawing use case.

## Public surface

`Scene` contains `Shape` instances (`Box`, `Sphere`, `Cylinder`, `Pyramid`), each with position, rotation, size and color. `SceneView` exposes `Scene`, `Orbit(yawDelta, pitchDelta)`, `Zoom(factor)`, and `ResetCamera()`. Default camera frames the origin; pointer drag rotates, pinch zooms. A `SceneRenderer.Render(scene, camera, width, height)` returns ordered projected triangles and can be tested without MAUI.

## Rendering

Reuse cached unit meshes; transform with `Matrix4x4`; perspective project through an orbit camera; discard triangles crossing the near plane initially, cull back faces, calculate flat diffuse lighting, and sort visible faces back to front. Draw filled triangles through `ICanvas`. Aim for low allocation and no background work in a frame. Do not promise general CAD correctness: painter ordering has limitations for interpenetrating objects and transparency; clipping near the camera is a deliberate small-scene constraint.

## Validation and delivery

Portable tests cover geometry counts, projection, visibility, camera limits, draw ordering, and invalid viewport/inputs. CI runs tests on Linux and compiles Android, iOS and Mac Catalyst on macOS. README includes installation, a minimal snippet, interaction and limitations. Milestone commits record design, core, and MAUI/demo/automation. Private GitHub repository is the intended source of truth.

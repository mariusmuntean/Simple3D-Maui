# Simple3D

Small, approachable 3D drawings in .NET MAUI. Build an opaque scene from familiar shapes, place a `SceneView` in a page, and drag to look around. It runs on **iOS, Android and macOS through Mac Catalyst** with .NET 10.

```csharp
using Simple3D.Core;
using Simple3D.Maui;

var view = new SceneView
{
    HeightRequest = 320,
    Scene = new Scene()
        .Add(Shape.Box(0xFF8DA9FF).At(-.7f, 0, 0).Rotated(0, .4f, 0))
        .Add(Shape.Sphere(0xFFFFB775).At(.7f, 0, 0))
};
Content = view;
```

Drag to orbit, pinch to zoom, or call `view.Orbit(yawRadians, pitchRadians)`, `view.Zoom(factor)`, and `view.ResetCamera()`. Assign a new `Scene` to change composition; call `view.Refresh()` when changing the existing scene. Shapes are immutable; `At`, `Rotated` and `Scaled` return transformed instances. Colors use opaque ARGB values (`0xFFRRGGBB`); transparent values are rejected. Angles are radians. The built-in unit shapes are centered at the origin; use `Scaled` for size.

## Projects

- `src/Simple3D.Core`: portable `net10.0` meshes, camera and deterministic projector. No MAUI or third-party dependency.
- `src/Simple3D.Maui`: `SceneView` using the standard MAUI `GraphicsView` canvas and touch events.
- `samples/Simple3D.Demo`: three selectable scenes, orbit and zoom controls; launch this project on a device or simulator.
- `tests/Simple3D.Core.Tests`: dependency-free executable regression tests.

Add project references to `Simple3D.Core` and `Simple3D.Maui`, or pack both locally. Install the .NET 10 MAUI workload and platform SDKs, then run the demo:

```bash
dotnet workload install maui
dotnet run --project samples/Simple3D.Demo -f net10.0-maccatalyst
dotnet run --project tests/Simple3D.Core.Tests -c Release
```

On Android and iOS, select `net10.0-android` or `net10.0-ios` and a device/simulator. CI runs the portable tests, builds each demo target, and packs both libraries. The iOS simulator opens all three gallery scenes and compares their rendered drawings; the Android emulator opens the gallery. CI validates screenshots against the gallery's background, panel, and shape colors, and uploads them for inspection.

## Scope and performance

Meshes are shared between instances. The renderer transforms and shades opaque triangles, removes back faces, clips against the near plane, then sorts them by depth for a lightweight native canvas draw. It is designed for small illustrations and diagrams, with a modest sphere tessellation. It is a software renderer, not a GPU scene engine. Intersecting geometry may sort incorrectly; transparent materials, mesh import, and hidden-surface depth buffers are outside the current scope. The test runner reports a per-frame rendering baseline for twelve spheres, without imposing a fragile time threshold. Test performance with your intended scene size and device before using dense or animated scenes.

## Milestones

See `docs/superpowers/` for the scope and implementation plan; git history records design, portable core and MAUI/demo milestones. GitHub Actions performs repeatable validation for pull requests and main branch pushes.

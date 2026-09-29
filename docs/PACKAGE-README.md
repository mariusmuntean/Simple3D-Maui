# Simple3D

Simple3D provides a portable scene and depth renderer (`Simple3D.Core`) and a touch-enabled SkiaSharp control for .NET 10 MAUI (`Simple3D.Maui`). The control supports iOS, Android and Mac Catalyst.

Use `Simple3D.Core` for rendering without a UI framework. In a MAUI app, install `Simple3D.Maui`; it includes Core as a dependency. Keep the host's normal `Microsoft.Maui.Controls` reference, register `UseSkiaSharp()` on the app builder, then assign a `Scene` to a `SceneView`. Shapes, custom indexed meshes, groups, camera fitting, labels and selection of visible shapes are supported.

Set `SceneView.IsInteractive = false` for a display-only scene. For animation, replace immutable shapes on the UI thread and lower `MaximumRenderDimension` while animating. Core's reusable `RenderTarget` avoids allocating a new pixel snapshot for each frame; use `RenderFrame` when you need to retain an owned snapshot.

For complete setup, examples, limits and API documentation, visit the [repository](https://github.com/mariusmuntean/Simple3D-Maui).

# Getting started

## Requirements

Use .NET 10 with the target's MAUI workload and an iOS, Android or Mac Catalyst app. Windows is not supported; remove a Windows target from a default MAUI project before restoring. Apple builds require macOS and a workload-compatible Xcode.

Preview packages are not yet on NuGet.org. Follow [Build packages and install from a local feed](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/docs/PACKAGING.md), including the persistent `NuGet.Config`, then return here to create a scene.

Core is a transitive dependency. For a source checkout, reference `src/Simple3D.Maui` instead. Core-only applications can install `Simple3D.Core` without MAUI workloads. Register the SkiaSharp handler in the host app:

```csharp
using SkiaSharp.Views.Maui.Controls.Hosting;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>().UseSkiaSharp();
```

In a `ContentPage`, create a scene and show it:

```csharp
using Simple3D.Core;
using Simple3D.Maui;

var scene = new Scene()
    .Add(Shape.Box(0xFF8DA9FF).Named("Case").At(-0.7f, 0, 0))
    .Add(Shape.Sphere(0xFFFFB775).Named("Ball").At(0.7f, 0, 0));

var view = new SceneView { Scene = scene, HeightRequest = 320 };
view.Camera.FitToScene(scene, aspectRatio: 4f / 3);
Content = view;
```

For a display-only scene, set `IsInteractive = false`. This removes the view's orbit, pinch and tap gestures. The default is interactive; you can switch the property at runtime. The camera can still be changed in code.

For simple animation, replace immutable shapes on the UI thread. Each replacement invalidates the frame. Set `MaximumRenderDimension` to a smaller physical size while animating to bound the software rasterizer's per-frame work, then restore it when the animation stops. The [gallery demo](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Demo/GalleryPage.cs) gives each scene its own time-based motion. Its timer targets 60 updates per second and stops when the page disappears or its window enters the background (`Window.Stopped`). Returning to the app leaves it paused until Animate is pressed. Ignore already queued ticks after stopping a timer. Actual rendered frame rate depends on the device, viewport and scene.

Colors passed to Core use opaque `0xAARRGGBB` values. `SceneView.SceneBackgroundColor` accepts a MAUI `Color` and must be opaque. Angles are radians. Shape transforms return copies; adding a changed shape requires `Scene.Replace` or `Scene.Add`. `Scene.Changed` and `Camera.Changed` trigger a redraw automatically. Keep scene and camera mutations on the UI thread.

Run the demo app or explore the [examples](examples.md). On Mac Catalyst, use the .NET installation with MAUI workloads and a compatible Xcode.

# Simple3D

Simple3D provides a portable scene and depth renderer (`Simple3D.Core`) and a touch-enabled SkiaSharp control for .NET 10 MAUI (`Simple3D.Maui`). The control supports iOS, Android and Mac Catalyst.

Install `Simple3D.Maui` in a .NET 10 MAUI app; it brings in `Simple3D.Core`. For headless rendering, install Core alone. Register `UseSkiaSharp()` on the app builder, then assign a `Scene` to a `SceneView`. Shapes, custom indexed meshes, groups, camera fitting, labels and depth-aware picking are supported.

For Core-only rendering:

```csharp
using Simple3D.Core;

var scene = new Scene().Add(Shape.Box(0xFF8DA9FF));
var camera = new Camera();
camera.FitToScene(scene, 4f / 3);
var frame = new DepthRenderer().Render(scene, camera, 320, 240);
Shape? visible = frame.Pick(160, 120);
ReadOnlyMemory<uint> pixels = frame.Pixels;
```

For a MAUI app:

```csharp
using Simple3D.Core;
using Simple3D.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

// In MauiProgram:
builder.UseMauiApp<App>().UseSkiaSharp();

// In a ContentPage:
var scene = new Scene().Add(Shape.Box(0xFF8DA9FF).Named("Case"));
var view = new SceneView { Scene = scene, HeightRequest = 320 };
view.Camera.FitToScene(scene, 4f / 3);
Content = view;
```

Drag to orbit, pinch to zoom and tap to select. Use `IsInteractive = false` for display-only content. Keep scene and camera mutations on the UI thread. Rendering uses opaque triangles and fixed directional lighting; image textures, transparency and shadows are unsupported.

Read the [setup, showcases, limits and API documentation](https://mariusmuntean.github.io/Simple3D-Maui/). Source and the interactive gallery are in the [repository](https://github.com/mariusmuntean/Simple3D-Maui). Licensed under MIT, copyright Marius Muntean.

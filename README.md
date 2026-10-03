# Simple3D

![Simple3D](docs/site/images/brand-banner.svg)

Small 3D scenes for .NET 10 MAUI on iOS, Android and Mac Catalyst. Build a scene from shapes or an indexed mesh, display it in a `SceneView`, and let users orbit, zoom and select parts. Use it for equipment diagrams, data displays, scientific models and other small illustrations.

![Equipment scene](docs/site/images/Equipment.png)

## Start with a scene

Preview packages are not yet on NuGet.org. Build and install them from a local feed using the [package instructions](docs/PACKAGING.md), or reference `src/Simple3D.Maui` from your MAUI app. Core is a transitive dependency. Register SkiaSharp in `MauiProgram`:

```csharp
using SkiaSharp.Views.Maui.Controls.Hosting;

builder.UseMauiApp<App>().UseSkiaSharp();
```

In a `ContentPage`:

```csharp
using Simple3D.Core;
using Simple3D.Maui;

var scene = new Scene()
    .Add(Shape.Box(0xFF8DA9FF).Named("Case").At(-.7f, 0, 0))
    .Add(Shape.Sphere(0xFFFFB775).Named("Ball").At(.7f, 0, 0));
var view = new SceneView { Scene = scene, HeightRequest = 320 };
view.Camera.FitToScene(scene, aspectRatio: 4f / 3);
Content = view;
```

Drag to orbit, pinch to zoom and tap to select. Selected parts receive a tint and contour that follow them during animation. Set `IsInteractive = false` for display-only content. Subscribe to `SelectionChanged` to inspect the selected shape.

Core colors use opaque `0xFFRRGGBB`; angles use radians. Shape transforms return copies and set absolute values. Replace animated shapes with `Scene.Replace` on the UI thread; scene and camera changes trigger a redraw.

## Try the gallery

Open [Simple3D.sln](Simple3D.sln) in Rider or another MAUI IDE. Choose **Simple3D.Demo** and an Android device, iOS simulator or Mac Catalyst target. The gallery contains fifteen scenes with animations specific to each subject. Read the [scene factories](samples/Simple3D.Shared/DemoScenes.cs) to adapt an example. Patterned Surface demonstrates selectable checker cells and directional shading using colored meshes.

## Documentation

Read the [documentation site](https://mariusmuntean.github.io/Simple3D-Maui/), including the API reference, or browse the sources below.

- [Getting started](docs/site/getting-started.md)
- [Architecture and rendering pipeline](docs/site/architecture.md)
- [Scenes and cameras](docs/site/scenes-and-cameras.md)
- [Meshes and groups](docs/site/meshes-and-groups.md)
- [Interaction and selection](docs/site/interaction-and-picking.md)
- [Showcases](docs/site/examples.md)
- [Rendering limits](docs/site/rendering-limits.md)

The software renderer supports opaque triangles, depth-aware picking and fixed directional flat lighting. It has no transparency, texture mapping or shadows. Labels overlay geometry. Viewport and work budgets bound rendering costs; frame rate depends on scene complexity, viewport and device. See the limits page before choosing it for your application.

## Develop

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release
dotnet run --project tests/Simple3D.Maui.Tests -c Release
```

See [development and native validation](docs/DEVELOPMENT.md) for Rider setup, documentation builds, image generation and performance comparisons, and [packaging](docs/PACKAGING.md) for independent package consumers. Checks run locally; hosted native validation requires a manual trigger.

## License and contributions

[MIT](LICENSE), copyright Marius Muntean. Report bugs or suggest changes through issues and pull requests. Include the platform, build configuration and a small reproduction. The maintainer reviews changes and publishes official releases.

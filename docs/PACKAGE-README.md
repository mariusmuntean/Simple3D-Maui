# Simple3D

Simple3D provides a portable scene and depth renderer (`Simple3D.Core`) and a touch-enabled SkiaSharp control for .NET 10 MAUI (`Simple3D.Maui`). The control supports iOS, Android and Mac Catalyst.

Install both packages in a MAUI app, register `UseSkiaSharp()` on the app builder, then assign a `Scene` to a `SceneView`. Shapes, custom indexed meshes, groups, camera fitting, labels and depth-aware picking are supported.

Read the [setup, showcases, limits and API documentation](https://mariusmuntean.github.io/Simple3D-Maui/). Source and the interactive gallery are in the [repository](https://github.com/mariusmuntean/Simple3D-Maui). Licensed under MIT, copyright Marius Muntean.

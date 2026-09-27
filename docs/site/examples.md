# Executable examples

The portable console runner renders the same seven scene factories used by the MAUI gallery. It writes binary PPM images using only the Core library and .NET. Each scene demonstrates a different use:

| Scene | Capability | Image |
| --- | --- | --- |
| Equipment | Named grouped primitives, lines, arrows, labels | [Equipment](images/Equipment.png) |
| Packing | Orthographic camera, repeated packages | [Packing](images/Packing.png) |
| Surface | Procedural indexed mesh and depth picking | [Surface](images/Surface.png) |
| Assembly | Pickable parts of a camera module | [Assembly](images/Assembly.png) |
| Molecule | Scientific model made from spheres and bonds | [Molecule](images/Molecule.png) |
| Telemetry | Orthographic data chart with named values | [Telemetry](images/Telemetry.png) |
| City | Architectural massing with selectable blocks | [City](images/City.png) |

Render one scene or all seven:

```bash
dotnet run --project samples/Simple3D.Examples -c Release -- Equipment equipment.ppm
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
```

The runner logs node and projected-label counts. Open PPM files in an image viewer or convert them to PNG. The documentation images are generated from that runner with `python3 scripts/render-doc-images.py`. The [scene factories](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/DemoScenes.cs) are concise working examples of groups, meshes, materials, and camera fitting. The [gallery app](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Demo/GalleryPage.cs) adds touch interaction.

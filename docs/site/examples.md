# Executable examples

The portable console runner renders the same seven scene factories used by the MAUI gallery. It writes binary PPM images using only the Core library and .NET. Each scene demonstrates a different use:

| Scene | Capability | Gallery animation | Image |
| --- | --- | --- | --- |
| Equipment | Named grouped primitives, lines, arrows, labels | Output vector sweeps | [Equipment](images/Equipment.png) |
| Packing | Orthographic camera, repeated packages | Packages lift and settle | [Packing](images/Packing.png) |
| Surface | Procedural indexed mesh and depth picking | Sample marker follows the wave | [Surface](images/Surface.png) |
| Assembly | Pickable parts of a camera module | Front element travels to focus | [Assembly](images/Assembly.png) |
| Molecule | Scientific model made from spheres and bonds | Whole molecule rotates | [Molecule](images/Molecule.png) |
| Telemetry | Orthographic chart with named samples | Bars change height and color within 0–2.5 | [Telemetry](images/Telemetry.png) |
| City | Architectural massing with selectable blocks | Building masses rise and settle | [City](images/City.png) |

Render one scene or all seven:

```bash
dotnet run --project samples/Simple3D.Examples -c Release -- Equipment equipment.ppm
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
```

The runner logs node and projected-label counts and exports the initial still frame. Open PPM files in an image viewer or convert them to PNG. The documentation images are generated from that runner with `python3 scripts/render-doc-images.py`. The [scene factories](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/DemoScenes.cs) are concise working examples of groups, meshes, materials, camera fitting and time-based animation. The [gallery app](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Demo/GalleryPage.cs) adds touch interaction and a 60-updates-per-second animation target. Actual displayed frame rate depends on the device and viewport.

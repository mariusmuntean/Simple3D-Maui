# Executable examples

The portable console runner renders the same three scene factories used by the MAUI gallery. It writes binary PPM images using only the Core library and .NET. Each scene demonstrates a different capability:

| Scene | Capability | Image |
| --- | --- | --- |
| Equipment | Named grouped primitives, lines, arrows, labels | [Equipment](images/Equipment.png) |
| Packing | Orthographic camera, repeated packages | [Packing](images/Packing.png) |
| Surface | Procedural indexed mesh and depth picking | [Surface](images/Surface.png) |

Render one scene or all three:

```bash
dotnet run --project samples/Simple3D.Examples -c Release -- Equipment equipment.ppm
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
```

The runner logs node and projected-label counts. Open PPM files in an image viewer or convert them to PNG. The documentation images are generated from that runner with `python3 scripts/render-doc-images.py`. The [scene factories](https://github.com/mariusmuntean/Simple3D-Maui/blob/maturity-tests/samples/Simple3D.Shared/DemoScenes.cs) are also concise working examples of groups, meshes, materials, and camera fitting. The [gallery app](https://github.com/mariusmuntean/Simple3D-Maui/blob/maturity-tests/samples/Simple3D.Demo/GalleryPage.cs) adds touch interaction.

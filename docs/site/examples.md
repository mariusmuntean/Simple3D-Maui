# Executable examples

The portable console runner renders the same fourteen scene factories used by the MAUI gallery. It writes binary PPM images using only the Core library and .NET. Each scene demonstrates a different use:

| Scene | Capability | Gallery animation | Image |
| --- | --- | --- | --- |
| Equipment | Named grouped primitives, lines, arrows, labels | Output vector sweeps | [Equipment](images/Equipment.png) |
| Packing | Orthographic camera, repeated packages | Packages lift and settle | [Packing](images/Packing.png) |
| Surface | Procedural indexed mesh and depth picking | Sample marker follows the wave | [Surface](images/Surface.png) |
| Assembly | Pickable parts of a camera module | Front element travels to focus | [Assembly](images/Assembly.png) |
| Molecule | Scientific model made from spheres and bonds | Whole molecule rotates | [Molecule](images/Molecule.png) |
| Telemetry | Orthographic chart with named samples | Bars change height and color within 0–2.5 | [Telemetry](images/Telemetry.png) |
| City | Architectural massing with selectable blocks | Building masses rise and settle | [City](images/City.png) |
| Robot Arm | Nested transforms and individually pickable joints | Shoulder and elbow articulate | [Robot Arm](images/Robot%20Arm.png) |
| Orbit | Parent-child motion and an orbital path | Planet revolves while its moon follows | [Orbit](images/Orbit.png) |
| Wind | Three blades sharing a rotating hub | Turbine rotor spins while its tower stays fixed | [Wind](images/Wind.png) |
| Conveyor Inspection | Visual inspection and status on a production line | Parcels cross a fixed scanner and change color after passing | [Conveyor Inspection](images/Conveyor%20Inspection.png) |
| Solar Tracker | Energy equipment with a moving light source | A panel tilts to follow the sun above its fixed mount | [Solar Tracker](images/Solar%20Tracker.png) |
| Packet Routing | Selectable network devices, links and traffic | Packets traverse two links through a router | [Packet Routing](images/Packet%20Routing.png) |
| Drone Survey | A field survey vehicle with individually pickable parts | The drone hovers as four propellers spin over its pad | [Drone Survey](images/Drone%20Survey.png) |

Render one scene or all fourteen:

```bash
dotnet run --project samples/Simple3D.Examples -c Release -- Equipment equipment.ppm
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
```

The runner logs node and projected-label counts and exports the initial still frame. Open PPM files in an image viewer or convert them to PNG. The documentation images are generated from that runner with `python3 scripts/render-doc-images.py`. The [scene factories](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/DemoScenes.cs) are concise working examples of groups, meshes, materials, camera fitting and time-based animation. The [gallery app](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Demo/GalleryPage.cs) adds touch interaction and a 60-updates-per-second animation target. Actual displayed frame rate depends on the device and viewport.

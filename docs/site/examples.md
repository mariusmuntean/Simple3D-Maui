# Showcases

The MAUI gallery uses fifteen shared scene factories. Each demonstrates a different use:

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
| Solar Tracker | Energy equipment with a moving sun marker | A panel tilts to face the marker above its fixed mount | [Solar Tracker](images/Solar%20Tracker.png) |
| Packet Routing | Selectable network devices, links and traffic | Packets traverse two links through a router | [Packet Routing](images/Packet%20Routing.png) |
| Drone Survey | A field survey vehicle with individually pickable parts | The drone hovers as four propellers spin over its pad | [Drone Survey](images/Drone%20Survey.png) |
| Patterned Surface | Checker cells made from colored meshes with fixed directional lighting | Surface tilts to show shading; each cell stays selectable | [Patterned Surface](images/Patterned%20Surface.png) |

Render one scene or all fifteen:

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images Equipment equipment.ppm
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images --all output
```

The Core runner exports the initial still frame; `--render-images` skips its tests. Open PPM files in an image viewer or convert them to PNG. Run `python3 scripts/render-doc-images.py` to regenerate the documentation images. The shared factories demonstrate groups, meshes, materials, camera fitting and time-based animation. The gallery adds selection, orbit and zoom with a 60-updates-per-second animation target. Actual displayed frame rate depends on the device and viewport. The Solar Tracker's sun is a visual marker; lighting uses the renderer's fixed direction.

Patterned Surface uses separate colored mesh cells, not image texture mapping. Its parts are built once and reused during animation. The current renderer provides flat directional lighting; it does not provide physically based materials or shadows.

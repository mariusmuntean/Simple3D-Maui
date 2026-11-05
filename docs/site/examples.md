# Five examples to learn from

Create the two-shape scene in [Getting started](getting-started.md) first, then follow these five examples in order. Each adds a different API concept, progressing from primitives to a mechanism with coordinated moving parts. Each file creates a scene, fits its camera and supplies a time-based animation. The gallery adds orbit, zoom and selection through the same `SceneView` you use in an app. The texture, lighting, normals, vertex-color and OBJ examples require `0.2.0-preview.1` or later.

| Example and source | What to learn | Motion |
| --- | --- | --- |
| [Equipment](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Equipment.cs) | Named primitives and groups; an OBJ controller with UV-mapped display | Output vector sweeps |
| [Telemetry](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Telemetry.cs) | Orthographic camera and data-driven height and color | Bars change within a fixed scale |
| [Surface](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Surface.cs) | Height-based vertex colors, flat/smooth normals and directional light controls | Sample marker follows a wave |
| [Robot Arm](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/RobotArm.cs) | Nested transforms and selectable joints | Shoulder and elbow articulate |
| [Gantry Crane](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/GantryCrane.cs) | Linked motion with fixed structure and reusable geometry | Carriage traverses while the hoist lifts its load |

![Equipment](images/Equipment.png)
![Telemetry](images/Telemetry.png)
![Surface](images/Surface.png)
![Robot Arm](images/Robot%20Arm.png)
![Gantry Crane](images/Gantry%20Crane.png)

In **Equipment**, orbit the controller beside the motor: the chart belongs to a mapped front face, while the solid sides reveal the imported enclosure's depth. `ObjReader.Parse` imports the wedge and its independent UV indices once. A small opaque `Texture2D` supplies its display and body colors. The texture is generated in the shared sample so Core-only image exports need no codec; use [TextureLoader.Load](materials-and-import.md#textures) in a MAUI app to decode an image once before creating the material.

In **Surface**, choose **Shading: smooth / flat**, then **Light: from right / left**. Smooth mode uses averaged vertex normals; flat mode omits normals from the same wave geometry. Both keep the height gradient and 512 triangles, so the comparison isolates lighting rather than changing mesh detail. The light-ray direction changes in world space while the orbit camera stays put. [SurfaceComparison.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/SurfaceComparison.cs) keeps both meshes and lights prepared; switching does no geometry generation and preserves selection. **Reset** restores smooth shading, light from the right, the initial camera and sample-marker position. The comparison controls appear only in Surface, in the horizontally scrollable tool row.

Telemetry and Gantry Crane use synthetic values, not live sensors or physical simulations. In an application, replace the time functions with measured values. Reuse immutable meshes and keep child order stable so selection follows the same logical part after replacement. The small shared helpers live in [DemoScenes.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/DemoScenes.cs).

Robot Arm demonstrates child transforms relative to a parent. Gantry Crane combines group translation with state-dependent part dimensions: the cable changes length as the load rises. They teach different uses of the same hierarchy API. The examples share their animation and color helpers rather than repeating them in each file.

The [gallery page](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Demo/GalleryPage.cs) owns the UI timer, lowers render resolution during animation and pauses on backgrounding. It targets 60 updates per second; this is not a displayed-frame-rate guarantee. Press Animate after returning to the app.

## Mirror

![Mirror: a tracked person and the character copying them](images/mirror.gif)

The gallery's **Mirror ✦** button opens a full-screen scene in which a character copies the person the camera sees, in portrait or landscape (landscape leaves room for outstretched arms).

- **Guided start.** It asks for the camera, then shows a silhouette until someone is in frame: their whole body, from the waist up, or just their head for face tracking. Whatever is out of frame holds still; legs out of frame keep the character standing.
- **Body.** Limbs follow the landmark directions, with depth estimated from foreshortening; the spine curves for bows and side bends; turning around shows the character's back; arms stay outside the torso and go behind it when the hand is hidden.
- **Face and head.** Mouth, each eye, smile and frown drive the model's VRM expressions; the head turns, nods and tilts with the person's.
- **Smooth.** Detectors deliver poses at camera rate (25–30 Hz); the shown pose eases toward each at the display's rate (`FrameClock`, up to 240 Hz).
- **Choices.** ☺ picks one of five CC0 characters (animated swap), ⇄ switches cameras, pinching zooms the camera. When the person leaves, the character holds its pose and a hint asks them back; haptics mark both moments. Video stays on the device.

| Piece | Source |
| --- | --- |
| Tracking states, smoothing, face calibration | [PoseTracker.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Mirror/PoseTracker.cs) |
| Landmarks → humanoid bone rotations | [HumanoidRetargeter.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Mirror/HumanoidRetargeter.cs) |
| Face measurements | [FaceGeometry.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Mirror/FaceGeometry.cs) |
| Pose easing and expressions | [MirrorSession.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Mirror/MirrorSession.cs) |
| Pond stage, character placement, swaps | [MirrorStage.cs](https://github.com/mariusmuntean/Simple3D-Maui/blob/main/samples/Simple3D.Shared/Mirror/MirrorStage.cs) |
| Camera, detectors and page | [Simple3D.Demo/Mirror](https://github.com/mariusmuntean/Simple3D-Maui/tree/main/samples/Simple3D.Demo/Mirror) |

To reproduce it in your app, load a VRM with `GltfLoader`, wrap it with `MirrorCharacter.FromVrm`, feed `PoseFrame`s from your detector into `MirrorSession.Apply`, and call `MirrorSession.Present` from a `FrameClock` tick; the shared folder has no MAUI or platform dependencies. Detection runs on the device: Apple Vision is built into iOS and macOS; on Android, ML Kit's accurate pose and face models arrive with their NuGet packages. Detectors report 2D landmarks, so motion straight toward the camera is approximate. The characters are CC0 VRMs from ToxSam and Polygonal Mind's 100Avatars; see [skinned characters](skinned-characters.md). The GIF is rendered from recorded landmarks with `--render-showcase` and `scripts/mirror-showcase.py`.

## Render without MAUI

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images Equipment equipment.ppm
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images --all output
```

This exports initial still frames as binary PPM and skips the test run. Convert them to PNG with `python3 scripts/render-doc-images.py` to regenerate the documentation images. Rendering supports opaque textured triangles and flat or smooth directional lighting; see [limits](rendering-limits.md) before choosing a workload.

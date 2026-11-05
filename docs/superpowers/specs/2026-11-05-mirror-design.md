# Mirror: camera-driven skinned character

Approved direction (2026-11-05): a new gallery scene, not the opening one, in which a rigged glTF character copies the full-body pose of a person seen by the device camera. It showcases skinned animation, the GPU renderers and frame rate. The landing page will feature a recording.

## User flow

1. **Intro.** Explains that the scene uses the camera to copy a person's movement; a Start button.
2. **Permission.** Start requests camera access. Denied: explain and offer Settings; the rest of the gallery still works.
3. **Guide.** Live camera preview with a translucent person silhouette and "Point the camera at someone and fit their whole body in the frame."
4. **Tracking.** A full body for several consecutive frames reveals the character in its scene, copying the pose. Success haptic.
5. **Lost.** No full body for ~0.6 s shows a small hint, "Step back into the frame", and holds the last pose. Warning haptic. Reacquiring hides the hint (success haptic again).

## Library (Core) additions

- `Skeleton` (joints: name, parent, rest TRS, affine inverse bind matrix; ≤128 joints), `SkeletonPose` (local joint rotations + root translation, cached joint matrices), `SkinnedMesh` (bind `Mesh` + up to four joint influences per vertex + skeleton).
- `Shape.FromSkinnedMesh` and `Shape.WithPose`, which applies to every skinned descendant using that skeleton. Shapes stay immutable; a pose change is a shape replacement like any animation.
- Software renderer, picking and bounds use CPU-skinned positions/normals, computed lazily once per posed shape.
- Metal and GLES skin in the vertex shader: bind-pose geometry uploads once; each frame uploads only the joint palette.
- `GltfReader` (Core): binary glTF 2.0 / VRM 0.x. Meshes, indices, normals, UV0, vertex colors, base color factor/texture (image bytes returned for platform decoding), node hierarchy, skins, VRM humanoid bone map. Unsupported: morph targets, animations, alpha blending (rendered opaque), PBR (base color only), cameras, sparse accessors, Draco/KTX2. Degenerate triangles are dropped. Skins over one armature (equal inverse binds for shared joints) merge into one skeleton, so one pose moves every part. `Simple3D.Maui.GltfLoader` decodes textures.

## Sample

- Portable (`samples/Simple3D.Shared/Mirror`): `PoseFrame` (13 body landmarks in normalized image space with confidence), `PoseTracker` (searching/tracking/lost state machine with hysteresis), `PoseFilter` (One Euro smoothing), `HumanoidRetargeter` (landmark directions → humanoid bone rotations, mirrored, with foreshortening-based depth), the stylized pond scene built from library primitives, and the bundled CC0 character (100Avatars "Froggy", Polygonal Mind).
- Platform: camera frames (AVCaptureSession / CameraX) and video frames (AVAssetReader / MediaCodec) feed one detector: Apple Vision `VNDetectHumanBodyPoseRequest` (2D, simulator-capable) or Google ML Kit Pose Detection. No model files ship with the app.
- Haptics: `UINotificationFeedbackGenerator` success/warning on Apple, `HapticFeedbackConstants.CONFIRM`/`REJECT` on Android (MAUI fallback).

## Assumptions checked during implementation

- Apple Vision's 2D body pose runs on macOS/Mac Catalyst (17.6 ms per 640×360 frame) but not on the iOS simulator ("Unable to setup request"); the simulator replays recorded landmarks. The 3D request is not used: it is device-only and slower.
- Mac Catalyst probes need an unlocked desktop session; a locked screen gives the app no window.
- 100Avatars VRM 0.x models face −Z and are T-posed; the stage turns them toward the camera and normalizes height.
- 2D landmarks give no depth: limbs swing toward their image direction and depth is estimated from foreshortening, so reaching straight at the camera is approximate.
- Sideways motion follows the person's position across the image, bounded to the island; scaling by leg length overshot when the person stood off-center.

## Validation without a physical camera

- A fixed CC-licensed-for-use stock video (Mixkit 44414, downloaded at test time, never committed) with an inserted empty segment exercises tracking, loss and reacquisition.
- Host: a macOS Vision tool extracts landmark fixtures from the video; portable tests retarget them, render with the software renderer and compare projected bone directions with the landmarks; a contact sheet supports visual review.
- Native: a Debug-only `SIMPLE3D_MIRROR_VIDEO` mode replaces the camera with the video on iOS simulator, Mac Catalyst and the Android emulator, and logs state transitions, bone-direction error and frame timings, ending in `MIRROR_PROBE_PASS`.
- Performance: skinning cost in the software renderer and GPU palette upload per frame; detector latency and displayed FPS are reported separately.

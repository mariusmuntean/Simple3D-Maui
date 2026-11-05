# Mirror pose pipeline implementation

Scope: implement the approved Mirror design's native input layer only. Keep `PoseFrame` and `IPoseSource` unchanged; no scene, retargeting, or page UI.

## Global constraints

- Frames contain 13 joints in upright, unmirrored, top-left normalized coordinates. Front-camera preview alone is mirrored.
- Native detection stays off the UI thread, one image at a time. Empty images still emit frames.
- Stop/dispose cancels work, waits for in-flight analysis and releases native resources; sources can restart after Stop.
- No bundled model files or committed test videos. Android dependencies are TFM-conditional.
- Only iPhone 17 Pro `2BAE44EE-B0AA-4B3F-B0FC-7776FD52A89F` and Android `Pixel_5_API_33_Android_13`, port 5580, may be used. Stop owned processes/devices.

## Steps

1. Add demo-only common `PoseSource` diagnostics (failure event, completion task, detection timings), `CameraPreview`, permissions, haptics and factory. Preserve the portable contract.
2. Apple: Vision detector, AVAssetReader paced playback, AVCaptureSession camera, preview handler. Shared code under Platforms/Apple/Mirror, excluded on Android.
3. Android: restore compatible ML Kit/CameraX bindings, detector, paced video decoder, lifecycle-bound CameraX camera, preview handler.
4. Add a Debug/native-probe-only runner and no-build launch script. Assert monotonic timestamps, image dimensions, joint count/ranges, and full-body detection; report all per-frame data and timing summaries. Test failure handling as well as real input.
5. Build native targets sequentially; run the full video on all three targets and inspect full-body rate and latency. Check unavailable camera behavior and Mac camera permission/start. Run both portable suites.
6. Review the combined diff and lifecycle/orientation behavior; record exact evidence and unresolved physical-device checks; commit on the current branch.

The Apple and Android implementations are independent after agreeing the common types, and can be implemented in parallel. The primary task owns glue, project integration, probe scripts and final validation; platform workers own only their platform directories. Native builds are coordinated to avoid shared restore outputs.

# Development and native validation

## Toolchain and Rider

Use .NET 10 with MAUI workloads. Check `dotnet --info` and `dotnet workload list`. In Rider, choose that CLI installation and its .NET SDK MSBuild under **Settings → Build, Execution, Deployment → Toolset and Build**. A second installation without workloads can produce missing references in the editor.

Use matching Xcode and Apple workload versions. The locally validated combination is SDK 10.0.401, workload set 10.0.401.1 and Xcode 27. Keep Xcode validation enabled. After a targeted terminal build, run `dotnet restore samples/Simple3D.Demo` before building all platforms in Rider; targeted restores can replace shared runtime assets.

The manual validation workflow pins that SDK and workload set, creating a job-local `global.json` to prevent a newer preinstalled SDK from taking precedence. Apple builds and documentation use the `xcode-27` runner with Xcode 27.0 selected explicitly. Update those versions together after local validation; installing the latest workload against an older Xcode can break an otherwise unchanged build.

Choose the `Simple3D.Demo` configuration with the appropriate platform icon. Android uses the checked-in manifest. If an iOS simulator launch returns `HE0042` / `NSPOSIXErrorDomain code 3`, stop the run and restart that simulator, then retry. Preserve other simulator sessions.

## Local checks

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release
dotnet run --project tests/Simple3D.Maui.Tests -c Release
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --performance
```

For a faster Core correctness run without the trailing timing benchmarks, append `-- --tests-only`. Allocation checks still run.

The performance runner reports selected animation and pan CPU medians, managed allocations and Core pixel hashes. Compare under similar machine load. It excludes native presentation and does not measure displayed FPS.

Build each native target sequentially. Android Debug uses JIT. Apple Debug compiles rendering libraries and framework assemblies, retaining interpreter support for the demo UI. Clean native outputs when changing interpreter configuration or workloads.

```bash
dotnet build samples/Simple3D.Demo -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true
dotnet build samples/Simple3D.Demo -f net10.0-ios -r iossimulator-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -r maccatalyst-arm64 -c Debug
bash scripts/ios-simulator-smoke.sh --render-probe
```

The Debug native probe checks pixels, picking and retained snapshots for each scene. Its completion marker is required. The iOS script selects an idle supported simulator, then stops its app and shuts down that simulator on completion or failure. Stop any Mac or Android test app after inspection.

Use the current `Simple3D.Demo.app` bundle. An obsolete `Simple3D Gallery.app` output can contain old code.

### Release on macOS

```bash
dotnet restore samples/Simple3D.Demo -p:Configuration=Release
dotnet clean samples/Simple3D.Demo -f net10.0-maccatalyst -c Release
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -c Release
open samples/Simple3D.Demo/bin/Release/net10.0-maccatalyst/Simple3D.Demo.app
```

Clean after workload changes or an AOT-module mismatch at startup. A stale `Microsoft.Maui.Controls.Xaml` module caused the observed Release abort; clean single-architecture and universal builds both launched and rendered. Release keeps the default compiled runtime.

Run `DOTNET_COMMAND="$HOME/.dotnet/dotnet" bash scripts/mac-release-smoke.sh` to clean, build and test the Release renderer. The script enables the native probe for that build, requires its completion marker and stops its own process on failure or timeout. Ordinary Release builds exclude the probe; clean and rebuild without `EnableNativeRenderProbe` for distribution. This checks native rendering, not the gallery UI or displayed frame rate.

## Images and documentation

The Core test runner also exports gallery still frames, without running tests:

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images --all output
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images Equipment equipment.ppm
```

The output format is binary PPM. Install Pillow and run `python3 scripts/render-doc-images.py` to regenerate documentation PNGs. Both the exporter and app use the five factories in `samples/Simple3D.Shared/`. Retired gallery scenes remain in `tests/Shared/RegressionScenes.cs` to preserve renderer and allocation coverage; the performance runner includes that larger corpus.

```bash
dotnet tool install docfx --tool-path .tools --version 2.81.0
.tools/docfx metadata docs/site/docfx.json --warningsAsErrors
.tools/docfx build docs/site/docfx.json --warningsAsErrors
python3 -m http.server 8000 --directory _site
```

DocFX needs the workload-enabled `dotnet` first in `PATH`; set `DOTNET_ROOT` if you have multiple installations. The built `_site` contains user documentation, images and generated API reference. Contributor setup and package validation remain in the repository's `docs` directory.

After reviewing `_site`, run `bash scripts/publish-docs.sh`. It publishes generated files to `gh-pages` through a temporary checkout and a normal push. It preserves the source checkout and cleans up its temporary directory. GitHub Pages serves the branch root; `.nojekyll` keeps DocFX assets intact. This triggers only the Pages deployment, not the manual MAUI validation workflow.

To test a Release iOS simulator build, use `BUILD_CONFIGURATION=Release bash scripts/ios-simulator-smoke.sh --render-probe` after building with `-p:EnableNativeRenderProbe=true`. Remove that property and rebuild for distribution. If ordinary Clean leaves stale registrar or SDK compiler cache files, move only the affected generated `bin/Release` and `obj/Release` target directories aside and rebuild.

## GPU validation

The portable suites now include scene preparation/projection/picking, rendering-mode failure policy, scene-error recovery, and the gallery's renderer picker exercised in every mode. Run the actual GLES shader pipeline on Linux with Mesa EGL/GLES installed:

```bash
sudo apt-get install libegl1 libgles2 libgl1-mesa-dri
dotnet run --project tests/Simple3D.Gpu.Tests -c Release
dotnet run --project tests/Simple3D.Gpu.Tests -c Release -- --performance
```

`GPU_TESTS_PASS` requires real shader compilation, framebuffer rendering/readback, depth, textures, filtering, culling, clipping, selection, image comparisons across the five gallery scenes, resize/empty-scene behavior, and warm-frame resource reuse. The test prints the renderer identity. `llvmpipe`/software Mesa validates the API path but does not measure hardware acceleration. It runs the same `NativeGpuValidation` checks as the on-device probes (gallery comparisons, single-pass vs composite, selection outline, empty scene) and prints their completed-frame timings and managed bytes per frame; it reports measurements rather than imposing a machine-dependent speed threshold. Resource-upload assertions are deterministic performance regression checks.

The `Portable and GLES rendering tests` workflow runs on PRs and can be started manually. The existing **Build and test** manual workflow additionally runs native Metal and Android GLES probes, storing their logs with the screenshots. Native probes must finish with `NATIVE_GPU_PROBE_PASS` and `NATIVE_GPU_VIEW_PROBE_PASS`; unavailability is a failure, never a skipped success.

On Apple, build the demo in Debug (or Release with `-p:EnableNativeRenderProbe=true`), then:

```bash
bash scripts/ios-simulator-smoke.sh --gpu-probe
# Mac Catalyst: run the executable from the built .app bundle:
SIMPLE3D_GPU_PROBE=1 path/to/Simple3D.Demo.app/Contents/MacOS/Simple3D.Demo
SIMPLE3D_GPU_VIEW_PROBE=1 path/to/Simple3D.Demo.app/Contents/MacOS/Simple3D.Demo
```

On a connected Android emulator/device after building a Debug APK:

```bash
bash scripts/android-emulator-smoke.sh --gpu-probe
```

Native probes render offscreen with the same backend/shaders, compare all gallery scenes to software with edge/rounding tolerance, require the single-pass and selection-composite paths to match, test depth, selection and outline width, and print completed+readback timings and managed bytes allocated per frame. This measures actual API completion plus explicit readback, not display FPS. A second stage (`SIMPLE3D_GPU_VIEW_PROBE=1`, or the `simple3d_gpu_view_probe` intent extra on Android) hosts a real `SceneView` in Hardware mode: it waits for presented hardware frames, picks, checks that an over-budget scene is reported without disabling hardware, switches Software/Hardware and Auto/Software/Auto, and disconnects and reattaches the handler. It also checks that frames without a selection take the single-pass path, that the label overlay is never repainted without labels and about once per GPU frame with them, that a transient failure (a simulated iOS background submission, or a real destroyed EGL context on Android) keeps hardware rendering, and that a permanent failure in Auto falls back to software. It also orbits the camera over an off-centre label, from a timer and from `FrameSubmitted`, and requires the overlay's label position to equal the GPU frame's after every main run-loop turn.

To check label sync on the composited screen itself, launch `SIMPLE3D_LABEL_SYNC_PROBE=timer` (or `=frame`; on Android the `simple3d_label_sync_probe` intent extra). It orbits a magenta marker with a white label at the same anchor and logs `LABEL_SYNC_READY ... motion_px_per_frame=N`. Take screenshots from outside the app (`adb exec-out screencap -p`, `xcrun simctl io <device> screenshot`, `screencapture`) and run `python3 scripts/check-label-sync.py <N/2> shots/*.png`: the label-to-marker offset must vary by less than half a frame of motion. The library's probe counters and fault hooks compile only into Debug builds or builds with `EnableNativeRenderProbe=true`. The `--gpu-probe` scripts run both stages. Backgrounding and performance still need the manual checks below. Probe code is absent from ordinary Release demo builds. Never distribute a Release build with `EnableNativeRenderProbe` enabled.

Before merging GPU changes, run native builds/probes on all three targets and manually exercise runtime switching, orbit/pinch/tap, selected animation, rotation/resizing and background/resume. For device performance, compare Software and Hardware on the same physical device in Release with matching scenes and render dimensions. The gallery shows the active backend so a fallback cannot masquerade as GPU performance.

## Mirror (camera pose) validation

Apple builds can keep a stale compiled `Info.plist` after you edit `Platforms/*/Info.plist`: the SDK's incremental check skips `_CompileAppManifest`. Delete `obj/<Configuration>/<tfm>/<rid>/AppManifest.plist` and rebuild. A missing `NSCameraUsageDescription` in the built bundle shows up as `PermissionException` when Mirror starts.


No physical camera is needed. Prepare a test clip once: download Mixkit stock video 44414 (one person, full body; Mixkit license, so never commit it) and insert an empty segment to exercise losing and finding the person:

```bash
curl -L -o /tmp/44414.mp4 https://assets.mixkit.co/videos/44414/44414-360.mp4
ffmpeg -i /tmp/44414.mp4 -filter_complex "[0:v]trim=0:14,setpts=PTS-STARTPTS[a];color=c=0x202428:s=640x360:r=25:d=3[g];[0:v]trim=14:30,setpts=PTS-STARTPTS[b];[a][g][b]concat=n=3:v=1:a=0,fps=25" -an -c:v libx264 -pix_fmt yuv420p /tmp/mirror-test.mp4
swift scripts/extract-pose-landmarks.swift /tmp/mirror-test.mp4 > /tmp/mirror-test.jsonl
```

`tests/Fixtures/mirror-landmarks.jsonl.gz` holds the extracted Apple Vision landmarks of that clip. The Core tests replay them through tracking and retargeting. `dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-mirror out 25` renders the posed character and prints CPU timings; `python3 scripts/mirror-contact-sheet.py /tmp/mirror-test.mp4 tests/Fixtures/mirror-landmarks.jsonl.gz out sheet.png` places each render beside its video frame and landmarks for review.

On devices, after a Debug build:

- `bash scripts/pose-probe.sh <maccatalyst|ios|android> /tmp/mirror-test.mp4` runs the platform detector over the clip and prints `POSE_FRAME` lines and `POSE_PROBE_PASS` with detection rate and latency. Mac Catalyst runs only in an unlocked desktop session (a locked screen gives the app no window).
- The iOS simulator cannot run Apple's body-pose model ("Unable to setup request"). `bash scripts/mirror-video-check.sh <simulator-udid> /tmp/mirror-test.mp4 /tmp/mirror-test.jsonl out` opens the Mirror page with the clip instead of the camera and its recorded landmarks instead of detection, logs `MIRROR_STATE`/`MIRROR_STATS` and takes timed screenshots. The same page runs on Mac Catalyst and Android with live detection when `SIMPLE3D_MIRROR_VIDEO` (Android: intent extra) is set without landmarks.

Measured on the test clip (Debug builds): Apple Vision on Mac Catalyst (M1 Pro) finds the whole body in 736 of 826 frames at about 18 ms per frame. ML Kit's accurate pose model on the Android emulator finds it in 81% of frames at about 28 ms (the base model managed 45% and lost the person repeatedly, so the gallery uses the accurate one). The Mirror page tracks, loses the person during the empty gap and finds them again on Mac Catalyst, the iOS simulator (recorded landmarks) and the Android emulator.

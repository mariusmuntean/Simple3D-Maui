# Project handoff

Updated: 2026-10-03. Fetch remote state before resuming.

## Current integration state

### Rendering integration and branch cleanup (2026-10-03)

PR #6 merged into `main` at `f11746c8d7f5f6f0c2e149ec70ded672a32d459c`. This checkpoint supersedes its earlier draft-only gates below. The owner reports good performance on a physical iPhone 17 Pro and confirms a full Mac trackpad pinch now zooms continuously and smoothly. Android interaction was previously confirmed by the owner. These are hands-on reports, not measured 60/120 FPS guarantees. The merged `main` was retested: Core 40/40 and MAUI 39/39 passed.

The final changes add optional native diagonal edge filtering (`IsAntialiasEnabled`, enabled by default), compile Android Debug rendering using JIT, and paint selection directly from the valid shape-ID buffer. Duplicate shape instances retain the same picking/outline semantics. Regression coverage checks filtered pixels, unchanged captures and picks, viewport edges, concurrent animated selection and pan, and detail recovery. Temporary timing logs and an ineffective image-drawing experiment were removed from source. Selection bitmap processing measured about 14.8 to 8.4 ms at 614×768 in matched iOS simulator runs, including edge filtering; this is CPU processing time, not displayed FPS.

Fresh local checks: Core 40/40, MAUI 39/39, scripts 13/13, and DocFX metadata/site builds with zero warnings/errors. Fresh iOS simulator and Mac Catalyst Debug builds passed with one existing local deprecated Xamarin settings warning each; Android Debug with embedded assemblies passed with zero warnings/errors. Linux ARM64 Docker checks passed Core 40/40 and MAUI 39/39 after installing fontconfig and DejaVu fonts. A minimal fontless image loaded Skia but failed two label layout checks; manual CI now explicitly installs those text prerequisites. No hosted run was dispatched. Linux ARM64 validation does not claim validation on an x64 GitHub runner. Independent code review found no blocking defects.

The earlier iPhone simulator installation predated the animated-selection fix. A fresh build retained the outlined output arrow during animation. The simulator test app was stopped; the owner's existing simulator session was preserved. The final clean build was installed there without launching it. The disposable Linux validation container removed itself. Unrelated development containers and worktrees were preserved.

Eight completed branches were deleted locally and remotely: `maturity-tests` (#1), `rider-apple-builds` (#2), `visual-identity` (#3), `gallery-scenes` (#4), `showcase-animations` (#5), `reusable-render-target` (#7), `native-smoke-cleanup` (#9), and `render-bitmap-reuse` (#6). Their exact remote heads were checked against the merged PRs before cleanup. Preserve `showcase-engineering-scenes` (#8), `package-readiness` (#10), and `showcase-workflows` (#11): they contain unmerged work. The packaging branch has a separate active worktree. The primary checkout is now on `main`.

For subsequent work, start from updated `main`, integrate each remaining PR additively, and repeat its scope-specific validation. Earlier checkpoints below are historical evidence, not instructions to keep the completed rendering PR in draft. Package publication and combined showcase validation remain separate work.

### Gallery interaction and animated selection checkpoint (2026-10-03)

The gallery scene now occupies a flexible grid row outside all scroll views. Catalogue buttons scroll horizontally, and the footer scrolls within its own bounded area. A regression reproduced the previous scrolling ancestor. The rebuilt Mac gallery visibly rotates on a vertical drag while its header and controls stay fixed.

`Scene.Replace` supplies `ShapeReplacementEventArgs` through the existing `Changed` event. `SceneView` follows the selected node into its replacement tree by child position, preserving the tint and contour during animation. Keep child order stable for logical parts; missing children and removed shapes clear selection. Selection notifications report the current instance. Regressions cover all seven animated showcases, duplicate names, missing children and visible outline pixels at their new positions. The rebuilt Mac output arrow visibly retained its outline while moving.

Local verification: Core 40/40; the isolated staged MAUI milestone 35/35; the working tree including the two earlier antialiasing tests 37/37. Android Debug build passed with zero warnings/errors. Mac Catalyst Debug build passed with the existing deprecated Xamarin settings warning. DocFX metadata and site builds passed with zero warnings/errors. iOS was not rebuilt or launched for this checkpoint. Antialiasing remains a separate local draft.

Android deployment caution: Rider fast deployment supplied old assemblies even after a successful build. The deployed MAUI assembly hash differed from both current build outputs, and the running app still logged removed `PAINT_PROFILE` instrumentation. Use an APK with embedded assemblies and remove the test installation before reinstalling when verifying new behavior; do not trust a successful deployment alone.

Rebuilt Android Debug with `-t:Rebuild -p:EmbedAssembliesIntoApk=true` (zero warnings/errors), reinstalled the disposable test app, and visibly verified vertical orbit with fixed page controls and the selected output arrow retaining its contour during animation. The Android test app and emulator were stopped afterward; the Mac test app was also stopped. A later Mac app launched by the owner was preserved. No hosted runs were dispatched. The earlier Android Debug `UseInterpreter=false` performance setting and antialiasing changes remain uncommitted local drafts, separate from this milestone.

### Rider mobile launch checkpoint (2026-10-03)

Android launch failed before deployment because Rider requires the source `samples/Simple3D.Demo/Platforms/Android/AndroidManifest.xml`, which was missing even though terminal builds generated a manifest. Added the minimal application manifest. Rider then built, deployed and launched the gallery using Debug on Pixel 5 / Android 13 (API 33, ARM64); the actual rendered Equipment scene was visibly verified in Running Devices.

The iPhone 17 / iOS 26.5 launch was reproduced: CoreSimulator's bridge stalled before returning an application process handle, then returned `NSPOSIXErrorDomain code 3` / `HE0042`. Restarting only the affected simulator recovered it without erasing data. Both a subsequent Rider Run and a second Rider Debug launch displayed the gallery, with native app processes confirmed. The user's iPhone 16e uses iOS 18.6, a separate runtime. This establishes launch using Debug, not breakpoint or Hot Reload behavior. Test apps and the test simulator/emulator were stopped afterward. No hosted runs were dispatched.

Local antialiasing changes in `SceneView.cs` and its source-linked tests remain unfinished drafts; they are excluded from this launch fix milestone and still need native image-quality and performance verification.

### Native rendering checkpoint (2026-10-03)

The rendering branch now uses a supported local Apple toolchain: .NET SDK 10.0.401, workload set 10.0.401.1, Apple packs 27.0.10722 and Xcode 27.0. Xcode version validation is enabled. The Apple Debug demo interprets its own assembly and compiles the rendering libraries and framework assemblies (`MtouchInterpreter=-all,Simple3D.Demo`). Clean and rebuild when changing workloads or interpreter settings. Rider's local toolset was updated to SDK 10.0.401. Its fresh solution build passed with no reported problems after a default demo restore repaired Android runtime targets overwritten by targeted Apple builds. A fresh Rider launch still needs verification.

A native ARM64 compilation defect produced invalid view coordinates when three `Vector3.Dot` calls appeared directly as constructor arguments. Portable tests did not reproduce it. Assigning the three vector fields separately fixes the observed native failure without changing the public API or camera math. The Debug-only `NativeRenderProbe` checks visible pixels, picking, owned/reusable equivalence and retained snapshots for all seven scenes inside the actual Apple runtime. Run `bash scripts/ios-simulator-smoke.sh --render-probe` after building the iOS bundle; a completion marker is required and the script cleans up its own session.

The final configuration passed all seven native scene probes on Mac Catalyst and the iPhone 17 Pro / iOS 26.5 simulator. At 512×315, native Core rendering measured 2.6–8.2 ms/frame across those scenes. Molecule fell from about 61 ms to 8.2 ms in the same simulator after compiling the rendering path. These are Core render costs, not displayed FPS or input latency. The ordinary iOS smoke check also displayed Equipment, Packing and Surface and stopped its app and simulator afterward. Leave the user's separate booted iPhone 17 session untouched.

Local portable checks passed Core 39/39, MAUI 30/30 and scripts 13/13. DocFX metadata and site builds passed with zero warnings/errors. Apple Debug builds passed with one warning about the deprecated user-level Xamarin Settings.plist. Zoom now uses a bounded preview throughout its gesture; linear bitmap sampling softens enlarged preview edges. Tests cover both touch and cumulative Mac pinch preview completion/cancellation and interpolated pixels. This does not establish full static-scene antialiasing or physically smooth gestures.

The rebuilt Mac gallery was also launched and visually checked after unlocking: clicking selected the motor with a visible contour, mouse orbit retained selection, and Telemetry animation changed bar heights. The app was stopped afterward. Rotation exposed overlapping world labels; the iOS Surface screenshot also exposes a label clipped at the right edge. Improve label layout.

Remaining gates: actual iOS touch/pinch and animation pacing; physical Mac trackpad pinch; fresh Rider launch of this configuration; full-resolution edge quality; Linux execution of the Skia dependency fix. Keep #6 draft until its native interaction gates are verified. No hosted checks were dispatched for this iteration.

### Label layout checkpoint

Native label painting now measures glyph bounds, keeps text inside the viewport, abbreviates oversized text at Unicode character boundaries and searches nearby rows to avoid overlap. It searches at most eight rows in either direction and omits labels that cannot fit. Core projected anchors and picking are unchanged. The placement list is reused between paints and cleared when render resources are released.

Two new regressions failed against the original placement, then passed with the fix: narrow viewport clipping (including insufficient height) and overlapping anchors. Local Core 39/39, MAUI 32/32, scripts 13/13 and DocFX site build passed. Both Apple Debug builds passed with the same single local deprecated-settings warning. The rebuilt Mac gallery visibly separated MOTOR and OUTPUT at the rotation that previously overlapped them and displayed the full Surface annotation; the app was stopped. The iOS three-scene smoke check passed and its Surface screenshot now shows the entire annotation inside the viewport. Its test simulator was shut down and the user's separate session remained untouched. This fixes label layout, not the remaining antialiasing or physical gesture gates.

Repository: `mariusmuntean/Simple3D-Maui` (private).

PR #9 merged into `main` at `2d6f5c97417e4ec928e1a46a0e887e5de75c74a9`. It stops iOS smoke apps and simulators on success, failure and interruption, selects an idle supported iPhone, routes Apple builds to Xcode 27, raises the demo's Mac Catalyst minimum to 17, and adopts the iOS scene lifecycle. Local Core 39/39, MAUI 13/13 and script tests 11/11 passed. Both Apple builds and the real local three-scene iOS smoke check passed. [All six hosted jobs passed](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37105153071), including iOS and Android app launches.

The remaining work is committed on these branches:

| Branch | PR | Scope and next gate |
| --- | --- | --- |
| `render-bitmap-reuse` | [#6](https://github.com/mariusmuntean/Simple3D-Maui/pull/6) | Native bitmap and render-target reuse, raster fallback/recovery. Updated from main at `8ecfb6f`; fix the Linux Skia native dependency failure, then verify physical Mac trackpad pinch and native interaction/frame pacing before merge. |
| `showcase-engineering-scenes` | [#8](https://github.com/mariusmuntean/Simple3D-Maui/pull/8) | Robot Arm, Orbit, Wind, renderer recovery/traversal improvements and Android smoke cleanup. Update from main and combine with #6 after #6 passes its gates. |
| `package-readiness` | [#10](https://github.com/mariusmuntean/Simple3D-Maui/pull/10) | Package metadata and isolated package consumers. Updated from main at `37793b3`; check hosted run `37107900337` before merging. |
| `showcase-workflows` | [#11](https://github.com/mariusmuntean/Simple3D-Maui/pull/11) | Conveyor Inspection, Solar Tracker, Packet Routing, Drone Survey. Stacked on #8; retarget to main after #8 merges and repeat combined validation. |

Preserve the existing PRs. All subsequent work should be additive. Keep each PR draft and unmerged while checks required for its scope remain incomplete.

## Transfer checkpoint (2026-10-03)

The owner requested committing and pushing all work and context to continue on another computer. Both working trees had no uncommitted source drafts or local-only branch commits before this handoff update. All implementation branches are on GitHub. Generated build outputs, restored packages, local IDE settings and tools remain ignored and reproducible; do not transfer them as source.

Start from `render-bitmap-reuse` and read this file first. The other current checkout is `package-readiness`. Branch heads before this documentation commit:

- `main`: `2d6f5c97417e4ec928e1a46a0e887e5de75c74a9` (PR #9 merged).
- `render-bitmap-reuse`: `8ecfb6f6f369edb8236108c8c6d011dbfffc9f0b`.
- `package-readiness`: `37793b3532dc40c4d30edac9c4d31823b1b8d807`.
- `showcase-engineering-scenes`: `8a3dd728ded298a8a7341dc399bf898a1ae5e08e`.
- `showcase-workflows`: `cc6f8cee7701d46300198dbcdfc31ee1f6f88714`.

Fresh local checks on the rendering branch: Core **39/39**, MAUI **23/23**, scripts **11/11**, all exit 0. Rider build session `9757d90f-3a82-467e-bd00-d2b34655bc83` completed successfully with no reported problems. These results do not establish that hosted Linux native dependencies work.

**Next concrete failure:** [rendering run 37107955413](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37107955413) has a failed `core` job. Core passed 39/39, but MAUI passed 19/23: four native paint tests failed loading `libSkiaSharp` on Linux (`DllNotFoundException` from `SKImageInfo`). Inspect the test project's native asset references and add the appropriate Linux runtime dependency; preserve the native paint tests. Android build/launch, Mac Catalyst build and documentation jobs passed; iOS was still running at this checkpoint. Recheck the exact head's run after a fix.

[Package run 37107900337](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37107900337) had five successful jobs and an iOS job still running at this checkpoint. It is not yet a confirmed all-green run. New documentation pushes create new head runs: inspect those before merging.

The latest Rider iOS run started an iPhone 17 / iOS 26.5 app, but reported an IDE socket refusal and an old lifecycle warning. The auto-generated iOS configuration has no Build-before-launch step; a stale Debug bundle remains a possibility, not a proven diagnosis. There are duplicate `Simple3D.Demo` names across Android, iOS and macOS configurations, so choosing by name is ambiguous. No portable `.run` configurations have been added yet. Next: save distinct repository-relative Apple configurations with a build step, verify Rider recognizes them, build fresh bundles, launch each and verify the actual UI. Do not copy machine-specific ignored workspace settings or assume debugging/Hot Reload works.

The active Rider run was stopped through Rider, its exact simulator was shut down, and process/device inspection found no remaining gallery or mlaunch process and no booted simulator. No testing sessions need to be resumed or cleaned up on this machine.

Suggested order: fix #6 Linux dependencies; complete its native checks; merge verified #10 independently; merge ready #6; update #8 from main and verify/merge; then retarget and verify #11. Fetch first and inspect current PR state, because CI or integration may have changed since this snapshot. Keep the existing PRs and preserve additive work. Publication is still a separate future milestone.

## Package checkpoint

On 2026-10-03, the updated package branch passed 14/14 script tests, including a generated Core package consumed from an isolated feed and cache with no project references. The consumer checks rendered pixels, picking and retained snapshots. Both packages packed successfully. A MAUI host using packages only compiled for iOS, Android and Mac Catalyst with zero warnings/errors; Core and SkiaSharp resolved transitively. This is compilation evidence, not a native launch of that host.

No package has been published. Publication still requires a license decision, package ownership and version checks, and a release validation pass. The README documents package-consumer commands.

## Gallery and performance checkpoint

Main has seven scenes. #8 adds three and #11 adds four, giving fourteen on `showcase-workflows`. Each scene animates a relevant subject and retains fixed anchors. Telemetry bars change height within their scale and shift color near range ends. The console examples and DocFX images use the shared scene factories.

The latest workflow branch passes Core 49/49, source-linked MAUI 13/13 and script tests 18/18. Its [hosted head run passed](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37091311778). The Mac gallery displayed all four workflow scenes and animated Drone Survey; picking selected a propeller. A local iOS 26.5 simulator displayed Drone Survey and the three-scene smoke check passed. Every tested app and simulator was stopped.

Core reusable rendering at 768×576 was measured at about 1.5 ms/frame for Conveyor and Solar, 2.6 ms for Packet Routing and 3.1 ms for Drone Survey on one local run. Drone managed allocation fell to about 4,122 bytes/frame after static geometry reuse; Solar fell to 2,912. Timings vary with machine load. These are Core measurements; native displayed FPS has not been measured.

PR #6 previously passed Core 39/39 and MAUI 23/23 and launched macOS/iOS from Rider. Its later traversal-budget regression also passed. This branch contains additional native paint tests that are not yet in #8 or #11. Do not treat their lower MAUI test counts as equivalent coverage.

## Remaining native validation

1. Physically verify continuous Mac trackpad pinch. Logical incremental-pinch tests pass, but automated mouse drag/click does not prove trackpad event delivery.
2. Verify iOS touch orbit, pinch, picking and animation pacing through a simulator GUI or device. A static screenshot does not prove these interactions.
3. Launch the combined gallery branch from Rider on macOS and iOS. Earlier #6 Rider launches do not validate the combined result; an IDE socket refusal means debugging/Hot Reload remains unproven.
4. After integration, run the portable suites, package consumers, relevant native builds and smoke checks again. Inspect the exact hosted run for the head being merged.

## Interaction checkpoint (2026-10-03)

Local rendering branch work now includes native Mac mouse pan handling, a 512-pixel drag preview, and visible selection feedback (mint tint plus an inner gold contour using depth picking). Materials and owned captures stay unchanged. Native Mac clicks, background deselection and camera orbit with a selected motor were visibly verified. Short drags preserve translation at the beginning and release. Local MAUI checks passed 28/28 and Mac Catalyst Debug built with zero warnings/errors. Temporary pan diagnostics were removed. The comparison benchmark reduced typical drag raster samples by about 75%; Molecule render/bitmap cost fell from 5.56 to around 1.6 ms/frame on this Mac, not displayed FPS.

The test project now references matching Skia Linux native assets; local output contains the Linux libraries. Linux execution has not been repeated, so do not claim its prior hosted failure is resolved. The user reports Mac rotation is much smoother, but iOS remains laggy, trackpad zoom still lags, and diagonal edges need antialiasing. Those are the active next tasks: bound zoom work throughout the gesture, investigate iOS input/render pacing, and improve presentation sampling/edge quality while measuring its cost locally. An iPhone 17 was booted again by the user after our cleanup; do not shut down that user session as if it were an abandoned test session.

## Hosted testing policy

The owner requested local development feedback and conserving Actions minutes (the supplied alert reported 1,827 of 2,000 included minutes used). Run local tests, native builds and Rider checks first. CI now uses only `workflow_dispatch` on the active rendering and package branches. Dispatch hosted checks later when the project is mature or a check specifically requires a GitHub runner. Do not rerun or dispatch checks on routine commits. Until this workflow policy reaches main and other branches, use `[skip ci]` on transfer-only pushes that would otherwise trigger hosted jobs. The two new checkpoint runs (`37108474550`, `37108476653`) were cancelled to avoid further resource use. Prior runs may already have completed; inspect their results only when needed.

## User requirements

- Work autonomously, make routine decisions, verify milestones, commit and push context, and merge verified work.
- Keep the library fast, clear and pleasant to use, supporting display-only and interactive scenes with meshes and lighting.
- Keep mouse orbit and trackpad zoom responsive. Aim for useful animation on mid-range devices without claiming unmeasured 60/120 FPS.
- Keep docs and executable examples current and grow the gallery with relevant scenes.
- Stop tested apps, simulators and emulators after use; leave unrelated sessions alone.
- Maintain simple, elegant, recognizable branding suitable for publication and LinkedIn promotion.
- Use neutral task-oriented branch, commit, PR, code and document names; do not add provenance markers.

## Resume on another computer

```bash
git clone git@github.com:mariusmuntean/Simple3D-Maui.git
cd Simple3D-Maui
git fetch origin
git switch main
```

Use a separate checkout or worktree for another branch. Read the live PR and branch state before editing. The original maturity branch and old handoff are historical.

Use a .NET 10 SDK with appropriate MAUI workloads. There is no committed `global.json`; check installed SDK/workload and Xcode versions. On the original Mac the workload-enabled SDK is under `$HOME/.dotnet`, while the Homebrew SDK lacks MAUI workloads. Use SDK 10.0.401 and workload set 10.0.401.1 with Xcode 27; the demo validates Xcode during builds. Hosted Apple jobs use the Xcode 27 image.

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release
dotnet run --project tests/Simple3D.Maui.Tests -c Release
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -r maccatalyst-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-ios -r iossimulator-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-android -c Debug
bash scripts/ios-simulator-smoke.sh
```

Run .NET builds sequentially when they share a checkout's outputs. Native smoke scripts require an already built, correctly signed bundle. DocFX commands and package validation are in the README.

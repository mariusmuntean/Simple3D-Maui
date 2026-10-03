# Project handoff

Updated: 2026-10-03. Fetch remote state before resuming.

## Current integration state

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
git switch --track origin/render-bitmap-reuse
```

Use a separate checkout or worktree for another branch. Read the live PR and branch state before editing. The original maturity branch and old handoff are historical.

Use a .NET 10 SDK with appropriate MAUI workloads. There is no committed `global.json`; check installed SDK/workload and Xcode versions. On the original Mac the workload-enabled SDK was under `$HOME/.dotnet`, while the Homebrew SDK lacked MAUI workloads. The demo's local `ValidateXcodeVersion=false` setting permits its tested local toolchain combination; it does not establish official support. Hosted Apple jobs use the Xcode 27 preview image.

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

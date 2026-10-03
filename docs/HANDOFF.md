# Project handoff

Updated: 2026-10-02. Validation below was performed on 2026-09-27 unless stated otherwise.

## Apple runner repair (2026-10-02)

GitHub billing has cleared enough for jobs to run. PR #11's first hosted run,
[36882715397](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/36882715397),
passed Core, documentation, Android build and Android emulator jobs. Its iOS
and Mac Catalyst jobs failed for a toolchain mismatch: unpinned workload
installation selected .NET 10 Apple packs requiring Xcode 27, while the
workflow selected Xcode 26.6. The Mac Catalyst pack also rejects the demo's
old 15.0 minimum and requires 17.0.

This branch now routes Apple jobs to the published `xcode-27` runner and selects
Xcode 27.0 explicitly; Android remains on `macos-26`. The demo's Mac Catalyst
minimum is 17.0. The iOS smoke script can choose an idle iPhone 17 Pro,
iPhone 18 Pro or iPhone 17, matching the devices on both local and Xcode 27
runners. It also contains the simulator cleanup from independent PR #9
(`2426e0b`) so this stacked branch terminates its app and shuts down the
device it boots. Merge #9 independently first if its gates pass; then reconcile
the shared script when updating #11 from `main`.

A new simulator fallback regression failed before the selector change. Local
script tests now pass **18/18**. Mac Catalyst Release and signed iOS Simulator
Debug builds passed with zero warnings/errors. The real iPhone 17 Pro / iOS
26.5 smoke script launched Equipment, Packing and Surface, passed screenshot
and scene-difference checks, then shut down the simulator; no demo or
simulator process remained.

The next hosted run,
[37004136654](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37004136654),
passed Core, documentation, Android build and emulator, and Mac Catalyst
Release build. Its iOS Simulator build passed with zero warnings/errors, but
the app crashed on launch under iOS 27: the crash report identified
`UIApplicationEvaluateRuntimeIssueForNoSceneLifecycleAdoption`. The iOS demo
now has a `MauiUISceneDelegate` and `UIApplicationSceneManifest`, matching the
existing Mac Catalyst scene configuration. The corrected iOS build passed
locally with zero warnings/errors. On iPhone 17 Pro / iOS 26.5, the real smoke
script displayed Equipment, Packing and Surface, passed pixel and scene
difference checks, and shut down its simulator. No demo process remained. A
third hosted run,
[37061200749](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/37061200749),
verified that repair: the iOS 27 build and simulator launch passed. Core,
documentation, Android build and Mac Catalyst build passed too. Its Android
emulator job failed while the runner action downloaded the emulator SDK package
(`Error on ZipFile unknown archive`), before the gallery smoke script ran. The
failed job was rerun on 2026-10-03. Attempt 2 passed: Android launched the
gallery and its screenshot checker found the expected background, panel and
blue shape. All six jobs are green for the final attempt. The `xcode-27`
runner is a preview image; inspect its actual selected Xcode and simulator
devices in future logs.

## Workflow gallery milestone (2026-10-01)

The `showcase-workflows` branch is based on `showcase-engineering-scenes` at
`8a3dd72`. It adds Conveyor Inspection, Solar Tracker, Packet Routing and Drone
Survey to the shared gallery and console scene factories, growing the gallery
from ten to fourteen scenes. Each animates a relevant moving subject while its
anchor stays fixed. Shapes have useful names for picking. The scene catalogue,
rendered documentation images, executable example check and benchmark include
all four. The implementation plan is in
[2026-09-30-showcase-workflows.md](superpowers/plans/2026-09-30-showcase-workflows.md).

The Core tests failed first for each missing scene. Image review exposed a
packet link floating above its rack; a regression failed before the route was
aligned with the device tops. The first drone animation allocated 94,720 bytes
per frame because it rebuilt arm geometry. It now reuses static body, arms and
rotor blades. Solar panel cells are also reused between frames. Those allocation
regressions failed before the changes and pass afterward.

Fresh local verification: Core **49/49**, source-linked MAUI **13/13**, script
tests **11/11**, and DocFX metadata/site generation with zero warnings or
errors. Debug demo builds for Mac Catalyst arm64, iOS Simulator arm64 and Android
succeeded with zero warnings/errors. The four generated PNGs were inspected.
Core reusable-target benchmarks at 768×576 over 60 animated frames were about
1.5 ms/frame for Conveyor and Solar, 2.5 ms for Packet Routing, and 3.1 ms for
Drone Survey on this Mac. Drone allocation fell from 90,716 to 4,122 managed
bytes/frame; Solar fell from 8,400 to 2,912. These are Core benchmarks, not
native displayed frame-rate measurements.

The Mac Catalyst gallery visibly displayed all four scenes. Drone animation
moved its body and propellers; a click selected `Propeller 4`. The app was
closed. The iOS app launched with Drone Survey selected on an iPhone 17 Pro /
iOS 26.5 simulator; its [captured frame](validation/ios-drone-survey.png) passed
the gallery pixel check. The app was terminated and the simulator shut down.
No demo, simulator or emulator process remained after validation. This was an
iOS static launch check, not a touch or frame-pacing measurement, and this run
did not validate launching the new branch from Rider.

This branch is [draft PR #11](https://github.com/mariusmuntean/Simple3D-Maui/pull/11),
based on `showcase-engineering-scenes`. Keep the independent PRs #6, #9 and #10 intact.
Once #8 merges, retarget this branch to `main` and revalidate the combined
result. The first hosted run and its Apple failures are described above.
Remaining physical/native interaction checks are still required before merging.

## Portable checkpoint (2026-09-30)

This is a historical checkpoint. The planned gallery scenes are implemented
on PR #11, described above.

All source changes and handoff context are committed on the branches listed below.
Both local checkouts were clean at the start of this checkpoint. The local
`showcase-engineering-scenes` checkout was fast-forwarded to its already pushed
quality pass. An empty local `showcase-workflows` branch was removed; it had no
changes, PR or remote branch to preserve. Start future work from a fresh branch
at the current `origin/showcase-engineering-scenes` or the appropriate integrated
`main`, and fetch before editing on another computer.

The next proposed additive gallery work is **not implemented**: Conveyor
Inspection (packages travel through a scan gate and change status), Solar
Tracker (panels follow a moving sun), Packet Routing (packets traverse fixed
network links), and Drone Survey (a drone moves over a pad with spinning
propellers). Keep fixed scene anchors and moving subjects named and pickable.
Add fail-first scene/animation/reset tests, script coverage, rendered DocFX
images and catalogue text. Inspect the Mac gallery, measure Core render cost,
build Apple/Android targets, and stop every launched app or simulator. This is
a design queue, not a verified feature or a reason to merge the existing PRs.

Actions run [36635101338](https://github.com/mariusmuntean/Simple3D-Maui/actions/runs/36635101338)
for PR #8 failed before any job step on 2026-09-29. Its check annotation says
recent account payments failed or the spending limit must be increased. No
GitHub runner result, native frame-rate result or physical trackpad-pinch result
can be inferred from that run.

## Latest quality pass (2026-09-29)

The owner requested resuming from the newest branch. Work continues on
`showcase-engineering-scenes`, whose preceding head was `e4096a9` (2026-09-29
15:27 UTC). Preserve the independent work in #6, #9 and #10; this pass does not
merge those PRs or claim their remaining native checks are complete.

- `RenderInto` now clears reusable picking and labels before argument validation.
  A null scene/camera or invalid background can no longer leave an old object
  selectable after a failed render. A regression failed before the fix and
  verifies successful recovery afterward.
- Scene traversal now uses an ancestor continuation stack instead of recursive
  iterator objects for every node. Depth-first ordering, hierarchical transforms
  and the node budget remain unchanged. A regression compares pixels and picks
  against an equivalent flat scene and limits extra group allocations. It failed
  before the change with 138,240 extra bytes per frame for 128 wrapped leaves.
  A separate branching regression hand-derives noncommuting scale/translation
  transforms and verifies pixels, picking identity and first-leaf equal-depth ties.
- Android smoke tests stop the gallery on success, failure, INT and TERM. A failed
  start also triggers cleanup; original failure statuses are preserved, and a
  failed cleanup turns success into failure. The emulator runner still owns
  device shutdown. Seven shell-boundary regressions cover these paths; six failed
  against the previous script.

Fresh Linux verification with SDK 10.0.401: Core **43/43**, source-linked MAUI
**13/13**, Python/script checks **11/11**; Core Release build has zero warnings
and errors. Android shell syntax and whitespace checks pass. These are portable
checks, not native app launches. Native platform builds and displayed frame
pacing were not rerun in this environment.

On the same Linux runner, the reusable Orbit scene at 768x576 fell from 23,960 to
9,176 managed bytes per frame (about 62% less); Surface fell from 1,552 to 1,296.
Renderer timings varied between runs, so no displayed-FPS or timing improvement
is asserted. The existing native paint allocation changes in #6 remain separate.

The quality-pass Actions run, `36635101338`, failed all six jobs with empty
step lists. Its Core check annotation identifies account billing/spending limits
as the startup blocker; no hosted test result can be inferred from these failures.

## Resume here

Repository: `mariusmuntean/Simple3D-Maui` (private).

Four independent draft PRs are open against `main`, with a fifth draft PR stacked on #8. They contain committed, pushed implementation with no unfinished source drafts.

| Branch | PR | Purpose |
| --- | --- | --- |
| `render-bitmap-reuse` | [#6](https://github.com/mariusmuntean/Simple3D-Maui/pull/6) | Reuse native painting buffers and bitmap; bounded raster fallback/recovery |
| `showcase-engineering-scenes` | [#8](https://github.com/mariusmuntean/Simple3D-Maui/pull/8) | Add Robot Arm, Orbit and Wind; harden render recovery, reduce traversal allocation, clean Android smoke sessions |
| `native-smoke-cleanup` | [#9](https://github.com/mariusmuntean/Simple3D-Maui/pull/9) | Stop iOS smoke sessions on success, failure and interruption |
| `package-readiness` | [#10](https://github.com/mariusmuntean/Simple3D-Maui/pull/10) | Real package metadata and isolated package-consumer checks |
| `showcase-workflows` | [#11](https://github.com/mariusmuntean/Simple3D-Maui/pull/11) | Four workflow scenes, purposeful animation, rendered examples and Apple runner repairs; based on #8 |

The handoff commit follows each implementation commit. Start with #6. After its remaining checks pass, merge it, update #8 from the new `main`, verify the combined result, then merge #8. Preserve the existing PRs.

PR #9 is independent and can be integrated separately after review and runner verification. Its native cleanup check has passed locally.

PR #10 is also independent. Package consumers passed locally, including compilation against all three MAUI targets. Its package-test projects intentionally stay outside the regular solution: pack first, then provide a local feed. Recheck all consumers after combining the branches.

On another computer:

```bash
git clone git@github.com:mariusmuntean/Simple3D-Maui.git
cd Simple3D-Maui
git fetch origin
git switch --track origin/render-bitmap-reuse
```

To inspect #8 or #11 separately, use another checkout or worktree for
`origin/showcase-engineering-scenes` or `origin/showcase-workflows`. Do not
overwrite another branch's work. PR #11 is stacked on #8, so compare it with
#8's branch until #8 has merged.

## User requirements and decisions

- Work autonomously; make routine implementation decisions.
- Verify milestones, commit and push them, then merge when ready. Keep PRs draft and unmerged while native validation is incomplete.
- Subsequent changes should be additive. Do not discard these PRs or replace the library unnecessarily.
- The demos must build and run from Rider on macOS and iOS, and work on GitHub runners.
- Support both displaying a scene without interaction and interactive scenes, including meshes and lighting.
- Keep mouse orbit and trackpad pinch responsive. A previous Mac pinch handled only one step; continuous zoom still needs a physical gesture check.
- Aim for useful animation on mid-range devices. The demo uses a 60 Hz timer; no native 60/120 FPS guarantee has been established.
- Each showcase should animate its own subject. Telemetry bars change height within their scale and shift color near the range ends.
- Stop tested apps, simulators and emulators after use. Do not leave accumulated instances running. Do not stop unrelated user processes.
- Branding should be simple, elegant and recognizable, suitable for a published library and LinkedIn promotion.
- Never include AI provenance names or identifiers in branch names, commits, PR text, code, documentation or generated artifacts.

## Already integrated into main

The latest observed remote `main` is `42fdbe09a8ecad61019f27818f2a39278f47354c`.

Earlier merged work includes the depth renderer, indexed meshes, hierarchy, lighting, picking, MAUI control, gallery, executable examples, hostable documentation, native build configuration, display-only interaction mode, purposeful animations, branding and reusable Core rendering targets (#7). The original maturity branch and its older handoff are no longer the starting point.

Important source locations:

- `src/Simple3D.Core/DepthRenderer.cs`: depth rasterization, picking and reusable target.
- `src/Simple3D.Maui/SceneView.cs`: cached/native painting, gestures, selection, render resolution policy.
- `samples/Simple3D.Shared/DemoScenes.cs`: shared scenes and scene-specific animation.
- `samples/Simple3D.Demo/GalleryPage.cs`: gallery controls and elapsed-time animation loop.
- `tests/Simple3D.Core.Tests` and `tests/Simple3D.Maui.Tests`: executable regression runners.
- `samples/Simple3D.Examples`: executable console image examples.
- `docs/site`: DocFX documentation and source images.
- `assets/brand`, demo `Resources/AppIcon` and `Resources/Splash`: committed visual identity.
- `.github/workflows/ci.yml`: builds, tests, documentation and native smoke checks.

## Verification evidence and limits

### PR #6

- Most recent local regression run (2026-09-28): Core **39/39**, MAUI **23/23**, exit code 0.
- Earlier script tests: **4/4**; DocFX metadata/site build had zero warnings/errors.
- Earlier Debug demo builds: Mac Catalyst, iOS Simulator arm64 and Android, zero warnings/errors.
- Rider solution build succeeded.
- Mac Catalyst launched from Rider. Equipment, Telemetry and Surface rendered. Telemetry bars changed height/color during Animate; the Surface sample marker moved. Mouse orbit, object selection, Reset and + zoom responded.
- iOS built and launched from Rider on iPhone 17 / iOS 26.5. A simulator capture showed Equipment rendered: [captured frame](validation/ios-rider-pr6.png).
- Native paint test measured **1,552 managed bytes/frame** for animated Surface at 768×576. This is an allocation benchmark, not displayed FPS.
- Regression coverage includes reusable bitmap/target pixels and picking, owned snapshots, disconnect cleanup, resolution fallback/recovery, and suppression of repeated failed full-resolution probes.
- The 2026-09-28 audit found that MAUI fallback geometry counting could traverse more than Core's node budget before rendering. It now rejects after 100,000 node visits, including repeated empty groups. A new regression failed before the fix and passed afterward. The Mac Catalyst Debug build was repeated with zero warnings/errors.

### PR #8

- Most recent local regression run: Core **40/40**, MAUI **13/13**, exit code 0. The lower MAUI count is expected: #8 does not yet contain #6's additional tests.
- Earlier script tests: **4/4**; DocFX metadata/site build had zero warnings/errors.
- Earlier Debug demo builds: Mac Catalyst, iOS Simulator arm64 and Android, zero warnings/errors.
- Orbit visibly rendered in an iPhone 17 Pro simulator.
- Mac app launched from its built bundle. Robot Arm, Orbit and Wind rendered and changed during Animate. Picking returned Forearm, Planet and Blade 1.
- Latest Core reusable-target benchmark for the three new scenes was about **1.2–1.6 ms/frame** at 768×576 over 60 frames on this Mac. This is not native displayed FPS.
- A review found a picking-label issue on orbit segments; it was fixed and covered by a regression test.

All app processes used in the final native pass exited; all simulators were shut down.

### PR #9 (2026-09-28)

- Six executable shell regressions cover cleanup on success, boot/install failure and termination, avoiding an already running user simulator, and reporting shutdown failure. Tests failed against the previous behavior before the fixes.
- All **10/10** script tests passed. Bash syntax and whitespace checks passed.
- A fresh iOS Simulator Debug build succeeded with zero warnings/errors.
- The real iPhone 17 Pro / iOS 26.5 smoke pass rendered Equipment, Packing and Surface and passed screenshot/scene-difference checks. The new exit cleanup stopped the gallery and shut down the device; no simulator was booted and no gallery process remained.
- GitHub billing blockage was rechecked on 2026-09-28; jobs still fail before starting.

### Remaining checks before merge

1. Physically verify continuous Mac trackpad pinch. The available automation generated mouse drag/click, not a pinch gesture. The logical incremental-pinch regression passes, but that alone does not verify native delivery.
2. Observe iOS touch interaction and animation pacing on a simulator with a GUI or a device. The previous installation had simulator runtimes and command-line tools but no Simulator application window.
3. GitHub jobs started again on 2026-10-01. Recheck #6, #8, #9 and #10 after carrying the Apple toolchain repair from #11 into their integration base; their older billing-blocked runs provide no evidence about source correctness. PR #11's first real run exposed the Apple mismatch described above.
4. After merging #6 and updating #8 from main, repeat portable tests and relevant builds for the combined code.

### PR #10 (2026-09-29)

- Both packages previously contained the placeholder description "Package Description" and lacked a repository URL. Descriptions, author, repository/project URLs and tags are now configured. A package-metadata test failed before the fix and passed afterward.
- All **7/7** script tests on this branch passed, including three package checks; Core **39/39** and MAUI **13/13** source tests passed. This branch is based on main and lacks #6's additional regression tests.
- Core's public API ran from a generated package in a fresh cache with a local-only feed. Rendering, picking and owned snapshot retention were checked without project references. XML docs and icon inclusion were also checked.
- Both libraries packed successfully. A MAUI host fixture using packages only compiled for iOS, Android and Mac Catalyst with zero warnings/errors. This was compilation, not a native app launch.
- The initial minimal MAUI host had an MA002 warning about a missing direct Controls reference. The committed fixture retains that normal host reference and builds without warnings; Core and SkiaSharp still resolve transitively from the MAUI package.
- DocFX metadata/site generation passed with zero warnings/errors. CI includes package-consumer checks, but runner execution is still an external gate.
- No package was published. Publication still needs a license decision, version/package ownership checks, successful runner validation and remaining native interaction checks.

Rider iOS Run launched and rendered, but its console reported an IDE socket connection refusal. Do not claim debugging/Hot Reload was validated.

## Reproduce verification

Use a .NET 10 installation with appropriate MAUI workloads. There is no committed `global.json`; recheck installed SDK and workload versions on the new machine.

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release
dotnet run --project tests/Simple3D.Maui.Tests -c Release
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet run --project samples/Simple3D.Examples -c Release -- --all output
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -r maccatalyst-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-ios -r iossimulator-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-android -c Debug
dotnet tool install docfx --tool-path .tools --version 2.81.0
.tools/docfx metadata docs/site/docfx.json --warningsAsErrors
.tools/docfx build docs/site/docfx.json --warningsAsErrors
```

The regression projects are executable runners, so use `dotnet run`, not merely `dotnet test`.

For Rider, select the .NET CLI containing the workloads under Settings → Build, Execution, Deployment → Toolset and Build, with its .NET SDK MSBuild. On the previous Mac this was `/Users/marius/.dotnet/dotnet`, SDK 10.0.301; do not assume the same path on another machine.

DocFX also launches `dotnet restore` from the shell's `PATH`. Ensure the workload-equipped SDK is first in `PATH` and set `DOTNET_ROOT` to its installation; Rider's SDK selection alone does not configure DocFX. The 2026-09-28 default-shell metadata attempt selected the Homebrew SDK without workloads and failed; metadata and site generation then passed with zero warnings/errors using the user-local SDK.

The previous Mac had Xcode 27.0 and Apple workload 26.5.10301, which expects Xcode 26.6. The demo currently sets `ValidateXcodeVersion=false` for Apple targets. That permitted local development, but does not establish official toolchain compatibility. The original CI selected Xcode 26.6, but later unpinned workload installation selected packs requiring Xcode 27. PR #11 now selects the Xcode 27 runner for Apple jobs; verify its hosted result rather than generalizing local results.

For native tests, track the precise app/device you launch. Terminate the app, stop the Rider Run session, and shut down that simulator/emulator in a cleanup block even on failure. PR #9 adds that cleanup to the iOS smoke script, including INT/TERM handling. Until it is merged, use its script or provide equivalent cleanup locally. Android smoke app/device cleanup remains to be audited separately.

`SIMPLE3D_GALLERY_SCENE` chooses the initial gallery scene. For simulator launch use `SIMCTL_CHILD_SIMPLE3D_GALLERY_SCENE`.

## Repository state and portability

Both implementation branches were clean before this handoff. Generated `bin`, `obj`, `_site`, API metadata, local tools, artifacts and IDE user settings are ignored and reproducible. The selected iOS screenshot was copied into tracked documentation for portable evidence. No credentials or machine-specific IDE configuration are needed in source control.

The documentation site builds to `_site`; it has not been publicly deployed. CI uploads it as an artifact. Package publishing and public promotion have not been performed.

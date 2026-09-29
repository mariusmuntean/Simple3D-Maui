# Project handoff

Updated: 2026-09-29. Validation below was performed on 2026-09-27 unless stated otherwise.

## Resume here

Repository: `mariusmuntean/Simple3D-Maui` (private).

Four independent draft PRs are open against `main`. They contain committed, pushed implementation with no unfinished source drafts.

| Branch | PR | Implementation commit | Purpose |
| --- | --- | --- | --- |
| `render-bitmap-reuse` | [#6](https://github.com/mariusmuntean/Simple3D-Maui/pull/6) | `758e1eb309ed9709af3299d8df1025728df341a1` | Reuse native painting buffers and bitmap; bounded raster fallback/recovery |
| `showcase-engineering-scenes` | [#8](https://github.com/mariusmuntean/Simple3D-Maui/pull/8) | `12bcb761613ac8ebe1f67b0508987a93f21d809c` | Add Robot Arm, Orbit and Wind, with relevant animation and documentation images |
| `native-smoke-cleanup` | [#9](https://github.com/mariusmuntean/Simple3D-Maui/pull/9) | `2426e0bf0d07531b84e9571c26e15c25e6682e2f` | Stop iOS smoke sessions on success, failure and interruption |
| `package-readiness` | [#10](https://github.com/mariusmuntean/Simple3D-Maui/pull/10) | `abc0fed161d968028db0fbd988e24b30b76ed7c2` | Real package metadata and isolated package-consumer checks |

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

To inspect #8 separately, use a second checkout or a worktree for `origin/showcase-engineering-scenes`. Do not overwrite either branch's work.

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
3. Resolve GitHub account billing/spending-limit block, then rerun Actions on both branches. Jobs fail before any steps start with: “The job was not started because recent account payments have failed or your spending limit needs to be increased.” These failures provide no evidence about source correctness.
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

The previous Mac had Xcode 27.0 and Apple workload 26.5.10301, which expects Xcode 26.6. The demo currently sets `ValidateXcodeVersion=false` for Apple targets. That permitted local development, but does not establish official toolchain compatibility. CI selects Xcode 26.6; once jobs can start, verify the actual runner toolchain rather than generalizing local results.

For native tests, track the precise app/device you launch. Terminate the app, stop the Rider Run session, and shut down that simulator/emulator in a cleanup block even on failure. PR #9 adds that cleanup to the iOS smoke script, including INT/TERM handling. Until it is merged, use its script or provide equivalent cleanup locally. Android smoke app/device cleanup remains to be audited separately.

`SIMPLE3D_GALLERY_SCENE` chooses the initial gallery scene. For simulator launch use `SIMCTL_CHILD_SIMPLE3D_GALLERY_SCENE`.

## Repository state and portability

Both implementation branches were clean before this handoff. Generated `bin`, `obj`, `_site`, API metadata, local tools, artifacts and IDE user settings are ignored and reproducible. The selected iOS screenshot was copied into tracked documentation for portable evidence. No credentials or machine-specific IDE configuration are needed in source control.

The documentation site builds to `_site`; it has not been publicly deployed. CI uploads it as an artifact. Package publishing and public promotion have not been performed.

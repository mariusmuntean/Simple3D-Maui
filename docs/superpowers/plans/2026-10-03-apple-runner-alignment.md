# Apple runner alignment plan

**Goal:** Make the independent iOS smoke cleanup pass current hosted Apple builds and a real iOS 27 simulator launch.

**Evidence:** Run 37004136654 built the iOS app but crashed at launch with `UIApplicationEvaluateRuntimeIssueForNoSceneLifecycleAdoption`. Run 37061200749 passed after adding the iOS scene delegate and manifest. Its Android emulator job passed on retry after a corrupt SDK archive download.

## Changes

- [x] Bring the proven Xcode 27 Apple job routing and Mac Catalyst 17 minimum into this branch, keeping Android on macOS 26.
- [x] Add the iOS scene delegate and manifest, and extend the smoke selector to idle iPhone 17 Pro, 18 Pro or 17 devices.
- [x] Update README requirements and smoke instructions.
- [x] Run script regressions, Core and MAUI regressions, Apple builds, and the real iOS simulator smoke check; verify the simulator is shut down.
- [ ] Commit and push; inspect the hosted run and update the PR with observed results.

Local verification on 2026-10-03: Core 39/39, MAUI 13/13, script tests 11/11. Mac Catalyst Release and signed iOS Simulator Debug builds had zero warnings/errors. The iPhone 17 Pro / iOS 26.5 smoke check rendered Equipment, Packing and Surface with distinct screenshot content; no demo process or booted simulator remained afterward.

The branch remains focused on native demo reliability. Keep unrelated renderer and gallery changes in their existing PRs.

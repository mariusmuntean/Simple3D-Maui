# Workflow Gallery Scenes Implementation Plan

**Goal:** Add four distinct, useful 3D workflow examples to the gallery, console examples and documentation.

**Architecture:** Keep using `DemoScene` factories in `samples/Simple3D.Shared/DemoScenes.cs`. Each scene has static, named, pickable anchors and one bounded time-based animated subject. The gallery and console runner already consume `DemoScenes.All`; no renderer or gallery API change is needed.

**Tech stack:** .NET 10, `Simple3D.Core`, MAUI gallery, executable Core regression runner, Python example tests, DocFX.

**Spec:** [portable checkpoint](../../HANDOFF.md#portable-checkpoint-2026-09-30).

## Constraints

- Use neutral names in branches, commits and artifacts.
- Preserve the existing ten scenes and their behavior.
- Keep animation deterministic from elapsed time; `Animate(0)` restores the initial image.
- Keep scene geometry cheap enough for the existing software renderer and a 60 Hz demo timer.
- Stop native apps, simulators and emulators after validation.

## Tasks

### 1. Conveyor Inspection

- [x] Add a failing test that expects the named scene, a fixed scan gate, moving package, inspection status/color, and exact reset.
- [x] Run the Core runner and confirm the expected failure.
- [x] Implement `DemoScenes.ConveyorInspection()` and include it in `All`.
- [x] Run the Core runner and confirm green.

### 2. Solar Tracker

- [x] Add a failing test for a fixed mount, moving sun and changing panel orientation, with reset.
- [x] Run the Core runner and confirm the expected failure.
- [x] Implement `DemoScenes.SolarTracker()` and include it in `All`.
- [x] Run the Core runner and confirm green.

### 3. Packet Routing

- [x] Add a failing test for fixed named links/switches and packets moving along the links, with reset.
- [x] Run the Core runner and confirm the expected failure.
- [x] Implement `DemoScenes.PacketRouting()` and include it in `All`.
- [x] Run the Core runner and confirm green.

### 4. Drone Survey

- [x] Add a failing test for a fixed pad, moving drone and rotating named propellers, with reset.
- [x] Run the Core runner and confirm the expected failure.
- [x] Implement `DemoScenes.DroneSurvey()` and include it in `All`.
- [x] Run the Core runner and confirm green.

### 5. Delivery and review

- [x] Extend the executable example test and benchmark to cover all four scenes.
- [x] Update README, getting started and example catalogue; generate and inspect the four PNGs.
- [x] Run Core, MAUI and script suites, DocFX and native target builds; inspect the Mac gallery if available.
- [x] Review the diff, record precise evidence/limits in `docs/HANDOFF.md`, commit and push the branch, and open a draft PR.

## Review focus

- Pickable moving parts remain the current scene shapes after replacement.
- Moving shapes stay inside the original fitted camera view throughout the animation.
- Every frame is determined by time, including reset after an arbitrary animation sequence.
- Static geometry does not shift while the subject moves.
- The scene remains readable at the gallery's smaller viewport sizes.

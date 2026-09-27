# Simple3D-Maui handoff for Codex

## Goal and user preferences

Build a new private .NET 10 MAUI library for simple, useful 3D drawings on iOS, Android and macOS via Mac Catalyst. API should feel intuitive, with custom indexed meshes, grouping, camera control, labels, depth-aware picking and a compelling demo. Prioritize simplicity, performance, strong tests, excellent XML and hostable documentation from getting started through complex scenes. Work autonomously, track progress, commit each verified milestone. The user asked to use the GitHub plugin and, most recently, GPT-6 Sol. Do not ask for routine choices. A solution file was explicitly requested and delivered.

## Repository and exact state (2026-09-27 10:01 Europe/Berlin)

Private repo: https://github.com/mariusmuntean/Simple3D-Maui
Draft PR: https://github.com/mariusmuntean/Simple3D-Maui/pull/1
Work branch: `maturity-tests`; latest verified Core milestone commit `78ce35f33e17e6465b9f91c996f54ceca6f78e72` (the handoff commit will be newer).
Default branch `main` has not been merged with this work. Keep draft until native validation is real.
Local checkout: `/workspace/scratch/849b540b7525/repo`; local HEAD `f4d9956` represents the same Core milestone with reconstructed local history, not matching GitHub SHA. GitHub plugin commits have the exact remote ancestry.
`Simple3D.sln` is committed and pushed, grouping Core, Maui, Demo and the two existing executable test runners. README explains opening it and choosing Demo startup. The planned Examples runner exists only as an untracked draft and has not been added to the solution.

## Committed feature baseline

Core milestone pushed: immutable indexed `Mesh` and `Material`, `Shape.FromMesh`, named shapes, groups/hierarchical transforms, line/arrow geometry, world labels, observable `Scene` and `Camera`, camera perspective/orthographic/target/fit, world bounds, a bounded CPU `DepthRenderer` with per-pixel opaque visibility and accurate picking, owned `RenderFrame` pixels, XML docs with warnings as errors. Existing primitive fluent APIs remain. The old `SceneRenderer` is public and remains legacy. The current MAUI view **still uses the legacy renderer**, so new Core features are not yet integrated in the app.

Before a new uncommitted red test, `dotnet run --project tests/Simple3D.Core.Tests -c Release` passed **31/31**; the tests include 46,080 independent ray-oracle pixel comparisons. Core Release build had zero warnings/errors. Core renderer benchmark on this Linux runner for 12 spheres/3456 triangles at 400×300 was ~3.7–4.2 ms/frame and ~967 KB allocated/frame, mainly owned pixel and pick-ID arrays. This excludes Skia/native presentation and is not a device FPS promise.

Current old demo also built successfully to a signed Android arm64 Debug APK with zero warnings/errors using local Android SDK and JDK, **before** the new Skia view. No native runtime test was run. Local Android Release reached linker but MSBuild task host failed with MSB4216; avoid presenting that as a source compile failure. iOS and Mac Catalyst require macOS/Xcode.

## Review findings and current failing test

Independent Core review (recorded in the previous workspace as `core-review.md`) identified three important issues:
1. `Scene.GetBounds()` lacks cumulative triangle budget, potentially traversing ~100 billion repeated triangles despite render's 1m triangle cap. `Camera.FitToScene` calls it. An uncommitted test was added to `tests/Simple3D.Core.Tests/CoreRegressionTests.cs` and currently fails as intended: **31/32** (`bounds and fit reject excessive repeated mesh triangles`). Implement aggregate preflight, atomic fit, test green.
2. `DepthRenderer` can perform billions of raster samples with overlapping full-screen triangles at 2048² even under the triangle cap. Add a practical per-frame clipped bounding-box sample-work budget with a clear exception and regression. Document this separate from normal scene budgets.
3. Public legacy `SceneRenderer.Render` silently ignores new camera target/orthographic/near-plane/group/material behavior. Give unsupported feature uses a caller-visible runtime guard or explicit diagnostic while preserving old primitive/default-camera behavior. Switch the MAUI control to `DepthRenderer`.
Two minor points: subpixel area threshold edge and line diameter before subsequent parent scaling. Review actual source before choosing fixes.

The GPT-6 Sol fix agent hit a model usage limit before making implementation changes. Its sole uncommitted change is the red bounds test. Do not mistake the dirty workspace for a passing branch. No other agents are currently implementing code.

## Pending milestones

1. Fix the three Core review findings and tests; verify full Core suite/build; commit and push through the GitHub plugin.
2. Replace `GraphicsView` with a SkiaSharp `SKCanvasView` using `DepthRenderer`/BGRA frame, bindable per-instance Scene and Camera, scene/camera subscription lifecycle, invalidation/frame cache, gestures, selection/picking coordinate mapping, labels, MAUI Color convenience and startup registration. Test the actual source-linked control with Skia native Linux assets. Make a real interactive equipment/packing/procedural mesh demo, using Core scene factories shared with examples. Preserve dark palette and adapt screenshot scripts. The previous local brief was `maui-brief.md`; the following requirements here are authoritative if the old workspace is unavailable.
3. Finish and verify hostable DocFX site, README, XML API reference and executable examples, rendered genuine geometry illustrations, link and browser QA, CI docs job. Partial uncommitted drafts exist. The previous local brief was `docs-brief.md`; reconstruct its tasks from this handoff and the committed plan if unavailable.
4. Independent final review, tests and package/native checks, update PR body. GitHub Actions jobs have been blocked before checkout by an account billing/spending restriction; no need to alter billing. Leave draft/unmerged if native tests remain unverified. The previous branch's earlier iOS platform builds/screenshots passed, but the new features require new validation.

## Moving the uncommitted work

Clone/check out `maturity-tests` in the new Codex project. It includes all completed commits including `Simple3D.sln` and the Core milestone. The current uncommitted drafts and red regression are in `simple3d-wip-handoff.tar.gz`, an optional attachment from the preceding ChatGPT conversation, **not tracked in the repository**. If attached to the new session, extract it at the repository root. Otherwise, recreate the single red bounds regression and implement the guides from the requirements below. Never assume a fresh clone includes the draft files. These drafts are not yet compiled or verified, and examples link a `DemoScenes.cs` that the pending demo milestone must create. The plan already committed to GitHub is `docs/superpowers/plans/2026-09-27-useful-3d.md`. The previous Core API handoff and verification notes were local-only files named `core-handoff.md` and `core-report.md`. The essential API signatures and behavior are present in the committed source and XML documentation; this file contains the tested results and outstanding findings.

Local environment, if preserved: SDK `/workspace/scratch/849b540b7525/dotnet/dotnet` (10.0.401), JDK `/workspace/scratch/849b540b7525/jdk`, Android SDK `/workspace/scratch/849b540b7525/android-sdk`, DocFX `/workspace/scratch/849b540b7525/tools/docfx` (2.81.0), Python Playwright 1.51 headless Chromium available. `dotnet build ... -m:1 -p:BuildInParallel=false` avoided nested MSBuild failure. On another machine install its own toolchain.

## Public API integration notes

`DepthRenderer.Render(scene,camera,width,height,background)` returns owned `RenderFrame` with `Width`, `Height`, `ReadOnlyMemory<uint> Pixels` (opaque ARGB packed, little-endian BGRA bytes), projected `Labels` and `Pick(int x,int y)` returning the visible leaf `Shape?`. It reuses private depth scratch; the renderer is not thread-safe. Frame pixels and picks remain valid after another render. Dimensions 1..2048. `Scene.Changed` and `Camera.Changed` are synchronous; subscribe/detach on bound-property replacement and handler lifecycle. `Shape.FromMesh` shares immutable indexed geometry; `Group`, `Named`, `Line`, `Arrow`, `WithMaterial` and `WorldLabel` are documented in XML. `Camera.FitToScene(scene,aspectRatio,padding)` and `CameraProjection.Orthographic` provide fit and parallel views. Never mutate a bound scene from a background thread.

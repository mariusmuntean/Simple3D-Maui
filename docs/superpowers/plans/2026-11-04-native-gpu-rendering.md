# Native GPU Rendering Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add selectable native GPU rendering, sample controls, docs and runnable validation.
**Architecture:** Shared scene preparation with Metal and GLES3 adapters behind a composed SceneView. Software capture remains portable and explicit.
**Tech Stack:** .NET 10, MAUI, MetalKit, Android EGL/GLES3, existing SkiaSharp overlays.
**Spec:** ../specs/2026-11-04-native-gpu-design.md

## Global Constraints
- Preserve Android, iOS and Mac Catalyst; Core remains independent of MAUI/Skia.
- Auto may fall back; Hardware never silently falls back.
- No per-frame GPU-to-CPU pixel copy, no browser runtime.
- Native CI remains manual; preserve snapshot ownership and scene budgets.

## Review Focus
- Switching while drawing and disconnect/reconnect must release native resources.
- Failed hardware initialization must produce a diagnostic and honor mode.
- Nested nonuniform transforms, backfaces and near clipping must preserve picking.
- Replaced immutable shapes must reuse geometry and transfer selection.
- Performance checks must identify the renderer and never mislabel CPU capture as GPU work.

## Tasks
- [x] Add shared GPU geometry, projection and ray picking tests; implement cacheable geometry and frame preparation.
- [x] Add rendering mode policy tests; compose SceneView with software/overlay and native surface views.
- [x] Implement GLES3 and Metal renderers with cached buffers/textures, depth and native lifecycle cleanup.
- [x] Adapt gallery, native correctness/performance probes and manual CI artifact collection.
- [x] Update setup/API/architecture/limits docs; run available suites, review and open draft PR.

## Execution ledger
- Working in a dedicated fresh clone on feat/native-gpu-rendering; no user work is present.
- Native platform SDKs are unavailable initially; bootstrap .NET for portable checks, report native limitations explicitly.

- Validation: portable Core/MAUI suites and 24 Python script tests pass. Headless GLES tests pass on llvmpipe, including five gallery image comparisons, cached-upload assertions and completed API performance measurements.
- C# compilation against Android and iOS platform reference assemblies passes with warnings treated as errors, including native probe sources. This is not a native application build.
- Native Android SDK dependency installation failed in this environment; no Apple runtime is available. Actual native builds, Metal shader execution, MAUI embedding and physical-device behavior remain PR merge gates.
- Review fixes: near-only label clipping, guarded overlay/picking preparation failures, asynchronous Metal command errors, inclusive CPU preparation timing and aspect-preserving tall-view picking.
- Ruling: GLES3 retained for the initial Android backend after discussing Vulkan tradeoffs; document Vulkan as a future option, with no speedup claim without physical-device evidence.

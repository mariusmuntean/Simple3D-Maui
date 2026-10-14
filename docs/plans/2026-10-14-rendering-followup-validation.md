# Rendering follow-up validation

Validated locally on 14 October 2026 with .NET SDK 10.0.401, workload set 10.0.401.1, macOS 27, and Xcode 27. No packages, documentation, or branch changes were published.

## Changes

Image decoding writes directly into the final texture buffer, pins it only during native decoding, and converts RGBA to ARGB in place. An internal ownership transfer retains this private buffer; the public constructor continues to copy and validate caller pixels. Public enumerable construction allocates its exact final buffer and inspects at most one excess pixel instead of growing intermediate buffers. Maximum dimensions remain 4096 × 4096; the required final pixel storage is 64 MiB at that limit. The former 64 MiB decoded bitmap and 64 MiB staging/copy buffer are removed. Codec scratch space and encoded stream storage are additional and were not measured as peak process memory.

Attributed rasterization retains the original double-precision reciprocal depth for perspective interpolation; depth testing still uses the same rounded float. Solid rendering is unchanged. Surface now offers prepared smooth/flat geometry and left/right directional light comparisons, with reset and selection preserved. Equipment shows a larger imported 3D controller with a mapped display.

## Bounded performance evidence

The opt-in shared fixture in `tests/Shared/AppearancePerformanceProbe.cs` uses two spheres, 576 triangles, a fixed perspective camera, and a reusable 400 × 300 target. Each workload warms up for 100 frames, then reports the median of five rounds of 100 frames and managed bytes per frame. Textures are identical 64 × 64 images sampled with repeat addressing. The smooth workload uses calculated normals and vertex colors. Measurements exclude native presentation and do not represent displayed FPS. The before implementation is commit `4862eae`; both runs use the same new fixture. Loader measurement uses the same opaque 1024 × 1024 PNG after three warmup loads; its timing is a single bounded sample.

| Runtime / workload | Before CPU ms | After CPU ms | Managed bytes/frame before / after |
| --- | ---: | ---: | ---: |
| Host Release, nearest | 0.758 | 0.821 | 776 / 776 |
| Host Release, bilinear | 1.068 | 1.093 | 776 / 776 |
| Host Release, smooth vertex colors | 0.657 | 0.692 | 776 / 776 |
| Mac Catalyst Release, nearest | 2.979 | 2.910 | 816 / 816 |
| Mac Catalyst Release, bilinear | 3.841 | 3.890 | 816 / 816 |
| Mac Catalyst Release, smooth vertex colors | 2.436 | 2.522 | 816 / 816 |

Raster timings vary in both directions; no general speedup is established. All before/after hashes agree in both runtimes:

- Nearest: `57A40E7D61F5AABA21307848AEC609BED41B730C3065035F8FA8482FEE65682A`
- Bilinear: `52F136C1C2F1A246FD30365DB662D69871C67CAB8B4C991A0C8960758483741B`
- Smooth vertex colors: `72EEB283184A6623F6067683E77ED1701DA7699E6DD7DECCA400ED6D66A8EC74`

The host image load changes from 5.032 ms / 8,390,600 managed bytes to 5.480 ms / 4,196,624 bytes. Native Mac changes from 11.837 ms / 8,390,656 bytes to 9.176 ms / 4,196,240 bytes. Both also remove a separate 4 MiB native decoded bitmap. The load hash remains `010BB3D33B0E8E0D2678182B95367584C5B4F152A84A88559C42C55EBF5A7ABF`. These results establish reduced allocation on this fixture, not a universal loading-speed improvement.

Reproduce current host evidence with `dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --appearance-performance`; native Mac evidence with `SIMPLE3D_APPEARANCE_PERFORMANCE=1 bash scripts/mac-release-smoke.sh`. The performance fixture is opt-in and does not slow the default tests. For a before comparison, use the same harness in an isolated checkout and substitute the three original implementation files (`Texture2D.cs`, `DepthRenderer.cs`, `TextureLoader.cs`) from `4862eae`. Raw outputs are retained under `artifacts/rendering-validation/appearance-{host,native}-{before,after}.log`.

## Final checks

- Core Release: **80/80**, including allocation bounds for public enumerable construction, defensive array/list copies, pixel/picking/clipping oracles, and the output arrow's fixed animation anchor. MAUI host Release: **57/57** after a forced fresh build with **0 warnings, 0 errors**, including decoded allocation bounds, immutable exposure, failure/retry streams, visible shading/light changes, reset, selection, and comparisons during animation.
- Python documentation/example/smoke helpers: **22 passed**. DocFX metadata and site build: **0 warnings, 0 errors**. Equipment and Surface documentation images were regenerated and inspected; the five gallery scenes remain.
- Native Mac Catalyst Release: `APPEARANCE_PROBE_PASS` (976 covered pixels), all gallery render/pick/snapshot checks, and `NATIVE_RENDER_PROBE_PASS`. After the late accessible-label/control-order adjustment, the incremental Release app build also passed. Apple builds emitted the existing Xcode settings deprecation warning; runtime background-fetch diagnostics did not indicate rendering failure.
- Core and three-platform MAUI local Release packages built. Fresh isolated NuGet-only Core runtime checks passed (`PACKAGE_CORE_PASS`, `PACKAGE_APPEARANCE_PASS`); a fresh Mac Catalyst package consumer built and passed `PACKAGE_MAUI_PASS`, including the packaged decoder/internal Core ownership boundary, textured primitive, native control, pixels, picking, selection and host gestures. Runtime consumer checks use the no-argument mode. An initial full release-metadata invocation rejected local source paths from the non-CI pack; CI symbol normalization and remote Source Link checksum checks remain release checks for this unpublished branch. The consumer's initial macOS temporary-path alias failure was resolved by cleaning its own build output and rebuilding through the physical `/private/tmp` path.
- Android Debug build passed before the final accessibility adjustment. Installation on the existing arm64 emulator was blocked by `INSTALL_FAILED_INSUFFICIENT_STORAGE`; its contents were preserved. Android runtime rendering and appearance performance therefore remain unverified. No iOS simulator/device execution was performed.

- Live Mac Catalyst gallery inspection confirmed that the shading and light controls visibly change Surface, announce their current state to accessibility, and that the enlarged imported controller is readable in Equipment. Screenshots are retained under `artifacts/demo-appearance`.
- After the final wording change (the light control now describes world-space ray direction: `IsLightToRight`/`SetLightToRight`), Core Release **80/80**, MAUI host Release **57/57**, Python helpers **22 passed**, the Mac Catalyst Release app build (0 errors; the existing Xcode settings warning) and the Android Debug build (0 warnings, 0 errors) were re-run and passed.

Task-started applications and the isolated consumer/temporary comparison files were cleaned after retaining evidence under `artifacts/rendering-validation`.

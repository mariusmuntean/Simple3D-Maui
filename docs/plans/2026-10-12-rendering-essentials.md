# Rendering essentials: research, design, and implementation plan

## Goal and constraints

Extend the portable CPU renderer with five useful capabilities while keeping existing calls and default rendering compatible. Work on `feature/rendering-essentials` in the main worktree. Core stays dependency-free. Keep immutable, copied assets; synchronous scene notifications; existing node, triangle, and raster budgets; deterministic fast tests. No publication or merge is part of this change.

## Research and priority (12 October 2026)

This is a suitability ranking, not a measured community vote. Three.js and Helix Toolkit provide broader GPU rendering feature sets; their APIs inform expectations, not performance promises for this CPU renderer.

1. **Texture mapping**: reusable images add detail without geometry. [Three.js Lambert materials](https://threejs.org/docs/pages/MeshLambertMaterial.html) expose texture maps.
2. **Configurable directional lighting**: a world-space direction, color, intensity, and ambient brightness make the existing fixed lighting useful across scenes. [DirectionalLight](https://threejs.org/docs/pages/DirectionalLight.html).
3. **Smooth shading**: supplied or calculated normals improve curved models without subdivision. [BufferGeometry normals](https://threejs.org/docs/pages/BufferGeometry.html) and [community OBJ smoothing question](https://discourse.threejs.org/t/how-to-smooth-an-obj-with-threejs/3950).
4. **OBJ mesh import**: lets existing modeling tools feed the renderer. [Three.js OBJLoader](https://threejs.org/docs/pages/OBJLoader.html). Support a clearly bounded geometry subset, not external materials or network/file resolution.
5. **Vertex colors**: low-memory gradients and heat maps; [community vertex-color use](https://forum.babylonjs.com/t/manipulate-color-of-specific-vertices-with-shader/16124).

[Helix Toolkit](https://github.com/helix-toolkit/helix-toolkit) confirms materials, lighting, and import as common .NET 3D concerns. Existing cameras, groups, picking, and unlit materials need no duplicate features. Defer transparency (ordering and depth semantics), shadows (additional rendering passes), PBR (pixel cost), and glTF (large asset/material/animation scope).

## Design

- `Texture2D`: immutable copied opaque ARGB pixels; maximum 4096 per dimension; nearest or bilinear filtering; clamp or repeat addressing. UV origin bottom-left, pixel storage top-left. Reject invalid sizes, enums, alpha, and nonfinite UVs. No implicit image IO in Core. MAUI gets a stream decoder using its existing Skia dependency.
- `Mesh`: optional per-vertex texture coordinates, normals, opaque colors. Preserve the two-argument constructor. Attribute counts equal vertex count. `WithCalculatedNormals()` returns an area-weighted smooth-normal copy; shared indices define smoothing, split vertices define hard edges/seams. Validate and normalize normals at asset creation.
- `Material`: keep the current constructor and default flat opaque look. An additional texture constructor defaults tint to white. Normals, UVs, and vertex colors are used when present. Applying a texture to geometry without UVs throws at shape creation/material assignment instead of silently ignoring it. Cached primitives receive UV geometry while retaining their original flat appearance.
- `DirectionalLight`: immutable normalized world-space ray direction, opaque color, nonnegative intensity bounded to 1. Scene has a nullable `Light` and `AmbientLight` in [0,1], with change notifications. Defaults reproduce the existing .38 ambient/.62 directional white lighting. Unlit materials bypass lighting.
- Rasterizer: preserve flat-color fast path. Clip view positions and attributes together at the near plane; perspective-correct UV/color/Gouraud-light interpolation (ordinary barycentric interpolation for orthographic). Use inverse-transpose normal transforms, with backface normal flipping matching existing two-sided behavior. Shade vertices once per triangle; no per-pixel normal normalization or heap allocation. Apply texture, tint, vertex color, and lighting multiplicatively, clamp final RGB. Depth and picking remain opaque.
- `ObjReader.Read(TextReader)` / `Parse(string)`: bounded input, invariant numbers, triangle and convex planar polygon faces, positive/negative independent position/UV/normal indices, tuple deduplication preserving seams. Mixed attribute presence, malformed indices, nonfinite data, concave/nonplanar faces, and unsupported geometry records fail with line diagnostics. Ignore named groups/material references and smoothing directives; preserve explicit normals and offer calculated normals explicitly. Never load referenced files. Bound source size, line size, vertices, and generated triangles.

## Review focus

1. Near-plane clipping must interpolate all attributes without UV discontinuities.
2. Nonuniform group transforms must not skew smooth lighting.
3. Texture boundaries, negative repeats, seams, and extreme finite coordinates must be deterministic.
4. OBJ independent/negative indices, mixed attributes, and oversized/malformed inputs must fail predictably.
5. Existing constructor signatures, default pixels, selection, notifications, and warmed allocations must remain compatible.

## Implementation sequence

- [x] Record baseline Core/MAUI tests and performance; add failing attribute/light/texture contract tests, then implement immutable assets and scene settings.
- [x] Add failing pixel tests for texture orientation, perspective/orthographic interpolation, clipping, vertex colors, transformed smooth lighting, and depth/picking; implement raster integration and primitive UVs.
- [x] Add failing OBJ import tests for valid seams/indices and invalid/limited input; implement bounded reader.
- [x] Add MAUI image decoding and notification tests; update existing Equipment and Surface samples and documentation together. Retain five gallery entries.
- [x] Run Core/MAUI regressions, allocation/performance checks, docs/example checks, package consumer, native build and render evidence. Review whole diff, fix findings, and leave a verified branch.

## Validation record

Validated on 13 October 2026 with .NET SDK 10.0.401, workload set 10.0.401.1, and Xcode 27.

- Core Release: **78/78**; MAUI host Release: **50/50**. Core includes a ray-intersection pixel oracle for texture/color/light interpolation under perspective, orthographic, and near-plane clipping; retained picking and allocation checks still pass. MAUI checks opaque image decoding and automatic cache invalidation after light/ambient changes.
- Added red/green regressions for repeat-V seam orientation, exact constant texels, and singular composed normal transforms. The latter now uses the surviving world-space face normal when no inverse exists. Review also corrected primitive geometry documentation and removed a redundant attributed-pixel branch.
- Python documentation/example/smoke helpers: **22 tests passed**. DocFX metadata and site build: **0 warnings, 0 errors**. Equipment and Surface images regenerated and visually inspected; five gallery entries retained.
- Mac Catalyst **Release** clean build and native probe passed. Native evidence covers decoded PNG texture, imported UVs/normals, clipped vertex colors, pixels/picking/retained snapshots, and all five scenes; `APPEARANCE_PROBE_PASS` (976 covered pixels) and `NATIVE_RENDER_PROBE_PASS` were present. Probe process exited and its cleanup trap completed. Existing Xcode settings deprecation/runtime background-fetch diagnostics do not indicate rendering failure.
- Android **Debug** demo build: **0 warnings, 0 errors**. Core and MAUI Release packages built; MAUI package contains Android, iOS, and Mac Catalyst assemblies, each with XML documentation and portable symbols. No iOS simulator/device execution was performed in this validation.
- A fresh isolated NuGet-only Core consumer passed existing pixels/picking/snapshot checks and all five new APIs. A fresh Mac Catalyst Release package consumer built and passed `PACKAGE_MAUI_PASS`: `TextureLoader`, textured primitives, native control, pixels, picking, selection, and host gestures. Its initial temporary-path alias failure was resolved by using the physical macOS path and clearing only the consumer output; its final build had one existing Xcode settings deprecation warning and zero errors. Package verification uses an isolated cache and local feed; remote Source Link checksum verification is excluded because this branch is unpublished. No packages or documentation were published.

The same-workload comparison uses original Core source at `a2d9747` and the final tree: 12 spheres (3,456 triangles), `RenderInto`, 400 × 300, 500 warmup frames, then the median of seven rounds of 200 frames. Baseline **2.649 ms/frame**, final **2.449 ms/frame**; both **856 managed bytes/frame** and identical SHA-256 pixels:

`870514273EB27AA2CF08E8D7EF4ECCB46C08D6013ABAB9F09F853C37ECE5DBAA`

This is one local CPU workload, not a device frame-rate or general speedup claim. Attribute interpolation adds visible-pixel work; separate solid-material rasterization preserves the existing workload and work budgets. Existing trailing benchmark output uses owned snapshots and a different warmup and is not the paired comparison. Temporary benchmark/package-consumer checkouts and task-started processes are removed after retaining this record.

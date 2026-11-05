# GPU skinning implementation plan

Goal: pose changes upload one joint palette per item without CPU skinning or vertex uploads in rendering.

Architecture: retain the rigid 16-float vertex; append float4 joint indices and float4 weights for a 24-float skinned vertex. Cache bind geometry by SkinnedMesh identity. Use conservative posed bounds and carry the immutable pose palette on GpuItem. Metal uses separate direct/mask pipelines and bounded reusable buffers for large palettes; GLES uses separate direct/mask programs with an 8 KiB uniform block.

Requirements: the task specification and `../specs/2026-11-05-mirror-design.md`. Preserve picking culling, selection, textures, lighting, labels, statistics, resource eviction and failure policy. Maximum 128 joints and four influences. No public API expansion required.

## Tasks

- [ ] Core: add regression tests for identity across poses, bind expansion and attributes, palettes, posed bounds, budgets and posed picking; demonstrate failure, then update GpuScene/GpuGeometry and run Core tests.
- [ ] GLES: add attributes 4/5 and std140 palette, two skinning programs, reused UBO and correct cleanup; preserve clip-depth conversion. Verify with native Android build/probe.
- [ ] Metal: share vertex shading, add skinned vertex and two pipelines; reuse synchronized palette buffers beyond the inline constant limit. Verify with iOS and Catalyst builds/probes.
- [ ] Shared validation: compare rest/bent scenes with software and direct/composite, check upload counters across pose changes, exercise maximum palettes and measure warmed animated frames.
- [ ] Review, document skinning, run complete portable suites and all native probes; clean up task-started processes and commit on the current branch.

Review focus: matrix packing; palette reuse while GPU work is in flight; differently posed instances sharing a mesh; last palette joint and mixed weights; selection and backface culling after bending.

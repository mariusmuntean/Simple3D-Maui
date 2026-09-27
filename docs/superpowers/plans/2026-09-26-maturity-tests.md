# Maturity Tests Implementation Plan

**Goal:** Make the first release resilient to geometry edge cases and verify real gallery rendering automatically.

**Approach:** Keep the dependency-free core test executable. Add deterministic geometry invariants and a near-plane regression, then implement clipping only after the regression fails. Extend the existing iOS screenshot smoke to cover all three gallery scenes. Run the four-job GitHub Actions workflow after each milestone.

## Milestone 1: Geometry properties and near-plane clipping

- Add internal mesh access for tests, assert nondegenerate/outward built-in triangles and deterministic rendering over seeded transforms/cameras.
- Test a front-facing triangle with one vertex crossing the near plane; verify the old renderer fails.
- Clip against the near plane in camera space and triangulate the resulting polygon; preserve back-face culling, depth ordering, finite projection, and opaque color.
- Validate the complete core suite and commit.

## Milestone 2: Gallery scene coverage

- Select a gallery scene at startup from a simulator-only environment value, retaining normal default behavior.
- Launch each scene in iOS Simulator, capture a screenshot, and check background/panel/shape colors and drawing differences between scenes.
- Upload all screenshots, inspect them, validate all four CI jobs, and commit.

## Milestone 3: Cross-platform runtime and packaging

- Pack both library projects during CI to catch package metadata and multi-target build failures.
- Install and launch the Android demo in an emulator, capture a screenshot, and validate gallery pixels.
- Inspect simulator artifacts and update usage and limitations in the README.

## Risk focus

- Clip edges exactly on the near plane without NaN or infinities.
- Preserve winding and shading when one or two vertices lie behind the plane.
- Avoid flaky wall-clock performance assertions; keep a reported baseline.
- Keep simulator checks resistant to the home screen and splash screen.
- Keep all platform builds working with the new core logic.

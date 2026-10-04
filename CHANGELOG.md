# Changelog

## Unreleased preview · 0.1.0-preview.1

- Portable scene hierarchy, immutable indexed meshes, perspective and orthographic cameras.
- CPU depth rendering with visible-leaf picking, owned snapshots and reusable targets.
- MAUI control for Android, iOS and Mac Catalyst with orbit, zoom, selection feedback and labels.
- `SceneView.ClearSelection()` clears feedback from application commands; gallery Reset also clears the selected part.
- Toggling `IsInteractive` preserves gestures supplied by the host application.
- Seventeen gallery scenes with subject-specific animations, including colored checker cells, battery charge gauges and a gantry crane.
- Gallery animation pauses when its window enters the background and ignores queued ticks after stopping.
- Bounded raster work, reduced gesture preview resolution and optional diagonal-edge filtering.
- Indexed triangle traversal reduces allocation when rendering many small mesh instances.
- Reusable node preparation reduces per-frame allocation without sharing picking snapshots.
- MIT licensing, public API documentation, architecture diagrams and independent package consumers.
- Portable symbols, Source Link and SDK package validation for preview packages.

Packages are not yet published to NuGet.org. See [packaging](docs/PACKAGING.md) for installation from a local feed. Rendering supports opaque geometry and fixed directional lighting; image textures, transparency and shadows are outside this preview.

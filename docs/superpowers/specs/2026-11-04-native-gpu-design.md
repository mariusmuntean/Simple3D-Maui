# Native GPU rendering

Approved direction: native hardware rendering alongside the portable software renderer, for Android, iOS and Mac Catalyst. No WebView or JavaScript runtime.

## Contract

`SceneView.RenderingMode` selects Auto (prefer GPU, observable software fallback), Software, or Hardware (GPU required, observable failure without silent software rendering). Expose the active backend and diagnostic. Changing mode retains scene, camera and selection. The gallery offers the same choices.

Keep Core independent of native UI and graphics dependencies. Use immutable geometry caches, per-instance transforms, native depth buffers and direct surface presentation. CaptureFrame retains its explicit owned software snapshot contract; normal hardware painting never reads pixels back. Picking must respect nearest visible geometry and culling. Labels remain overlays. GPU output may differ slightly in raster edges and floating-point rounding.

## Approach

Use the existing .NET platform bindings: Metal on Apple and OpenGL ES 3 on Android. This avoids shipping a browser or third-party native engine. A shared preparation layer owns transforms, geometry and limits. Native resources are scoped to the surface/context and released on disconnect/context destruction. Shaders transform and shade geometry; buffers/textures are reused across animation.

Alternatives considered: WebGL/WebView (rejected by user in favor of native), and a third-party graphics abstraction (additional native distribution and binding burden for three MAUI targets).

## Validation

Portable tests cover geometry preparation, projection, picking, mode transitions and failure policy. Native tests must verify actual shader compilation, rendering and backend availability; CPU-only tests never establish GPU correctness or speed. Performance reports distinguish CPU submission from GPU completion and displayed FPS. Expensive native CI stays manually triggered. No NuGet publication or merging is authorized.

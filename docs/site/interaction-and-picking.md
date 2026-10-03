# Interaction and picking

`SceneView` is an `SKCanvasView` backed by the CPU `DepthRenderer`. Native painting and picking reuse a `RenderTarget` and Skia bitmap until their dimensions change. The target is rendered again when the bound scene, camera, background, or viewport changes. It contains packed opaque ARGB pixels, projected labels, and shape IDs for depth-aware picking. Explicit captures return owned `RenderFrame` snapshots.

The control installs drag, pinch, and tap gestures. On Mac Catalyst, native UIKit pan and pinch recognizers handle mouse and trackpad updates without MAUI touch-count gating. Drag orbits, each pinch event applies its incremental zoom factor, and tap selects the visible leaf shape. Projected label text scales with display density. The selected leaf gets a mint tint and a gold contour inside its visible boundary. Hidden pixels remain hidden; scene materials and exported frames are unchanged. Tap the background to clear the highlight. Handle the event to show details:

```csharp
view.SelectionChanged += (_, shape) =>
    details.Text = shape?.Name ?? "Nothing selected";
```

Native label painting keeps glyphs inside the viewport and abbreviates text with an ellipsis when it cannot fit the available width. Overlapping labels move to a free nearby row, searching up to eight rows in either direction; labels without space are omitted. Earlier scene labels take priority. These adjustments affect the overlay only; Core projected anchors and picking remain unchanged. Labels still overlay geometry without depth testing.

`SelectAt(x, y, viewWidth, viewHeight)` performs the same selection and feedback for a custom pointer. `PickAt` only queries; it does not change selection. Selection refers to an immutable shape instance, so replacing that instance in an animation removes its highlight until it is selected again.

While dragging or pinching, native rendering uses a preview capped at 512 pixels on its longest side, keeping the aspect ratio and depth picking. Scaled previews use linear sampling to soften magnified pixel steps. This filters the rendered bitmap; it is not multisample triangle rasterization. On release or cancellation the view restores its configured `MaximumRenderDimension`. Animation may independently use a lower configured limit.

For custom pointers, `view.PickAt(x, y, viewWidth, viewHeight)` maps layout coordinates to the last rendered frame. `CaptureFrame(width, height)` produces or retrieves an owned physical-pixel frame; `frame.Pick(x, y)` accepts physical pixels. `CaptureFrame` is useful for tests and exports. The renderer is not thread safe: use it and mutate its scene or camera on the same thread.

The view subscribes to its current scene and camera and releases those subscriptions when its handler disconnects. Replacing either bindable property stops observing the old object. When a tap follows a scene change before the next paint, picking refreshes the frame first.

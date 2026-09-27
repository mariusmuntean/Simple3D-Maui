# Interaction and picking

`SceneView` is an `SKCanvasView` backed by the CPU `DepthRenderer`. It keeps one owned `RenderFrame` per view and reuses it until a bound scene, camera, background, or viewport size changes. The frame contains packed opaque ARGB pixels, projected labels, and shape IDs for depth-aware picking.

The control installs drag, pinch, and tap gestures. Drag orbits, each pinch event applies its incremental zoom factor, and tap selects the visible leaf shape. Handle the event to show details:

```csharp
view.SelectionChanged += (_, shape) =>
    details.Text = shape?.Name ?? "Nothing selected";
```

For custom pointers, `view.PickAt(x, y, viewWidth, viewHeight)` maps layout coordinates to the last rendered frame. `CaptureFrame(width, height)` produces or retrieves an owned physical-pixel frame; `frame.Pick(x, y)` accepts physical pixels. `CaptureFrame` is useful for tests and exports. The renderer is not thread safe: use it and mutate its scene or camera on the same thread.

The view subscribes to its current scene and camera and releases those subscriptions when its handler disconnects. Replacing either bindable property stops observing the old object. When a tap follows a scene change before the next paint, picking refreshes the frame first.

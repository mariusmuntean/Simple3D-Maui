#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Metal;
using MetalKit;
using UIKit;
namespace Simple3D.Maui;

internal sealed class AppleGpuView : MTKView
{
    private readonly SceneView _owner;
    private readonly DrawDelegate _drawDelegate;
    private readonly NSObject _activeObserver;
    private bool _released;
    internal AppleGpuView(SceneView owner) : base(CGRect.Empty, MTLDevice.SystemDefault)
    {
        _owner = owner;
        Paused = true; EnableSetNeedsDisplay = true; ColorPixelFormat = MTLPixelFormat.BGRA8Unorm;
        // Never below the screen's rate: frames are drawn on demand, as fast as the app's changes arrive.
        PreferredFramesPerSecond = UIScreen.MainScreen.MaximumFramesPerSecond;
        DepthStencilPixelFormat = MTLPixelFormat.Invalid;
        FramebufferOnly = true; UserInteractionEnabled = false; Opaque = true;
        _drawDelegate = new DrawDelegate(this); Delegate = _drawDelegate;
        // Frames requested while backgrounded are skipped; draw the current state on return.
        _activeObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => RequestRender());
        // The renderer is created on the first draw, not here: this constructor runs inside
        // handler creation, where reporting a failure would re-enter the owner mid-connect.
    }
    internal MetalRenderer? Renderer { get; private set; }
    internal void RequestRender() { if (!_released && _owner.CanRenderHardware) SetNeedsDisplay(); }
    private void Render()
    {
        _owner.DrawingHardware = true;
        try { RenderFrame(); }
        finally { _owner.DrawingHardware = false; }
    }
    private void RenderFrame()
    {
        if (_released || !_owner.CanRenderHardware || DrawableSize.Width < 1 || DrawableSize.Height < 1) return;
        // iOS rejects GPU work from background apps (MTLCommandBufferError.NotPermitted).
        if (UIApplication.SharedApplication.ApplicationState == UIApplicationState.Background) return;
        var presented = false;
        try
        {
            var renderer = Renderer ??= new MetalRenderer(Device ?? throw new PlatformNotSupportedException("Metal is unavailable."), OnCommandFailed);
            var size = SceneView.RenderSize((int)DrawableSize.Width, (int)DrawableSize.Height, _owner.MaximumRenderDimension);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var frame = _owner.PrepareHardwareFrame(size.Width, size.Height, Bounds.Width > 0 ? size.Width / Bounds.Width : 1);
            if (frame is null) return; // Scene content skipped; the owner reported why.
            // Keep the Metal frame and the UIKit overlay (labels, first-frame placeholder) in one transaction.
            var withTransaction = _owner.PresentWithOverlay;
            if (PresentsWithTransaction != withTransaction) PresentsWithTransaction = withTransaction;
            using var drawable = CurrentDrawable;
            if (drawable is null) return; // Window may be hidden/backgrounded. Retry on next invalidation.
            renderer.Render(frame, size.Width, size.Height, drawable.Texture, drawable, presentWithTransaction: withTransaction);
            _owner.HardwareFrameSubmitted(renderer.Name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, size.Width, size.Height);
            presented = true;
        }
        catch (Exception ex) { _owner.ReportHardwareFailure(ex); }
        if (presented) _owner.RaiseFrameSubmitted();
    }
    // Completion handlers run on a Metal thread.
    private void OnCommandFailed(nint code, string message) =>
        _owner.Dispatcher.Dispatch(() =>
        {
            if (_released) return;
            // A submit that raced the app moving to the background is not a device failure:
            // keep hardware rendering and redraw once active again.
            if (IsTransientCommandFailure(code)) RequestRender();
            else _owner.ReportHardwareFailure(new InvalidOperationException(message));
        });
    internal static bool IsTransientCommandFailure(nint code) => code == (nint)(long)MTLCommandBufferError.NotPermitted;
#if DEBUG || NATIVE_RENDER_PROBE
    internal void DebugSimulateCommandFailure(MTLCommandBufferError code) => OnCommandFailed((nint)(long)code, "Simulated Metal command failure " + code + ".");
#endif
    internal void Release()
    {
        if (_released) return;
        _released = true;
        NSNotificationCenter.DefaultCenter.RemoveObserver(_activeObserver);
        Delegate = null; _drawDelegate.Dispose(); Renderer?.Dispose(); Renderer = null; ReleaseDrawables();
    }
    private sealed class DrawDelegate(AppleGpuView owner) : MTKViewDelegate
    {
        public override void Draw(MTKView view) => owner.Render();
        public override void DrawableSizeWillChange(MTKView view, CGSize size) => owner.RequestRender();
    }
}
#endif

using System.Diagnostics;
#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
using UIKit;
#elif ANDROID
using Android.Views;
#endif

namespace Simple3D.Maui;

/// <summary>Ticks once per display refresh on the UI thread, at the screen's real rate (60, 120 Hz or more), for
/// smooth animation. Uses <c>CADisplayLink</c> on iOS and Mac Catalyst and <c>Choreographer</c> on Android; elsewhere
/// a timer at most <see cref="MaximumRate"/> times per second.</summary>
/// <remarks>iPhones run apps at 60 Hz unless the app's Info.plist sets <c>CADisableMinimumFrameDurationOnPhone</c>
/// to true. Stop the clock while nothing animates: a running clock keeps the display at its highest rate.</remarks>
public sealed class FrameClock : IDisposable
{
    /// <summary>Upper bound when the display rate is unknown or reported higher.</summary>
    public const double MaximumRate = 240;
    private long _started, _last;
    private bool _running;
#if IOS || MACCATALYST
    private CADisplayLink? _link;
#elif ANDROID
    private Callback? _callback;
#else
    private Microsoft.Maui.Dispatching.IDispatcherTimer? _timer;
#endif

    /// <summary>Raised once per displayed frame with the time since <see cref="Start"/> and since the previous tick.</summary>
    public event Action<TimeSpan, TimeSpan>? Tick;

    /// <summary>The display's refresh rate in frames per second, capped at <see cref="MaximumRate"/>.</summary>
    public double RefreshRate
    {
        get
        {
#if IOS || MACCATALYST
            var rate = (double)UIScreen.MainScreen.MaximumFramesPerSecond;
#elif ANDROID
            var activity = Platform.CurrentActivity;
#pragma warning disable CA1422 // WindowManager.DefaultDisplay: the replacement, Context.Display, needs API 30.
            var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? activity?.Display : activity?.WindowManager?.DefaultDisplay;
#pragma warning restore CA1422
            var rate = (double)(display?.RefreshRate ?? 0);
#else
            var rate = 0d;
#endif
            return rate > 0 ? Math.Min(rate, MaximumRate) : MaximumRate;
        }
    }

    /// <summary>Whether the clock is ticking.</summary>
    public bool IsRunning => _running;

    /// <summary>Starts ticking from zero. Call on the UI thread.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _started = _last = Stopwatch.GetTimestamp();
#if IOS || MACCATALYST
        _link = CADisplayLink.Create(OnFrame);
        // Ask for the screen's top rate; without a range, ProMotion displays may stay at 60 Hz.
        var top = (float)RefreshRate;
        if (OperatingSystem.IsIOSVersionAtLeast(15) || OperatingSystem.IsMacCatalystVersionAtLeast(15))
            _link.PreferredFrameRateRange = new CAFrameRateRange { Minimum = Math.Min(60, top), Maximum = top, Preferred = top };
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
#elif ANDROID
        _callback = new Callback(this);
        Choreographer.Instance!.PostFrameCallback(_callback);
#else
        _timer = (Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread() ?? Microsoft.Maui.Controls.Application.Current?.Dispatcher)?.CreateTimer()
            ?? throw new InvalidOperationException("FrameClock needs a MAUI dispatcher on this thread.");
        _timer.Interval = TimeSpan.FromSeconds(1 / RefreshRate);
        _timer.Tick += (_, _) => OnFrame();
        _timer.Start();
#endif
    }

    /// <summary>Stops ticking. Call on the UI thread.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
#if IOS || MACCATALYST
        _link?.Invalidate();
        _link?.Dispose();
        _link = null;
#elif ANDROID
        if (_callback is not null) Choreographer.Instance!.RemoveFrameCallback(_callback);
        _callback = null;
#else
        _timer?.Stop();
        _timer = null;
#endif
    }

    private void OnFrame()
    {
        if (!_running) return;
        var now = Stopwatch.GetTimestamp();
        var delta = Stopwatch.GetElapsedTime(_last, now);
        _last = now;
        Tick?.Invoke(Stopwatch.GetElapsedTime(_started, now), delta);
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

#if ANDROID
    private sealed class Callback(FrameClock owner) : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos)
        {
            if (!owner._running) return;
            // Re-post first, so a slow tick handler cannot cost a frame.
            Choreographer.Instance!.PostFrameCallback(this);
            owner.OnFrame();
        }
    }
#endif
}

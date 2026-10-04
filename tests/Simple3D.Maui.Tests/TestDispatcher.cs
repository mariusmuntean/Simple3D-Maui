using Microsoft.Maui.Dispatching;

internal sealed class TestDispatcher : IDispatcher, IDispatcherProvider
{
    internal TestTimer Timer { get; } = new();
    public bool IsDispatchRequired => false;
    public IDispatcher GetForCurrentThread() => this;
    public bool Dispatch(Action action) { action(); return true; }
    public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException();
    public IDispatcherTimer CreateTimer() => Timer;

    internal sealed class TestTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; } = true;
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        // Can deliver a tick already queued before Stop, as a native dispatcher might.
        internal void DeliverTick() => Tick?.Invoke(this, EventArgs.Empty);
    }
}

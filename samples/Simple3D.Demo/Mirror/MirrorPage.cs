#if IOS || MACCATALYST || ANDROID
using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using Shape = Simple3D.Core.Shape;

namespace Simple3D.Demo.Mirror;

/// <summary>A 3D frog copies the person the camera sees. Guides the user from camera permission to a tracked full body.</summary>
public sealed class MirrorPage : ContentPage
{
    private enum Step { Intro, Starting, Guide, Live, Blocked }

    private readonly SceneView _view = new() { SceneBackgroundColor = Color.FromArgb("#0E1A2B"), IsInteractive = false, Opacity = 0 };
    private readonly CameraPreview _preview = new() { IsVisible = false };
    private readonly Border _previewFrame;
    private readonly View _intro, _guide, _blocked, _lostHint;
    private readonly Label _blockedText = Text("", 16), _status = Text("", 12, "#A9B8D4");
    private readonly Button _blockedAction;
#if DEBUG
    // Development and automated checks without a camera: replay a video (and on simulators without a body-pose
    // model, recorded landmarks) through the same page.
    private readonly string? _videoPath = Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_VIDEO");
    private readonly string? _landmarksPath = Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_LANDMARKS");
#else
    private readonly string? _videoPath = null, _landmarksPath = null;
#endif
    internal static bool IsRequested =>
#if DEBUG
        Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_VIDEO") is not null;
#else
        false;
#endif
    private MirrorSession? _session;
    private IPoseSource? _source;
    private Task? _loading;
    private Step _step;
    private PoseFrame? _pending;
    private IDispatcherTimer? _timer;
    private long _started;
    private int _frames, _poses;
    private double _detectMilliseconds;

    public MirrorPage()
    {
        BackgroundColor = Color.FromArgb("#0E1A2B");
        Shell.SetNavBarIsVisible(this, false);
        _previewFrame = new Border
        {
            StrokeThickness = 0, Content = _preview,
            StrokeShape = new RoundRectangle { CornerRadius = 0 }
        };
        _intro = Card("Mirror", "Your camera lets a 3D frog copy your moves in real time.\nVideo stays on this device.",
            ("Start", Start), ("Back", Close));
        _guide = Guide();
        _lostHint = Pill("Step back into the frame so your whole body shows");
        _blockedAction = PrimaryButton("Open Settings", CameraAccess.OpenSettings);
        _blocked = new Border
        {
            VerticalOptions = LayoutOptions.Center, Margin = 24, Padding = 22, BackgroundColor = Color.FromRgba(14, 22, 38, 235),
            StrokeShape = new RoundRectangle { CornerRadius = 22 }, Stroke = Color.FromRgba(255, 255, 255, 30),
            Content = new VerticalStackLayout { Spacing = 16, Children = { _blockedText, _blockedAction, SecondaryButton("Back", Close) } }
        };
        var close = new Button
        {
            Text = "✕", FontSize = 18, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0,
            BackgroundColor = Color.FromRgba(8, 12, 22, 170), TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(16, 16, 0, 0)
        };
        SemanticProperties.SetDescription(close, "Close Mirror");
        close.Clicked += (_, _) => Close();
        _status.HorizontalOptions = LayoutOptions.End; _status.VerticalOptions = LayoutOptions.End;
        _status.Margin = new Thickness(0, 0, 16, 12); _status.InputTransparent = true;
        Content = new Grid { Children = { _view, _previewFrame, _guide, _lostHint, _intro, _blocked, close, _status } };
        Show(Step.Intro);
        _loading = LoadAsync();
        if (_videoPath is not null) Dispatcher.Dispatch(Start);
    }

    private async Task LoadAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("froggy.vrm");
        // Decoding the model and its texture takes a moment; keep it off the UI thread.
        var character = await GltfLoader.LoadAsync(stream);
        _session = new MirrorSession(character.Shape, character.Model.Skeletons[0], character.Model.HumanoidBones, facesPositiveZ: false);
        _session.StateChanged += OnTrackingChanged;
        _view.Scene = _session.Stage.Scene;
        _view.Camera = _session.Stage.Camera;
    }

    private async void Start()
    {
        if (_step is Step.Starting or Step.Guide or Step.Live) return;
        Show(Step.Starting);
        try
        {
            if (_videoPath is null)
            {
                var access = await CameraAccess.RequestAsync();
                if (access != CameraAccessStatus.Granted)
                {
                    Block(access == CameraAccessStatus.Restricted
                        ? "Camera access is restricted on this device, so Mirror can't see anyone."
                        : "Mirror needs the camera to see who to copy. You can allow it in Settings.", settings: true);
                    return;
                }
            }
            await _loading!;
            _source = _videoPath is null ? PoseSources.CreateCamera() : PoseSources.CreateVideo(_videoPath, _landmarksPath);
            _source.FrameAnalyzed += OnFrame;
            if (_source is PoseSource diagnostics) diagnostics.DetectionMeasured += ms => _detectMilliseconds = ms;
            _preview.Source = _source;
            _session!.Reset();
            await _source.StartAsync();
            _started = Stopwatch.GetTimestamp();
            _timer ??= Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000d / 30);
            _timer.Tick -= Animate; _timer.Tick += Animate;
            _timer.Start();
            Show(Step.Guide);
            Log($"MIRROR_START source={(_videoPath is null ? "camera" : "video")} renderer={_view.ActiveRenderingBackend}");
        }
        catch (Exception error)
        {
            await StopAsync();
            Block($"The camera couldn't start: {error.Message}", settings: false);
            Log($"MIRROR_FAIL {error.GetType().Name}: {error.Message}");
        }
    }

    // Detector thread: keep only the newest frame, so a slow UI never builds a queue of stale poses.
    private void OnFrame(PoseFrame frame)
    {
        if (Interlocked.Exchange(ref _pending, frame) is null) Dispatcher.Dispatch(ApplyPending);
    }

    private void ApplyPending()
    {
        if (Interlocked.Exchange(ref _pending, null) is not { } frame || _session is null || _source is null) return;
        _frames++;
        if (_session.Apply(frame, _source.IsMirrored)) _poses++;
    }

    private void OnTrackingChanged(TrackingState state)
    {
        Log($"MIRROR_STATE {state} t={Stopwatch.GetElapsedTime(_started).TotalSeconds:0.00}");
        switch (state)
        {
            case TrackingState.Tracking:
                MirrorHaptics.PersonFound();
                Show(Step.Live);
                break;
            case TrackingState.Lost:
                MirrorHaptics.PersonLost();
                _lostHint.IsVisible = true;
                break;
            default:
                Show(Step.Guide);
                break;
        }
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (_session is null) return;
        var seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        _session.Stage.Animate((float)seconds);
        // About once a second: pose rate, detection latency, renderer and how closely the limbs follow.
        if ((int)(seconds * 30) % 30 != 0 || seconds < 1) return;
        var line = $"{_frames / seconds:0} poses/s · detect {_detectMilliseconds:0} ms · {_view.ActiveRenderingBackend} {_view.LastFrameMilliseconds:0.0} ms";
        _status.Text = line;
        if (_step == Step.Live) Log($"MIRROR_STATS t={seconds:0.0} analyzed={_frames} posed={_poses} detect_ms={_detectMilliseconds:0.0} render_ms={_view.LastFrameMilliseconds:0.0} limb_error_deg={_session.LimbError:0.0} backend={_view.ActiveRenderingBackend}");
    }

    private void Show(Step step)
    {
        _step = step;
        _intro.IsVisible = step is Step.Intro or Step.Starting;
        _intro.Opacity = step == Step.Starting ? .6 : 1;
        _blocked.IsVisible = step == Step.Blocked;
        _guide.IsVisible = step == Step.Guide;
        _lostHint.IsVisible = false;
        _status.IsVisible = step is Step.Guide or Step.Live;
        _preview.IsVisible = step is Step.Guide or Step.Live;
        // Full-screen viewfinder while searching; a small inset while the frog performs.
        var live = step == Step.Live;
        _previewFrame.WidthRequest = live ? 108 : -1;
        _previewFrame.HeightRequest = live ? 144 : -1;
        _previewFrame.HorizontalOptions = live ? LayoutOptions.End : LayoutOptions.Fill;
        _previewFrame.VerticalOptions = live ? LayoutOptions.Start : LayoutOptions.Fill;
        _previewFrame.Margin = live ? new Thickness(0, 16, 16, 0) : 0;
        _previewFrame.StrokeShape = new RoundRectangle { CornerRadius = live ? 16 : 0 };
        _previewFrame.StrokeThickness = live ? 2 : 0;
        _previewFrame.Stroke = Color.FromRgba(255, 255, 255, 120);
        _ = _view.FadeTo(live ? 1 : 0, 250);
    }

    private void Block(string message, bool settings)
    {
        _blockedText.Text = message;
        _blockedAction.IsVisible = settings;
        Show(Step.Blocked);
    }

    private async void Close()
    {
        await StopAsync();
        if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
    }

    private async Task StopAsync()
    {
        _timer?.Stop();
        if (_source is { } source)
        {
            _source = null;
            source.FrameAnalyzed -= OnFrame;
            _preview.Source = null;
            await source.DisposeAsync();
        }
    }

    protected override void OnDisappearing()
    {
        // Leaving the page (or the app going to the background on some platforms) releases the camera.
        _ = StopAsync();
        if (_step is Step.Guide or Step.Live) Show(Step.Intro);
        base.OnDisappearing();
    }

    private View Guide()
    {
        // A translucent standing figure shows how much of the person should be in frame.
        var silhouette = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(
                "M50,4 A13,13 0 1 1 49.9,4 Z M30,36 L70,36 Q80,36 82,46 L90,100 Q91,106 85,107 Q80,107 79,101 L72,60 L72,112 L68,190 " +
                "Q67,196 61,196 Q55,196 55,190 L52,124 L48,124 L45,190 Q45,196 39,196 Q33,196 32,190 L28,112 L28,60 L21,101 Q20,107 15,107 " +
                "Q9,106 10,100 L18,46 Q20,36 30,36 Z")!,
            Aspect = Stretch.Uniform, Fill = Color.FromRgba(255, 255, 255, 40), Stroke = Color.FromRgba(255, 255, 255, 200),
            StrokeThickness = 2, StrokeDashArray = [4, 3], Margin = new Thickness(0, 90, 0, 120), InputTransparent = true
        };
        return new Grid
        {
            InputTransparent = true,
            Children = { silhouette, Pill("Point the camera at someone and fit their whole body in the frame", top: true) }
        };
    }

    private static View Card(string title, string body, params (string Text, Action Action)[] buttons)
    {
        var stack = new VerticalStackLayout { Spacing = 14, Children = { Text(title, 30, bold: true), Text(body, 16, "#C7D3EA") } };
        for (var i = 0; i < buttons.Length; i++)
            stack.Add(i == 0 ? PrimaryButton(buttons[i].Text, buttons[i].Action) : SecondaryButton(buttons[i].Text, buttons[i].Action));
        return new Border
        {
            VerticalOptions = LayoutOptions.Center, Margin = 24, Padding = 24, BackgroundColor = Color.FromRgba(14, 22, 38, 235),
            Stroke = Color.FromRgba(255, 255, 255, 30), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = stack
        };
    }

    private static View Pill(string text, bool top = false) => new Border
    {
        InputTransparent = true, Padding = new Thickness(16, 10), Margin = new Thickness(24, top ? 72 : 0, 24, top ? 0 : 40),
        HorizontalOptions = LayoutOptions.Center, VerticalOptions = top ? LayoutOptions.Start : LayoutOptions.End,
        BackgroundColor = Color.FromRgba(8, 12, 22, 200), Stroke = Color.FromRgba(255, 255, 255, 40), StrokeShape = new RoundRectangle { CornerRadius = 18 },
        Content = Text(text, 15, center: true)
    };

    private static Button PrimaryButton(string text, Action action) => Button(text, action, "#4C7BD9");
    private static Button SecondaryButton(string text, Action action) => Button(text, action, "#30466C");
    private static Button Button(string text, Action action, string color)
    {
        var button = new Button { Text = text, BackgroundColor = Color.FromArgb(color), TextColor = Colors.White, CornerRadius = 14, FontSize = 16, Padding = new Thickness(18, 12) };
        button.Clicked += (_, _) => action();
        return button;
    }

    private static Label Text(string text, double size, string color = "#FFFFFF", bool bold = false, bool center = false) => new()
    {
        Text = text, FontSize = size, TextColor = Color.FromArgb(color), FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        HorizontalTextAlignment = center ? TextAlignment.Center : TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap
    };

    private static void Log(string line)
    {
#if ANDROID
        Android.Util.Log.Info("Simple3D", line);
#else
        Console.WriteLine(line);
#endif
    }
}
#endif

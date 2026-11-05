#if IOS || MACCATALYST || ANDROID
using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Simple3D.Core;
using Simple3D.Maui;
using Simple3D.Shared.Mirror;
using Shape = Simple3D.Core.Shape;

namespace Simple3D.Demo.Mirror;

/// <summary>A 3D character copies the person the camera sees: body, head and face. Guides the user from camera permission
/// to a tracked person, and lets them pick the character from a carousel.</summary>
public sealed class MirrorPage : ContentPage
{
    private enum Step { Intro, Starting, Guide, Live, Blocked }

    private readonly SceneView _view = new() { SceneBackgroundColor = Color.FromArgb("#0E1A2B"), IsInteractive = false, Opacity = 0 };
    private readonly CameraPreview _preview = new() { IsVisible = false };
    private readonly Border _previewFrame;
    private readonly View _intro, _guide, _blocked, _lostHint;
    private readonly Label _blockedText = Text("", 16), _status = Text("", 12, "#A9B8D4");
    private readonly Button _blockedAction, _switchCamera;
    private readonly View _characters;
    private readonly Label _introCharacter = Text("", 15, "#FFD27A", bold: true);
    private readonly List<(MirrorModel Model, Border Chip)> _chips = [];
    private MirrorModel _model = MirrorModels.Find(Preferences.Default.Get<string?>("mirror.character", null));
    // The person's choice of camera; front by default, since people usually film themselves.
    private bool _frontCamera = Preferences.Default.Get("mirror.camera.front", true);
    private bool _switching;
    private readonly Label _zoomText = Text("", 15, center: true);
    private readonly Border _zoomPill;
    private readonly View _guidePill;
    private int _zoomShown, _choice;
    // Debug builds can replay a video instead of the camera (and on simulators without a body-pose model, recorded
    // landmarks instead of detection) through this same page, for development and automated checks.
#if DEBUG
    private static string? VideoPath => Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_VIDEO");
    private static string? LandmarksPath => Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_LANDMARKS");
#else
    private static string? VideoPath => null;
    private static string? LandmarksPath => null;
#endif
    internal static bool IsRequested => VideoPath is not null;
    private MirrorSession? _session;
    private IPoseSource? _source;
    private Task? _loading;
    private Step _step;
    private PoseFrame? _pending;
    private readonly FrameClock _clock = new();
    private long _started, _lastFrame;
    private TimeSpan _lastTimestamp;
    private int _generation;
    private Task _stopping = Task.CompletedTask;
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
        _intro = Intro();
        _guidePill = Pill("", top: true);
        _guide = Guide();
        _lostHint = Pill("Step back into the frame: whole body, waist up, or your face");
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
        _switchCamera = new Button
        {
            Text = "⇄", FontSize = 18, WidthRequest = 44, HeightRequest = 44, CornerRadius = 22, Padding = 0,
            BackgroundColor = Color.FromRgba(8, 12, 22, 170), TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(68, 16, 0, 0)
        };
        SemanticProperties.SetDescription(_switchCamera, "Switch between the front and back camera");
        _switchCamera.Clicked += (_, _) => SwitchCamera();
        _characters = Carousel();
        // Debug-only developer stats, under the top-left buttons and clear of the carousel.
        _status.HorizontalOptions = LayoutOptions.Start; _status.VerticalOptions = LayoutOptions.Start;
        _status.Margin = new Thickness(18, 68, 0, 0); _status.InputTransparent = true;
        _zoomPill = new Border
        {
            IsVisible = false, InputTransparent = true, Padding = new Thickness(14, 6), Margin = new Thickness(0, 20, 0, 0),
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromRgba(8, 12, 22, 200), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = _zoomText
        };
        var root = new Grid { Children = { _view, _previewFrame, _guide, _lostHint, _characters, _intro, _blocked, close, _switchCamera, _zoomPill, _status } };
        // Pinch anywhere to zoom the camera, for example to frame someone standing far away.
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinch;
        root.GestureRecognizers.Add(pinch);
        Content = root;
        _view.SizeChanged += (_, _) => UpdateLayout();
        Show(Step.Intro);
        _loading = LoadAsync();
        if (IsRequested) Dispatcher.Dispatch(Start);
    }

    private async Task LoadAsync()
    {
        // Decoding the model and its textures takes a moment; keep it off the UI thread.
        var session = new MirrorSession(await Character(_model));
        if (_session is { } previous) previous.StateChanged -= OnTrackingChanged;
        _session = session;
        session.StateChanged += OnTrackingChanged;
        _view.Scene = session.Stage.Scene;
        _view.Camera = session.Stage.Camera;
        UpdateLayout();
    }

    // Swaps the character without stopping the camera: the stage animates the change while tracking carries on.
    // The latest tap wins: a slower load from an earlier tap is dropped. The session outlives camera restarts, so a load that
    // finishes while the camera is stopped still applies.
    private async Task ChooseCharacterAsync(MirrorModel model)
    {
        if (model == _model) return;
        var previous = _model;
        var choice = ++_choice;
        _model = model;
        HighlightCharacter();
        try
        {
            // Parse and decode off the UI thread first, so the swap animation itself never waits on loading.
            var character = await Character(model);
            await _loading!;
            if (choice != _choice) return;
            _session!.SwitchCharacter(character);
            UpdateLayout();
            Preferences.Default.Set("mirror.character", model.File);
        }
        catch (Exception error)
        {
            if (choice != _choice) return;
            _model = previous;
            HighlightCharacter();
            Log($"MIRROR_FAIL character {model.File}: {error.Message}");
        }
    }

    private static async Task<MirrorCharacter> Character(MirrorModel model)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(model.File);
        var loaded = await GltfLoader.LoadAsync(stream);
        return MirrorCharacter.FromVrm(loaded.Model, loaded.Shape, model.Face);
    }

    private async void Start()
    {
        if (_step is Step.Starting or Step.Guide or Step.Live) return;
        Show(Step.Starting);
        // Closing or backgrounding during any await below invalidates this attempt.
        var attempt = ++_generation;
        bool Current() => attempt == _generation && _step == Step.Starting;
        try
        {
            await _stopping;
            if (!IsRequested)
            {
                var access = await CameraAccess.RequestAsync();
                if (!Current()) return;
                if (access != CameraAccessStatus.Granted)
                {
                    Block(access == CameraAccessStatus.Restricted
                        ? "Camera access is restricted on this device, so Mirror can't see anyone."
                        : "Mirror needs the camera to see who to copy. You can allow it in Settings.", settings: true);
                    return;
                }
            }
            await _loading!;
            if (!Current()) return;
            var source = _source = VideoPath is not { } video ? PoseSources.CreateCamera(_frontCamera) : PoseSources.CreateVideo(video, LandmarksPath);
            source.FrameAnalyzed += OnFrame;
            if (source is PoseSource diagnostics)
            {
                diagnostics.DetectionMeasured += ms => _detectMilliseconds = ms;
                diagnostics.Failed += error => Dispatcher.Dispatch(() => Fail(source, error));
            }
            _preview.Source = source;
            _session!.Reset();
            await source.StartAsync();
            if (!Current()) return;
            _started = _lastFrame = Stopwatch.GetTimestamp();
            // The display's own rate (60, 120 Hz or more): fireflies and character swaps animate smoothly; poses still
            // arrive at the camera's rate.
            _clock.Tick -= Animate; _clock.Tick += Animate;
            _clock.Start();
            _switching = false;
            Show(Step.Guide);
            Log($"MIRROR_START source={(IsRequested ? "video" : "camera")}");
        }
        catch (Exception error)
        {
            _switching = false;
            if (attempt != _generation) return;
            Stop();
            await _stopping;
            Block($"The camera couldn't start: {error.Message}", settings: false);
            Log($"MIRROR_FAIL {error.GetType().Name}: {error.Message}");
        }
    }

    private void SwitchCamera()
    {
        if (_step is not (Step.Guide or Step.Live)) return;
        _frontCamera = !_frontCamera;
        Preferences.Default.Set("mirror.camera.front", _frontCamera);
        Stop();
        _switching = true;
        _step = Step.Intro;
        Start();
    }

    private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (_step is not (Step.Guide or Step.Live) || _source is not PoseSource { MaximumZoom: > 1 } source) return;
        if (e.Status == GestureStatus.Running)
        {
            // Scale is relative to the previous update.
            source.Zoom = source.Zoom * (float)e.Scale;
            _zoomText.Text = $"{source.Zoom:0.0}×";
            _zoomPill.IsVisible = true;
        }
        else if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
        {
            // The person's limbs changed size in the image; relearn their lengths for depth estimates.
            _session?.Recalibrate();
            var shown = ++_zoomShown;
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.2), () => { if (shown == _zoomShown) _zoomPill.IsVisible = false; });
        }
    }

    // A source that fails after starting (detector or playback error) stops the session and says why.
    private void Fail(IPoseSource source, Exception error)
    {
        if (!ReferenceEquals(source, _source)) return;
        Stop();
        Block($"Tracking stopped: {error.Message}", settings: false);
        Log($"MIRROR_FAIL {error.GetType().Name}: {error.Message}");
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
        _lastFrame = Stopwatch.GetTimestamp();
        _lastTimestamp = frame.Timestamp;
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

    private void Animate(TimeSpan elapsed, TimeSpan delta)
    {
        if (_session is null || _source is null) return;
        var seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        _session.Present(delta);
        _session.Stage.Animate((float)seconds);
        // A stalled camera delivers no frames, so no frame can report the person gone: report an empty one.
        // Two seconds, well beyond ordinary detector hiccups, which the tracker already rides out.
        var silence = Stopwatch.GetElapsedTime(_lastFrame);
        if (_step == Step.Live && silence > TimeSpan.FromSeconds(2))
            _session.Apply(new PoseFrame(_lastTimestamp + silence, 1, 1, null), _source.IsMirrored);
        // About once a second: pose rate, detection latency and renderer.
        if ((int)seconds == (int)(seconds - delta.TotalSeconds) || seconds < 1) return;
        var line = $"{_frames / seconds:0} poses/s · detect {_detectMilliseconds:0} ms · {_view.ActiveRenderingBackend} {_view.LastFrameMilliseconds:0.0} ms · {_clock.RefreshRate:0} Hz";
        _status.Text = line;
        if (_step == Step.Live) Log($"MIRROR_STATS t={seconds:0.0} analyzed={_frames} posed={_poses} detect_ms={_detectMilliseconds:0.0} render_ms={_view.LastFrameMilliseconds:0.0} backend={_view.ActiveRenderingBackend}");
    }

    private void Show(Step step)
    {
        _step = step;
        // Switching cameras restarts capture without bringing back the intro.
        _intro.IsVisible = step is Step.Intro || step == Step.Starting && !_switching;
        _switchCamera.IsVisible = !IsRequested && step is Step.Guide or Step.Live;
        _characters.IsVisible = step is Step.Guide or Step.Live;
        _intro.Opacity = step == Step.Starting ? .6 : 1;
        _blocked.IsVisible = step == Step.Blocked;
        _guide.IsVisible = step == Step.Guide;
        _lostHint.IsVisible = false;
        // Pose rate, detection latency and frame time are for developers, not for the people using the app.
#if DEBUG
        _status.IsVisible = step is Step.Guide or Step.Live;
#else
        _status.IsVisible = false;
#endif
        _preview.IsVisible = step is Step.Guide or Step.Live;
        // Full-screen viewfinder while searching; a small inset while the frog performs.
        var live = step == Step.Live;
        UpdateLayout();
        _previewFrame.HorizontalOptions = live ? LayoutOptions.End : LayoutOptions.Fill;
        _previewFrame.VerticalOptions = live ? LayoutOptions.Start : LayoutOptions.Fill;
        _previewFrame.Margin = live ? new Thickness(0, 16, 16, 0) : 0;
        _previewFrame.StrokeShape = new RoundRectangle { CornerRadius = live ? 16 : 0 };
        _previewFrame.StrokeThickness = live ? 2 : 0;
        _previewFrame.Stroke = Color.FromRgba(255, 255, 255, 120);
        _ = _view.FadeToAsync(live ? 1 : 0, 250);
    }

    private void UpdateLayout()
    {
        var width = _view.Width;
        var height = _view.Height;
        if (width <= 0 || height <= 0) return;
        var landscape = width > height;
        if (_session is { } session)
        {
            session.Stage.Frame((float)(width / height));
            _view.Camera = session.Stage.Camera;
        }
        var live = _step == Step.Live;
        _previewFrame.WidthRequest = live ? landscape ? 192 : 108 : -1;
        _previewFrame.HeightRequest = live ? landscape ? 108 : 144 : -1;
        if (_guidePill is Border { Content: Label label })
            label.Text = landscape ? "Step back · body, waist up or face" :
                "Get into the frame: your whole body, from the waist up, or just your head for face tracking";
        _guidePill.MaximumWidthRequest = Math.Max(1, width - 48);
        _lostHint.MaximumWidthRequest = Math.Max(1, width - 48);
        // Debug stats wrap left of the camera inset.
        _status.MaximumWidthRequest = Math.Max(1, width - (live ? landscape ? 240 : 160 : 36));
        // Keep transient zoom feedback below the top buttons and camera inset.
        _zoomPill.Margin = new Thickness(0, live && landscape ? 132 : 168, 0, 0);
    }

    private void Block(string message, bool settings)
    {
        _blockedText.Text = message;
        _blockedAction.IsVisible = settings;
        Show(Step.Blocked);
    }

    private async void Close()
    {
        Stop();
        await _stopping;
        if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
    }

    // Stops capture now and lets disposal finish in the background; the next Start waits for it.
    private void Stop()
    {
        _generation++;
        _clock.Stop();
        _pending = null;
        if (_source is not { } source) return;
        _source = null;
        source.FrameAnalyzed -= OnFrame;
        _preview.Source = null;
        var previous = _stopping;
        _stopping = DisposeAfter(previous, source);
        static async Task DisposeAfter(Task previous, IPoseSource source)
        {
            try { await previous; } catch { }
            await source.DisposeAsync();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Window is { } window) { window.Stopped -= OnStopped; window.Stopped += OnStopped; }
    }

    protected override void OnDisappearing()
    {
        if (Window is { } window) window.Stopped -= OnStopped;
        OnStopped(this, EventArgs.Empty);
        base.OnDisappearing();
    }

    // Leaving the page or sending the app to the background releases the camera; Start resumes from the intro.
    private void OnStopped(object? sender, EventArgs e)
    {
        Stop();
        if (_step is Step.Guide or Step.Live) Show(Step.Intro);
    }

    private View Intro()
    {
        static View Way(string icon, string title, string detail) => new HorizontalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Border
                {
                    WidthRequest = 40, HeightRequest = 40, BackgroundColor = Color.FromRgba(76, 123, 217, 60), StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 12 },
                    Content = new Label { Text = icon, FontSize = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                },
                new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { Text(title, 16, bold: true), Text(detail, 13, "#A9B8D4") } }
            }
        };
        HighlightCharacter();
        var stack = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                Text("Mirror", 32, bold: true),
                Text("Move, and a 3D character moves with you: arms, legs, torso, head and face, live from your camera.", 16, "#C7D3EA"),
                Way("🧍", "Whole body", "Step back until head to feet are in view"),
                Way("🙋", "Waist up", "Arms, torso and head"),
                Way("🙂", "Face", "Come close: mouth, eyes, smile and head"),
                _introCharacter,
                PrimaryButton("Start camera", Start),
                SecondaryButton("Back", Close),
                Text("Everything runs on this device. Nothing is recorded or sent.", 12, "#7F8FAF", center: true),
            }
        };
        return new ScrollView
        {
            VerticalOptions = LayoutOptions.Center,
            Content = new Border
            {
                Margin = 24, Padding = 24, MaximumWidthRequest = 440, BackgroundColor = Color.FromRgba(14, 22, 38, 235),
                Stroke = Color.FromRgba(255, 255, 255, 30), StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = stack
            }
        };
    }

    // A strip of character thumbnails along the bottom; the chosen one is outlined.
    private View Carousel()
    {
        var row = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(16, 0) };
        foreach (var model in MirrorModels.All)
        {
            var image = new Image { WidthRequest = 56, HeightRequest = 56, Aspect = Aspect.AspectFill, Source = Thumbnail(model) };
            var chip = new Border
            {
                Padding = 3, StrokeThickness = 2.5, BackgroundColor = Color.FromRgba(8, 12, 22, 190),
                StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 15 }, Content = image }
            };
            SemanticProperties.SetHint(chip, "Switches to this character");
            AutomationProperties.SetIsInAccessibleTree(chip, true);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await ChooseCharacterAsync(model);
            chip.GestureRecognizers.Add(tap);
            _chips.Add((model, chip));
            row.Add(chip);
        }
        HighlightCharacter();
        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 22), Content = row
        };
    }

    private void HighlightCharacter()
    {
        _introCharacter.Text = $"Your character: {_model.Name} · change it any time";
        foreach (var (model, chip) in _chips)
        {
            var chosen = model == _model;
            chip.Stroke = chosen ? Color.FromArgb("#FFD27A") : Color.FromRgba(255, 255, 255, 40);
            chip.Scale = chosen ? 1.08 : 1;
            SemanticProperties.SetDescription(chip, chosen ? $"{model.Name}, selected" : model.Name);
        }
    }

    // Thumbnails are rendered from each model (see docs/DEVELOPMENT.md) and bundled as raw assets.
    private static ImageSource Thumbnail(MirrorModel model) =>
        ImageSource.FromStream(async _ => await FileSystem.OpenAppPackageFileAsync("mirror/" + System.IO.Path.ChangeExtension(model.File, ".png")));

    private View Guide()
    {
        // A translucent standing figure shows how much of the person should be in frame.
        var silhouette = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(
                "M50,4 A13,13 0 1 1 49.9,4 Z M30,36 L70,36 Q80,36 82,46 L90,100 Q91,106 85,107 Q80,107 79,101 L72,60 L72,112 L68,190 " +
                "Q67,196 61,196 Q55,196 55,190 L52,124 L48,124 L45,190 Q45,196 39,196 Q33,196 32,190 L28,112 L28,60 L21,101 Q20,107 15,107 " +
                "Q9,106 10,100 L18,46 Q20,36 30,36 Z")!,
            Aspect = Stretch.Uniform, Fill = Color.FromRgba(255, 255, 255, 22), Stroke = Color.FromRgba(255, 255, 255, 200),
            StrokeThickness = 2, StrokeDashArray = [4, 3], Margin = new Thickness(0, 128, 0, 104), InputTransparent = true
        };
        return new Grid
        {
            InputTransparent = true,
            Children = { silhouette, _guidePill }
        };
    }

    private static View Pill(string text, bool top = false) => new Border
    {
        InputTransparent = true, Padding = new Thickness(16, 10), Margin = new Thickness(24, top ? 72 : 0, 24, top ? 0 : 108),
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

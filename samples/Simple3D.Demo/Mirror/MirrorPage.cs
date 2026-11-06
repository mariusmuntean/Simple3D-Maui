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

    private readonly SceneView _view = new() { SceneBackgroundColor = Theme.Canvas, IsInteractive = false, Opacity = 0 };
    private readonly CameraPreview _preview = new() { IsVisible = false };
    private readonly Border _previewFrame;
    private readonly Grid _previewHost;
    private readonly View _intro, _guide, _blocked, _lostHint, _insetStatus, _characterCard;
    private readonly Label _blockedText = Theme.Label("", Theme.FontBody, 15, Theme.TextSoft), _status = Theme.Label("", Theme.FontMono, 11, Theme.TextMuted);
    private readonly Label _insetText = Theme.Label("", Theme.FontLabel, 11, Theme.Text);
    private readonly Border _insetDot = new() { WidthRequest = 6, HeightRequest = 6, StrokeShape = new Ellipse(), VerticalOptions = LayoutOptions.Center };
    private readonly Label _characterName = Theme.Label("", Theme.FontDisplay, 16, Theme.Text), _characterTracks = Theme.Label("", Theme.FontBody, 12, Theme.TextMuted);
    private readonly ActionButton _blockedAction;
    private readonly IconButton _close, _switchCamera;
    private readonly Grid _topButtons = new() { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Start, InputTransparent = true, CascadeInputTransparent = false };
    private readonly ScrollView _characters;
    private readonly StackLayout _characterRow = new() { Spacing = 12 };
    private readonly Label _introCharacter = Theme.Label("", Theme.FontLabel, 13, Theme.EmberLight);
    private readonly Image _hero = new() { Aspect = Aspect.AspectFill };
    private readonly List<(MirrorModel Model, Border Chip, Button Button)> _chips = [];
    private MirrorModel _model = MirrorModels.Find(Preferences.Default.Get<string?>("mirror.character", null));
    // The person's choice of camera; front by default, since people usually film themselves.
    private bool _frontCamera = Preferences.Default.Get("mirror.camera.front", true);
    private bool _switching;
    private readonly Label _zoomText = Theme.Label("", Theme.FontMono, 15, Theme.EmberLight);
    private readonly Border _zoomPill;
    private readonly Label _guideText = Theme.Label("", Theme.FontBody, 14, Theme.Text);
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

    public MirrorPage(RenderingMode renderingMode = RenderingMode.Auto)
    {
        _view.RenderingMode = renderingMode;
        BackgroundColor = Theme.Canvas;
        SafeAreaEdges = SafeAreaEdges.None;
        Shell.SetNavBarIsVisible(this, false);
        _insetStatus = new HorizontalStackLayout
        {
            Spacing = 5, Margin = 8, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End, InputTransparent = true,
            Children = { _insetDot, _insetText }
        };
        _previewFrame = new Border
        {
            StrokeThickness = 0, Content = new Grid { Children = { _preview, _insetStatus } },
            StrokeShape = new RoundRectangle { CornerRadius = 0 }
        };
        SemanticProperties.SetDescription(_previewFrame, "Camera preview");
        _previewHost = new Grid { InputTransparent = true, Children = { _previewFrame } };
        _intro = Intro();
        _guide = Guide();
        _lostHint = Hint(Icons.Create(Icon.FindPerson, Theme.Ember, 20), "Step back into the frame: whole body, waist up, or your face", top: false);
        _blockedAction = new ActionButton("Open Settings", CameraAccess.OpenSettings, ActionStyle.Primary, Icon.Settings, 56);
        _blocked = Blocked();
        _close = new IconButton(Icon.Close, "Close Mirror", Close) { HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        _switchCamera = new IconButton(Icon.SwitchCamera, "Switch between the front and back camera", SwitchCamera)
        {
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start
        };
        _topButtons.Add(_close);
        _topButtons.Add(_switchCamera);
        _characters = Carousel();
        _characterCard = new Border
        {
            Padding = new Thickness(18, 10), BackgroundColor = Theme.OverlayStrong, Stroke = Theme.Line, StrokeThickness = 1,
            StrokeShape = Theme.Rounded(18), InputTransparent = true,
            Content = new VerticalStackLayout { Spacing = 2, Children = { _characterName, _characterTracks } }
        };
        // Debug-only developer stats, under the top-left buttons and clear of the carousel.
        _status.HorizontalOptions = LayoutOptions.Start; _status.VerticalOptions = LayoutOptions.Start;
        _status.InputTransparent = true;
        _zoomPill = new Border
        {
            IsVisible = false, InputTransparent = true, Padding = new Thickness(14, 6),
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start, BackgroundColor = Theme.OverlayStrong,
            Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(Theme.RadiusSmall), Content = _zoomText
        };
        var overlay = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.All, Padding = new Thickness(Theme.Margin, 8, Theme.Margin, 12), InputTransparent = true, CascadeInputTransparent = false,
            Children = { _guide, _lostHint, _characterCard, _characters, _blocked, _topButtons, _zoomPill, _status }
        };
        var root = new Grid { SafeAreaEdges = SafeAreaEdges.None, Children = { _view, _previewHost, overlay, _intro } };
        // Pinch anywhere to zoom the camera, for example to frame someone standing far away.
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinch;
        root.GestureRecognizers.Add(pinch);
        Content = root;
        _view.SizeChanged += (_, _) => UpdateLayout();
        Show(Step.Intro);
        _loading = LoadAsync();
        if (IsRequested) Dispatcher.Dispatch(Start);
#if DEBUG
        // Screenshot of the blocked state without denying the camera: SIMPLE3D_MIRROR_STATE=blocked.
        if (Environment.GetEnvironmentVariable("SIMPLE3D_MIRROR_STATE") == "blocked")
            Block("Mirror needs the camera to see who to copy. You can allow it in Settings.", settings: true);
#endif
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
                _characterCard.IsVisible = false;
                ShowInset(searching: true);
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
        _intro.Opacity = step == Step.Starting ? .6 : 1;
        _topButtons.IsVisible = step is not (Step.Intro or Step.Starting) || _switching;
        _switchCamera.IsVisible = !IsRequested && step is Step.Guide or Step.Live;
        _characters.IsVisible = step is Step.Guide or Step.Live;
        _blocked.IsVisible = step == Step.Blocked;
        _guide.IsVisible = step == Step.Guide;
        _lostHint.IsVisible = false;
        // The blocked screen goes back to the intro's choices; while the camera runs, the button closes Mirror.
        _close.SetIcon(step == Step.Blocked ? Icon.Back : Icon.Close, Theme.Text);
        SemanticProperties.SetDescription(_close.Button, step == Step.Blocked ? "Back" : "Close Mirror");
        // Pose rate, detection latency and frame time are for developers, not for the people using the app.
#if DEBUG
        _status.IsVisible = step is Step.Guide or Step.Live;
#else
        _status.IsVisible = false;
#endif
        _preview.IsVisible = step is Step.Guide or Step.Live;
        // Full-screen viewfinder while searching; a small inset while the character performs.
        var live = step == Step.Live;
        // iOS keeps modal content out of the status bar: there, match the scene instead of showing the glow.
        Background = step is Step.Guide or Step.Live ? new SolidColorBrush(Theme.Canvas) : Theme.Glow(Theme.Canvas, .3, .9);
        _characterCard.IsVisible = live;
        _previewHost.SafeAreaEdges = live ? SafeAreaEdges.All : SafeAreaEdges.None;
        _previewFrame.HorizontalOptions = live ? LayoutOptions.End : LayoutOptions.Fill;
        _previewFrame.VerticalOptions = live ? LayoutOptions.Start : LayoutOptions.Fill;
        _previewFrame.Margin = live ? new Thickness(0, 8, Theme.Margin, 0) : 0;
        _previewFrame.StrokeShape = new RoundRectangle { CornerRadius = live ? 18 : 0 };
        _previewFrame.StrokeThickness = live ? 2 : 0;
        _insetStatus.IsVisible = live;
        if (live) ShowInset(searching: false);
        UpdateLayout();
        _ = _view.FadeToAsync(live ? 1 : 0, 250);
    }

    private void ShowInset(bool searching)
    {
        _insetText.Text = searching ? "Searching" : "Tracking";
        _insetText.TextColor = searching ? Theme.TextSoft : Theme.Text;
        _insetDot.BackgroundColor = searching ? Colors.Transparent : Theme.Ember;
        _insetDot.Stroke = searching ? Theme.TextMuted : null;
        _insetDot.StrokeThickness = searching ? 1.5 : 0;
        _previewFrame.Stroke = searching ? Theme.Text.WithAlpha(.2f) : Theme.EmberLight.WithAlpha(.5f);
        _previewFrame.Shadow = searching ? null! : Theme.EmberGlow(18, .25f);
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
        _guideText.Text = landscape ? "Step back · body, waist up or face" : "Get into the frame: whole body, waist up, or just your head for face tracking";
        // Close and switch camera stack on the left in portrait; in landscape, switch camera sits left of the camera inset.
        _switchCamera.HorizontalOptions = landscape && live ? LayoutOptions.End : step(LayoutOptions.Start, LayoutOptions.End);
        _switchCamera.Margin = landscape && live ? new Thickness(0, 0, 192 + 10, 0) : _step == Step.Live ? new Thickness(0, 56, 0, 0) : 0;
        // The carousel runs along the bottom in portrait and down the left edge in landscape, clear of the character's feet.
        var chip = landscape ? 52 : 64;
        _characterRow.Orientation = landscape ? StackOrientation.Vertical : StackOrientation.Horizontal;
        _characterRow.Spacing = landscape ? 10 : 12;
        _characterRow.Padding = landscape ? new Thickness(6, 8) : new Thickness(8, 6);
        foreach (var (_, border, _) in _chips.Where(c => c.Chip.Parent?.Parent == _characterRow))
        {
            border.WidthRequest = border.HeightRequest = chip;
            border.StrokeShape = Theme.Rounded(landscape ? 16 : 20);
        }
        _characters.Orientation = landscape ? ScrollOrientation.Vertical : ScrollOrientation.Horizontal;
        _characters.HorizontalOptions = landscape ? LayoutOptions.Start : LayoutOptions.Center;
        _characters.VerticalOptions = landscape ? LayoutOptions.Center : LayoutOptions.End;
        _characters.Margin = landscape ? new Thickness(0, 56, 0, 0) : 0;
        // The character's name sits above the carousel in portrait, bottom right in landscape.
        _characterCard.HorizontalOptions = landscape ? LayoutOptions.End : LayoutOptions.Center;
        _characterCard.VerticalOptions = LayoutOptions.End;
        _characterCard.Margin = landscape ? 0 : new Thickness(0, 0, 0, 96);
        _characterName.HorizontalTextAlignment = _characterTracks.HorizontalTextAlignment = landscape ? TextAlignment.End : TextAlignment.Center;
        _lostHint.Margin = new Thickness(8, 0, 8, landscape ? 0 : 96);
        _lostHint.MaximumWidthRequest = landscape ? 420 : double.PositiveInfinity;
        // Debug stats wrap left of the camera inset.
        _status.Margin = new Thickness(landscape ? 64 : 0, landscape ? 4 : 112, 0, 0);
        _status.MaximumWidthRequest = Math.Max(1, width - (live ? landscape ? 300 : 160 : 36));
        // Keep transient zoom feedback below the top buttons and camera inset.
        _zoomPill.Margin = new Thickness(0, live && landscape ? 124 : 160, 0, 0);

        LayoutOptions step(LayoutOptions portrait, LayoutOptions guide) => _step == Step.Guide ? guide : portrait;
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
        static View Way(Icon icon, string title, string detail)
        {
            var glyph = Icons.Create(icon, Theme.Ember, 26, 1.8);
            glyph.HorizontalOptions = LayoutOptions.Center;
            var name = Theme.Label(title, Theme.FontLabel, 13, Theme.Text);
            var text = Theme.Label(detail, Theme.FontBody, 11, Theme.TextMuted);
            name.HorizontalTextAlignment = text.HorizontalTextAlignment = TextAlignment.Center;
            return new Border
            {
                Padding = new Thickness(10, 14), BackgroundColor = Theme.Surface, Stroke = Theme.Line, StrokeThickness = 1, StrokeShape = Theme.Rounded(18),
                Content = new VerticalStackLayout { Spacing = 8, Children = { glyph, name, text } }
            };
        }
        var back = new IconButton(Icon.Back, "Back to the gallery", Close) { HorizontalOptions = LayoutOptions.Start };
        var hero = new Border
        {
            WidthRequest = 132, HeightRequest = 132, HorizontalOptions = LayoutOptions.Center, StrokeThickness = 2, Stroke = Theme.Ember,
            StrokeShape = Theme.Rounded(40), BackgroundColor = Theme.Surface, Shadow = Theme.EmberGlow(40, .5f), Content = _hero
        };
        AutomationProperties.SetIsInAccessibleTree(hero, false);
        var title = Theme.Label("Mirror", Theme.FontDisplay, 32, Theme.Text);
        var about = Theme.Label("Move, and a 3D character moves with you: arms, legs, torso, head and face, live from your camera.", Theme.FontBody, 15, Theme.TextSoft);
        title.HorizontalTextAlignment = about.HorizontalTextAlignment = TextAlignment.Center;
        about.MaximumWidthRequest = 310;
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        var ways = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        ways.Add(Way(Icon.Body, "Whole body", "Step back, head to feet in view"));
        ways.Add(Way(Icon.WaistUp, "Waist up", "Arms, torso and head"), 1);
        ways.Add(Way(Icon.Face, "Face", "Come close: mouth, eyes, smile"), 2);
        var label = Theme.Label("CHARACTER", Theme.FontLabel, 12, Theme.TextMuted);
        label.CharacterSpacing = 1;
        _introCharacter.HorizontalOptions = LayoutOptions.End;
        var characterHeader = new Grid { Children = { label, _introCharacter } };
        var picker = new Grid { ColumnSpacing = 10 };
        foreach (var model in MirrorModels.All)
        {
            picker.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            picker.Add(CharacterChip(model, 76), picker.ColumnDefinitions.Count - 1);
        }
        var privacy = new HorizontalStackLayout
        {
            Spacing = 6, HorizontalOptions = LayoutOptions.Center,
            Children = { Icons.Create(Icon.Private, Theme.TextMuted, 16), Theme.Label("Runs on this device. Nothing is recorded or sent.", Theme.FontBody, 12, Theme.TextMuted) }
        };
        ((View)privacy.Children[0]).VerticalOptions = LayoutOptions.Center;
        HighlightCharacter();
        var content = new Grid
        {
            RowSpacing = 22, Padding = new Thickness(20, 8, 20, 20), MaximumWidthRequest = 480,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        content.Add(back);
        content.Add(new VerticalStackLayout { Spacing = 14, Children = { hero, title, about } }, 0, 1);
        content.Add(ways, 0, 2);
        content.Add(new VerticalStackLayout { Spacing = 10, Children = { characterHeader, picker } }, 0, 3);
        content.Add(new VerticalStackLayout
        {
            Spacing = 12, Children = { new ActionButton("Start camera", Start, ActionStyle.Primary, Icon.Camera, 56), privacy }
        }, 0, 5);
        return new ScrollView { SafeAreaEdges = SafeAreaEdges.All, Content = content, Background = Theme.Glow(Theme.Canvas, .18, .8) };
    }

    private View Blocked()
    {
        var icon = new Border
        {
            WidthRequest = 96, HeightRequest = 96, HorizontalOptions = LayoutOptions.Center, BackgroundColor = Theme.Raised, Stroke = Theme.Line,
            StrokeThickness = 1, StrokeShape = Theme.Rounded(32), Shadow = Theme.EmberGlow(40, .2f), Content = Icons.Create(Icon.CameraOff, Theme.Ember, 36, 1.8, center: true)
        };
        var title = Theme.Label("Camera is off", Theme.FontDisplay, 26, Theme.Text);
        title.HorizontalTextAlignment = _blockedText.HorizontalTextAlignment = TextAlignment.Center;
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);
        var layout = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(4, 0, 4, 8), MaximumWidthRequest = 480 };
        layout.Add(new VerticalStackLayout { Spacing = 18, Padding = new Thickness(12, 0), VerticalOptions = LayoutOptions.Center, Children = { icon, title, _blockedText } });
        layout.Add(new VerticalStackLayout { Spacing = 10, Children = { _blockedAction, new ActionButton("Back", Close, ActionStyle.Secondary) } }, 0, 1);
        return layout;
    }

    // A strip of character thumbnails along the bottom; the chosen one is outlined.
    private ScrollView Carousel()
    {
        foreach (var model in MirrorModels.All) _characterRow.Add(CharacterChip(model, 64));
        HighlightCharacter();
        return new ScrollView { HorizontalScrollBarVisibility = ScrollBarVisibility.Never, VerticalScrollBarVisibility = ScrollBarVisibility.Never, Content = _characterRow };
    }

    private View CharacterChip(MirrorModel model, double size)
    {
        var chip = new Border
        {
            StrokeThickness = 2, BackgroundColor = Theme.Surface, HeightRequest = size,
            StrokeShape = Theme.Rounded(20), Content = new Image { Aspect = Aspect.AspectFill, Source = SceneCatalog.Character(model.File) }
        };
        if (size < 70) chip.WidthRequest = size;
        var item = new Tappable(chip, null, async () => await ChooseCharacterAsync(model));
        SemanticProperties.SetHint(item.Button, "Switches to this character");
        _chips.Add((model, chip, item.Button));
        return item;
    }

    private void HighlightCharacter()
    {
        _introCharacter.Text = _model.Name;
        _characterName.Text = _model.Name;
        _characterTracks.Text = char.ToUpperInvariant(_model.Tracks[0]) + _model.Tracks[1..];
        _hero.Source = SceneCatalog.Character(_model.File);
        foreach (var (model, chip, button) in _chips)
        {
            var chosen = model == _model;
            chip.Stroke = chosen ? Theme.Ember : Theme.Text.WithAlpha(.18f);
            chip.Shadow = chosen ? Theme.EmberGlow(18, .5f) : null!;
            ((View)chip.Parent!).Scale = chosen && chip.WidthRequest > 0 ? 1.08 : 1;
            SemanticProperties.SetDescription(button, chosen ? $"{model.Name}, selected" : model.Name);
        }
    }

    private View Guide()
    {
        // A standing figure shows how much of the person should be in frame.
        var silhouette = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(
                "M50,4 A13,13 0 1 1 49.9,4 Z M30,36 L70,36 Q80,36 82,46 L90,100 Q91,106 85,107 Q80,107 79,101 L72,60 L72,112 L68,190 " +
                "Q67,196 61,196 Q55,196 55,190 L52,124 L48,124 L45,190 Q45,196 39,196 Q33,196 32,190 L28,112 L28,60 L21,101 Q20,107 15,107 " +
                "Q9,106 10,100 L18,46 Q20,36 30,36 Z")!,
            Aspect = Stretch.Uniform, Fill = Theme.Ember.WithAlpha(.08f), Stroke = Theme.Ember, StrokeThickness = 2.5,
            StrokeDashArray = [3, 2.5], Margin = new Thickness(0, 128, 0, 104), InputTransparent = true, Shadow = Theme.EmberGlow(10, .55f)
        };
        SemanticProperties.SetDescription(silhouette, "Outline showing how to stand in the frame");
        var dot = new Border
        {
            WidthRequest = 10, HeightRequest = 10, StrokeThickness = 0, StrokeShape = new Ellipse(), BackgroundColor = Theme.Ember,
            Shadow = Theme.EmberGlow(10, 1), VerticalOptions = LayoutOptions.Center
        };
        return new Grid { InputTransparent = true, Children = { silhouette, Hint(dot, _guideText, top: true) } };
    }

    private static View Hint(View icon, string text, bool top) => Hint(icon, Theme.Label(text, Theme.FontBody, 14, Theme.Text), top);

    private static View Hint(View icon, Label text, bool top)
    {
        icon.VerticalOptions = text.VerticalOptions = LayoutOptions.Center;
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        row.Add(icon);
        row.Add(text, 1);
        return new Border
        {
            InputTransparent = true, Padding = new Thickness(16, 12), Margin = new Thickness(8, top ? 56 : 0, 8, 0), MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = top ? LayoutOptions.Start : LayoutOptions.End,
            BackgroundColor = Theme.OverlayStrong, Stroke = Theme.Ember.WithAlpha(.35f), StrokeThickness = 1, StrokeShape = Theme.Rounded(18), Content = row
        };
    }

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

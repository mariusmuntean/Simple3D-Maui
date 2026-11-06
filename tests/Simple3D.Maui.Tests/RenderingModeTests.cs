using Microsoft.Maui.Controls;
using Simple3D.Maui;
using Simple3D.Core;
internal static class RenderingModeTests
{
    internal static void Run()
    {
        var view = new SceneView();
        if (view.RenderingMode != RenderingMode.Auto) throw new Exception("default mode");
        view.ReportHardwareFailure(new InvalidOperationException("test unavailable"));
        if (view.ActiveRenderingBackend != RenderingBackend.Software || !view.RenderingDiagnostic!.Contains("test unavailable"))
            throw new Exception("Auto must expose fallback reason");
        view.RenderingMode = RenderingMode.Hardware;
        view.ReportHardwareFailure(new InvalidOperationException("test unavailable"));
        if (view.ActiveRenderingBackend != RenderingBackend.None) throw new Exception("Hardware silently fell back");
        view.RenderingMode = RenderingMode.Software;
        if (view.ActiveRenderingBackend != RenderingBackend.Software) throw new Exception("Software mode");
        try { view.RenderingMode = (RenderingMode)42; throw new Exception("invalid mode accepted"); }
        catch (ArgumentException) { }
        var tall = new SceneView { Scene = new Scene().Add(Shape.Box().At(.5f, 0, 0)), Camera = new Camera(5, 0, 0) };
        tall.HardwareFrameSubmitted("test", 0, 200, 500);
        var reference = tall.CaptureFrame(200, 500);
        for (var y = 101; y < 400; y += 17)
            for (var x = 11; x < 190; x += 11)
                if (!ReferenceEquals(tall.PickAt((x + .5) * 2, (y + .5) * 2, 400, 1000), reference.Pick(x, y)))
                    throw new Exception("hardware picking distorted a tall viewport");
        if (tall.PickAt(1, 1, double.PositiveInfinity, 10) is not null) throw new Exception("invalid layout size");
        // Scene content errors are diagnostics, not hardware failures: no fallback, no sticky flag.
        var failures = 0;
        tall.RenderingFailed += (_, _) => failures++;
        var valid = tall.Scene;
        var overBudget = new Scene();
        for (var i = 0; i <= DepthRenderer.MaximumTriangles / Shape.Box().TriangleCount; i++) overBudget.Add(Shape.Box());
        tall.Scene = overBudget;
        if (tall.PickAt(200, 500, 400, 1000) is not null) throw new Exception("over-budget scene picked");
        if (tall.ActiveRenderingBackend != RenderingBackend.Hardware || !tall.CanRenderHardware || failures != 0 ||
            tall.RenderingDiagnostic?.Contains("budget") != true)
            throw new Exception("scene error must be reported without disabling hardware");
        tall.Scene = valid;
        tall.HardwareFrameSubmitted("test", 0, 200, 500);
        var hits = 0;
        for (var y = 101; y < 400; y += 17)
            for (var x = 11; x < 190; x += 11)
                if (reference.Pick(x, y) is { } expected && ReferenceEquals(tall.PickAt((x + .5) * 2, (y + .5) * 2, 400, 1000), expected)) hits++;
        if (tall.RenderingDiagnostic != "test" || hits == 0) throw new Exception("hardware must recover after scene error");
        Console.WriteLine("PASS rendering mode fallback, strict hardware policy and tall-view picking");
        RunOverlayVisibility();
        RunGallery();
    }

    // The Skia overlay is a full-surface CPU raster layer: in hardware mode it is composited only
    // while the scene has labels; before the first hardware frame it stays as the opaque placeholder.
    private static void RunOverlayVisibility()
    {
        var view = new SceneView { Scene = new Scene().Add(Shape.Box()) };
        if (!view.PresentWithOverlay) throw new Exception("overlay must cover the native view before the first hardware frame");
        var presented = 0;
        view.FrameSubmitted += (_, _) => presented++;
        view.HardwareFrameSubmitted("test", 2.5, 100, 80);
        view.RaiseFrameSubmitted(); // Native views raise it after their failure handling.
        if (presented != 1 || view.LastFrameMilliseconds != 2.5 || view.LastFrameWidth != 100 || view.LastFrameHeight != 80)
            throw new Exception("a presented hardware frame must raise FrameSubmitted with its statistics");
        // A throwing subscriber is the app's bug, not a GPU failure: recording a frame never invokes subscribers.
        view.FrameSubmitted += (_, _) => throw new InvalidOperationException("subscriber");
        view.HardwareFrameSubmitted("test", 1, 100, 80);
        if (view.PresentWithOverlay) throw new Exception("overlay must be hidden in hardware mode without labels");
        view.Scene.AddLabel(new WorldLabel("label", System.Numerics.Vector3.Zero));
        if (!view.PresentWithOverlay) throw new Exception("adding a label must show the overlay");
        view.Scene = new Scene().Add(Shape.Box());
        if (view.PresentWithOverlay) throw new Exception("removing labels must hide the overlay");
        view.RenderingMode = RenderingMode.Software;
        if (!view.PresentWithOverlay) throw new Exception("software mode paints through the overlay");
        // Leaving hardware must not keep the last prepared scene (and its textures) alive.
        view.RenderingMode = RenderingMode.Hardware;
        var prepared = view.PrepareHardwareFrame(64, 64) ?? throw new Exception("hardware frame not prepared");
        if (prepared.Geometry.Items.Count == 0) throw new Exception("prepared frame has no items");
        view.RenderingMode = RenderingMode.Software;
        if (prepared.Geometry.Items.Count != 0) throw new Exception("switching to software must release the prepared hardware scene");
        Console.WriteLine("PASS overlay composited only for labels in hardware mode; leaving hardware releases the prepared scene");
    }

    // The gallery must keep working with every renderer: the renderer sheet drives the view's mode, its status follows the
    // active backend, the chip and fallback banner report a fallback, and scenes, capture and picking keep working.
    private static void RunGallery()
    {
        var gallery = DemoAppearanceTests.Gallery();
        var cards = Descendants(gallery.Content).OfType<Button>().Where(b => Simple3D.Shared.DemoScenes.All.Any(s => s.Name == b.Text)).ToArray();
        if (cards.Length != Simple3D.Shared.DemoScenes.All.Count) throw new Exception("gallery scene cards missing");
        var opened = new List<SceneView>();
        try
        {
            Simple3D.Demo.SceneViewerPage page = null!;
            SceneView view = null!;
            Element[] all = [];
            void Select(RenderingMode mode) =>
                ((IButtonController)all.OfType<Button>().Single(b => b.AutomationId == $"RenderingMode{mode}")).SendClicked();
            Label Status() => all.OfType<Label>().Single(l => l.AutomationId == "RenderingBackend");
            foreach (var mode in new[] { RenderingMode.Software, RenderingMode.Hardware, RenderingMode.Auto, RenderingMode.Software })
            {
                foreach (var card in cards)
                {
                    (page, view, _) = DemoAppearanceTests.Viewer(gallery, card.Text);
                    opened.Add(view);
                    all = Descendants(page.Content).ToArray();
                    Select(mode);
                    if (view.RenderingMode != mode) throw new Exception($"renderer sheet did not select {mode}");
                    if (Status().Text != view.RenderingDiagnostic) throw new Exception($"renderer status stale after {mode}");
                    var frame = view.CaptureFrame(96, 64); // Always a software snapshot, in every mode.
                    var hit = false;
                    for (var y = 0; y < 64 && !hit; y += 4) for (var x = 0; x < 96 && !hit; x += 4) hit = frame.Pick(x, y) is not null;
                    if (!hit) throw new Exception($"{card.Text} rendered nothing in {mode}");
                }
            }
            // Without a native surface (headless), Software is presented and taps pick in Auto/Software.
            if (view.ActiveRenderingBackend != RenderingBackend.Software) throw new Exception("software mode not active");
            Select(RenderingMode.Auto);
            view.ReportHardwareFailure(new InvalidOperationException("simulated GPU loss"));
            var banner = all.OfType<Simple3D.Demo.FallbackBanner>().Single();
            var chip = all.OfType<Simple3D.Demo.RendererChip>().Single();
            if (view.ActiveRenderingBackend != RenderingBackend.Software || !Status().Text.Contains("simulated GPU loss") || !banner.IsVisible ||
                SemanticProperties.GetDescription(chip.Button) != "Renderer: Auto, drawing in software")
                throw new Exception("gallery must show the fallback and its reason");
            ((IButtonController)all.OfType<Button>().Single(b => b.Text == "Try GPU again")).SendClicked();
            if (view.RenderingMode != RenderingMode.Auto || !view.CanRenderHardware || Status().Text?.Contains("simulated GPU loss") == true)
                throw new Exception("Try GPU again must retry the GPU in Auto");
            view.ReportHardwareFailure(new InvalidOperationException("simulated GPU loss"));
            Select(RenderingMode.Hardware);
            if (!view.CanRenderHardware || Status().Text?.Contains("simulated GPU loss") == true) throw new Exception("changing mode must retry hardware");
            Select(RenderingMode.Software);
            if (view.ActiveRenderingBackend != RenderingBackend.Software) throw new Exception("gallery cannot return to software");
            Console.WriteLine("PASS gallery renderer sheet, status, fallback banner, scenes, capture and failure recovery in every mode");
        }
        finally
        {
            foreach (var view in opened) view.ReleaseRenderResources();
        }
    }

    private static IEnumerable<Element> Descendants(Element? root) => DemoAppearanceTests.Descendants(root);
}

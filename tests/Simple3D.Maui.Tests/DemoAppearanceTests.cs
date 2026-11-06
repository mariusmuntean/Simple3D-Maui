using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Simple3D.Core;
using Simple3D.Demo;
using Simple3D.Maui;
using Simple3D.Shared;

internal static class DemoAppearanceTests
{
    public static readonly (string Name, Action Run)[] Cases =
    [
        ("surface shading comparison changes pixels and reuses prepared geometry", () =>
        {
            var (viewer, view, _) = Viewer(Gallery(), "Surface");
            try
            {
                var control = Segment(viewer, "SurfaceShading");
                Assert(Shown(control), "surface shading control is hidden");
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var smooth = view.Scene.Shapes[0];
                var frame = view.CaptureFrame(320, 240);
                var point = Enumerable.Range(0, frame.Width * frame.Height)
                    .First(i => ReferenceEquals(frame.Pick(i % frame.Width, i / frame.Width), smooth));
                view.SelectAt(point % frame.Width, point / frame.Width, frame.Width, frame.Height);
                var camera = view.Camera;
                Toggle(control);
                Assert(SemanticProperties.GetDescription(control.Buttons[1]) == "Shading: flat, selected", "accessible shading state stayed smooth");
                var flat = view.Scene.Shapes[0];
                Assert(flat.Geometry?.Normals is null && smooth.Geometry?.Normals is not null,
                    "comparison did not switch normal attributes");
                Assert(Changed(original, view.CaptureFrame(320, 240).Pixels.ToArray()) > 300,
                    "flat comparison is not visibly different");
                Assert(ReferenceEquals(view.SelectedShape, flat), "shading switch lost selected surface");
                Assert(ReferenceEquals(view.Camera, camera), "shading switch reset the orbit camera");
                Toggle(control);
                Assert(ReferenceEquals(smooth.Geometry, view.Scene.Shapes[0].Geometry), "smooth mesh was rebuilt");
                Toggle(control);
                Assert(ReferenceEquals(flat.Geometry, view.Scene.Shapes[0].Geometry), "flat mesh was rebuilt");
                viewer.Viewer.StopAnimation();
            }
            finally { view.ReleaseRenderResources(); }
        }),
        ("surface light control redraws without replacing geometry or camera", () =>
        {
            var (viewer, view, _) = Viewer(Gallery(), "Surface");
            try
            {
                var control = Segment(viewer, "SurfaceLight");
                Assert(Shown(control) && control.Buttons.All(b => !string.IsNullOrWhiteSpace(SemanticProperties.GetHint(b))),
                    "light control is hidden or lacks its accessible explanation");
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var shapes = view.Scene.Shapes.ToArray();
                var light = view.Scene.Light;
                var camera = view.Camera;
                Toggle(control);
                Assert(SemanticProperties.GetDescription(control.Buttons[0]) == "Light: from left, selected", "accessible light state stayed on the right");
                Assert(view.Scene.Light?.Direction.X > 0 && light?.Direction.X < 0, "light did not cross the surface");
                Assert(Changed(original, view.CaptureFrame(320, 240).Pixels.ToArray()) > 1000,
                    "light change did not affect visible pixels");
                Assert(view.Scene.Shapes.SequenceEqual(shapes) && ReferenceEquals(view.Camera, camera),
                    "light change replaced geometry or reset the camera");
                Toggle(control);
                Assert(view.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "returning the light changed the initial appearance");
            }
            finally { view.ReleaseRenderResources(); }
        }),
        ("gallery reset restores surface appearance and other scenes hide comparison controls", () =>
        {
            var gallery = Gallery();
            var (viewer, view, buttons) = Viewer(gallery, "Surface");
            SceneView? other = null;
            try
            {
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var shading = Segment(viewer, "SurfaceShading");
                var light = Segment(viewer, "SurfaceLight");
                Toggle(shading); Toggle(light); Click(Described(buttons, "Reset scene"));
                Assert(view.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "Reset retained modified appearance");
                Assert(shading.Selected == 0 && light.Selected == 1, "Reset retained stale control states");
                Toggle(shading);
                var equipment = Viewer(gallery, "Equipment");
                other = equipment.View;
                Assert(!Shown(Segment(equipment.Page, "SurfaceShading")) && !Shown(Segment(equipment.Page, "SurfaceLight")),
                    "surface controls leaked into Equipment");
                var surface = Viewer(gallery, "Surface");
                Assert(surface.View.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "scene return retained modified appearance");
                surface.View.ReleaseRenderResources();
            }
            finally { view.ReleaseRenderResources(); other?.ReleaseRenderResources(); }
        }),
        ("equipment imported textured controller occupies a readable part of the initial frame", () =>
        {
            var sample = DemoScenes.Equipment();
            var controller = sample.Scene.Shapes[0].Children.Single(shape => shape.Name == "Imported controller");
            Assert(controller.Geometry?.TextureCoordinates is not null && controller.Material.Texture is not null,
                "controller lost imported UVs or texture");
            Assert(controller.Geometry!.Vertices.Select(vertex => vertex.Z).Distinct().Count() > 1,
                "import example is still a planar decoration");
            var frame = new DepthRenderer().Render(sample.Scene, sample.Camera, 320, 240);
            var count = Enumerable.Range(0, 320 * 240).Count(i => ReferenceEquals(frame.Pick(i % 320, i / 320), controller));
            Assert(count > 1500, $"textured imported controller is too small: {count} pixels");
            var geometry = controller.Geometry;
            sample.Animate(1);
            Assert(ReferenceEquals(geometry, sample.Scene.Shapes[0].Children.Single(shape => shape.Name == "Imported controller").Geometry),
                "animation rebuilt imported geometry");
        }),
        ("surface comparisons preserve running animation and its current marker position", () =>
        {
            var previous = DispatcherProvider.Current;
            var dispatcher = new TestDispatcher();
            DispatcherProvider.SetCurrent(dispatcher);
            SceneView? view = null;
            SceneViewerPage? page = null;
            try
            {
                Button[] buttons;
                (page, view, buttons) = Viewer(Gallery(), "Surface");
                var animate = buttons.Single(button => button.Text == "Animate");
                Click(animate);
                dispatcher.Timer.DeliverTick();
                var marker = view.Scene.Shapes[1];
                Toggle(Segment(page, "SurfaceShading"));
                Toggle(Segment(page, "SurfaceLight"));
                Assert(animate.Text == "Pause" && dispatcher.Timer.IsRunning && view.MaximumRenderDimension == 768,
                    "appearance comparison interrupted running animation");
                Assert(ReferenceEquals(marker, view.Scene.Shapes[1]), "appearance comparison rewound the sample marker");
                dispatcher.Timer.DeliverTick();
                Assert(!ReferenceEquals(marker, view.Scene.Shapes[1]), "animation stopped advancing after appearance changes");
            }
            finally
            {
                page?.Viewer.StopAnimation();
                view?.ReleaseRenderResources();
                DispatcherProvider.SetCurrent(previous);
            }
        })
    ];

    /// <summary>A gallery whose scene cards hand their page to <see cref="Opened"/> instead of navigating.</summary>
    internal static GalleryPage Gallery()
    {
        var page = new GalleryPage();
        page.Navigate = opened => { Opened = (SceneViewerPage)opened; return Task.CompletedTask; };
        return page;
    }

    private static SceneViewerPage? Opened;

    /// <summary>Taps the scene's card and returns the viewer page it opened.</summary>
    internal static (SceneViewerPage Page, SceneView View, Button[] Buttons) Viewer(GalleryPage gallery, string scene)
    {
        Opened = null;
        Click(Descendants(gallery.Content).OfType<Button>().Single(button => button.Text == scene));
        var page = Opened ?? throw new InvalidOperationException($"{scene} card did not open a viewer");
        var all = Descendants(page.Content).ToArray();
        return (page, all.OfType<SceneView>().Single(), all.OfType<Button>().ToArray());
    }

    private static Segmented Segment(Page page, string id) => Descendants(page).OfType<Segmented>().Single(s => s.AutomationId == id);
    private static bool Shown(VisualElement view)
    {
        for (Element? e = view; e is not null; e = e.Parent) if (e is VisualElement { IsVisible: false }) return false;
        return true;
    }
    private static void Toggle(Segmented control) => Click(control.Buttons[1 - control.Selected]);
    internal static Button Described(IEnumerable<Button> buttons, string description) =>
        buttons.Single(button => SemanticProperties.GetDescription(button) == description);
    internal static void Click(Button button) => ((IButtonController)button).SendClicked();

    internal static IEnumerable<Element> Descendants(Element? root)
    {
        if (root is null) yield break;
        yield return root;
        IEnumerable<Element> children = root switch
        {
            SceneView => [],
            Layout layout => layout.Children.OfType<Element>(),
            ScrollView scroll => scroll.Content is null ? [] : [scroll.Content],
            Border border => border.Content is null ? [] : [border.Content],
            ContentView content => content.Content is null ? [] : [content.Content],
            ContentPage page => page.Content is null ? [] : [page.Content],
            _ => []
        };
        foreach (var child in children) foreach (var item in Descendants(child)) yield return item;
    }

    private static int Changed(uint[] before, uint[] after) => before.Zip(after).Count(pair => pair.First != pair.Second);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

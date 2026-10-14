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
            var (page, view, buttons) = Gallery();
            try
            {
                Click(buttons, "Surface");
                var control = buttons.Single(button => button.AutomationId == "SurfaceShading");
                Assert(control.IsVisible, "surface shading control is hidden");
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var smooth = view.Scene.Shapes[0];
                var frame = view.CaptureFrame(320, 240);
                var point = Enumerable.Range(0, frame.Width * frame.Height)
                    .First(i => ReferenceEquals(frame.Pick(i % frame.Width, i / frame.Width), smooth));
                view.SelectAt(point % frame.Width, point / frame.Width, frame.Width, frame.Height);
                var camera = view.Camera;
                Click(control);
                Assert(SemanticProperties.GetDescription(control) == "Shading: flat", "accessible shading state stayed smooth");
                var flat = view.Scene.Shapes[0];
                Assert(flat.Geometry?.Normals is null && smooth.Geometry?.Normals is not null,
                    "comparison did not switch normal attributes");
                Assert(Changed(original, view.CaptureFrame(320, 240).Pixels.ToArray()) > 300,
                    "flat comparison is not visibly different");
                Assert(ReferenceEquals(view.SelectedShape, flat), "shading switch lost selected surface");
                Assert(ReferenceEquals(view.Camera, camera), "shading switch reset the orbit camera");
                Click(control);
                Assert(ReferenceEquals(smooth.Geometry, view.Scene.Shapes[0].Geometry), "smooth mesh was rebuilt");
                Click(control);
                Assert(ReferenceEquals(flat.Geometry, view.Scene.Shapes[0].Geometry), "flat mesh was rebuilt");
                page.StopAnimation();
            }
            finally { view.ReleaseRenderResources(); }
        }),
        ("surface light control redraws without replacing geometry or camera", () =>
        {
            var (_, view, buttons) = Gallery();
            try
            {
                Click(buttons, "Surface");
                var control = buttons.Single(button => button.AutomationId == "SurfaceLight");
                Assert(control.IsVisible && !string.IsNullOrWhiteSpace(SemanticProperties.GetDescription(control)),
                    "light control is hidden or lacks its accessible explanation");
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var shapes = view.Scene.Shapes.ToArray();
                var light = view.Scene.Light;
                var camera = view.Camera;
                Click(control);
                Assert(SemanticProperties.GetDescription(control) == "Light: from left", "accessible light state stayed on the right");
                Assert(view.Scene.Light?.Direction.X > 0 && light?.Direction.X < 0, "light did not cross the surface");
                Assert(Changed(original, view.CaptureFrame(320, 240).Pixels.ToArray()) > 1000,
                    "light change did not affect visible pixels");
                Assert(view.Scene.Shapes.SequenceEqual(shapes) && ReferenceEquals(view.Camera, camera),
                    "light change replaced geometry or reset the camera");
                Click(control);
                Assert(view.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "returning the light changed the initial appearance");
            }
            finally { view.ReleaseRenderResources(); }
        }),
        ("gallery reset restores surface appearance and other scenes hide comparison controls", () =>
        {
            var (_, view, buttons) = Gallery();
            try
            {
                Click(buttons, "Surface");
                var original = view.CaptureFrame(320, 240).Pixels.ToArray();
                var shading = buttons.Single(button => button.AutomationId == "SurfaceShading");
                var light = buttons.Single(button => button.AutomationId == "SurfaceLight");
                Click(shading); Click(light); Click(buttons, "Reset");
                Assert(view.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "Reset retained modified appearance");
                Assert(shading.Text!.Contains("smooth") && light.Text == "Light: from right", "Reset retained stale control labels");
                Click(buttons, "Equipment");
                Assert(!shading.IsVisible && !light.IsVisible, "surface controls leaked into Equipment");
                Click(buttons, "Surface");
                Assert(view.CaptureFrame(320, 240).Pixels.Span.SequenceEqual(original), "scene return retained modified appearance");
            }
            finally { view.ReleaseRenderResources(); }
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
            GalleryPage? page = null;
            try
            {
                Button[] buttons;
                (page, view, buttons) = Gallery();
                Click(buttons, "Surface");
                var animate = buttons.Single(button => button.Text == "Animate");
                Click(animate);
                dispatcher.Timer.DeliverTick();
                var marker = view.Scene.Shapes[1];
                Click(buttons.Single(button => button.AutomationId == "SurfaceShading"));
                Click(buttons.Single(button => button.AutomationId == "SurfaceLight"));
                Assert(animate.Text == "Pause" && dispatcher.Timer.IsRunning && view.MaximumRenderDimension == 768,
                    "appearance comparison interrupted running animation");
                Assert(ReferenceEquals(marker, view.Scene.Shapes[1]), "appearance comparison rewound the sample marker");
                dispatcher.Timer.DeliverTick();
                Assert(!ReferenceEquals(marker, view.Scene.Shapes[1]), "animation stopped advancing after appearance changes");
            }
            finally
            {
                page?.StopAnimation();
                view?.ReleaseRenderResources();
                DispatcherProvider.SetCurrent(previous);
            }
        })
    ];

    private static (GalleryPage Page, SceneView View, Button[] Buttons) Gallery()
    {
        var page = new GalleryPage();
        var grid = (Grid)page.Content;
        var view = grid.Children.OfType<Border>().Single().Content as SceneView
            ?? throw new InvalidOperationException("gallery scene view is missing");
        var header = grid.Children.OfType<VerticalStackLayout>().Single();
        var footer = (VerticalStackLayout)grid.Children.OfType<ScrollView>().Single().Content;
        var scrollers = header.Children.OfType<ScrollView>().Concat(footer.Children.OfType<ScrollView>());
        Assert(scrollers.All(scroller => scroller.Orientation == ScrollOrientation.Horizontal),
            "gallery controls are unreachable in a narrow layout");
        var buttons = scrollers.SelectMany(scroller => ((HorizontalStackLayout)scroller.Content).Children.OfType<Button>()).ToArray();
        return (page, view, buttons);
    }
    private static void Click(Button button) => ((IButtonController)button).SendClicked();
    private static void Click(Button[] buttons, string text) => Click(buttons.Single(button => button.Text == text));
    private static int Changed(uint[] before, uint[] after) => before.Zip(after).Count(pair => pair.First != pair.Second);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

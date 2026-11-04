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
        tall.HardwareFramePresented("test", 0);
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
        tall.HardwareFramePresented("test", 0);
        var hits = 0;
        for (var y = 101; y < 400; y += 17)
            for (var x = 11; x < 190; x += 11)
                if (reference.Pick(x, y) is { } expected && ReferenceEquals(tall.PickAt((x + .5) * 2, (y + .5) * 2, 400, 1000), expected)) hits++;
        if (tall.RenderingDiagnostic != "test" || hits == 0) throw new Exception("hardware must recover after scene error");
        Console.WriteLine("PASS rendering mode fallback, strict hardware policy and tall-view picking");
    }
}

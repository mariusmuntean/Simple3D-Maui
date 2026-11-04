namespace Simple3D.Maui;

/// <summary>Selects how SceneView presents its scene. CaptureFrame always produces a software snapshot.</summary>
public enum RenderingMode
{
    /// <summary>Prefer the native GPU, with software fallback if initialization or rendering fails.</summary>
    Auto,
    /// <summary>Use the portable software renderer.</summary>
    Software,
    /// <summary>Require the native GPU. Failure is reported without silently rendering in software.</summary>
    Hardware
}

/// <summary>The backend currently presenting frames, separate from the requested mode.</summary>
public enum RenderingBackend
{
    /// <summary>No frame is available yet, or required hardware failed.</summary>
    None,
    /// <summary>CPU rasterization with SkiaSharp presentation.</summary>
    Software,
    /// <summary>Native Metal or OpenGL ES 3 triangle rendering.</summary>
    Hardware
}

/// <summary>Describes a native rendering failure and whether software fallback was selected.</summary>
public sealed class RenderingFailureEventArgs(string message, bool fellBackToSoftware) : EventArgs
{
    /// <summary>Diagnostic from native initialization or rendering.</summary>
    public string Message { get; } = message;
    /// <summary>True when Auto selected software after this failure.</summary>
    public bool FellBackToSoftware { get; } = fellBackToSoftware;
}

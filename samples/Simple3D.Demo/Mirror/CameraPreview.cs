using Simple3D.Shared.Mirror;

namespace Simple3D.Demo.Mirror;

/// <summary>Aspect-fill preview. Set Source before starting it; stop the source when leaving the page.</summary>
public sealed class CameraPreview : View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(IPoseSource), typeof(CameraPreview));

    public IPoseSource? Source
    {
        get => (IPoseSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
}

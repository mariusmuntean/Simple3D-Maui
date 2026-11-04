using Microsoft.Maui.Controls;
namespace Simple3D.Maui;

internal sealed class GpuSurface(SceneView owner) : View
{
    internal SceneView Owner { get; } = owner;
    internal void RequestRender() => Handler?.Invoke("Render", null);
    internal void Release()
    {
        Handler?.DisconnectHandler();
        Handler = null;
    }
}

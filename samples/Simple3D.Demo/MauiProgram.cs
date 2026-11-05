using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Simple3D.Demo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseSkiaSharp()
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<Mirror.CameraPreview, Mirror.CameraPreviewHandler>());
        return builder.Build();
    }
}

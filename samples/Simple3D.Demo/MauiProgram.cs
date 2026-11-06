using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Simple3D.Demo;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                // OFL-licensed, see Resources/Fonts/OFL.txt.
                fonts.AddFont("Inter-Regular.ttf", Theme.FontBody);
                fonts.AddFont("Inter-SemiBold.ttf", Theme.FontLabel);
                fonts.AddFont("SpaceGrotesk-SemiBold.ttf", Theme.FontDisplay);
                fonts.AddFont("JetBrainsMono-Regular.ttf", Theme.FontMono);
                fonts.AddFont("JetBrainsMono-Medium.ttf", Theme.FontMonoMedium);
            })
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<Mirror.CameraPreview, Mirror.CameraPreviewHandler>());
        return builder.Build();
    }
}

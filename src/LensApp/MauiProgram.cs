using LensApp.Controls;
using LensApp.Handlers;
using LensApp.Services;
using LensApp.ViewModels;
using Microsoft.Extensions.Logging;

namespace LensApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<CameraPreview, CameraPreviewHandler>();
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IRalMatcher, RalMatcher>();
        builder.Services.AddSingleton<WhiteBalanceService>();
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}

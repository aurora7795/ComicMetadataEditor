using System;
using Avalonia;
using InkTag.Core.Logging;
using Velopack;

namespace InkTag.Gui;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppLogger.Initialize();
            var settingsService = new InkTag.Core.Configuration.AppSettingsService();
            AppLogger.IsDebugEnabled = settingsService.Settings.EnableDebugLogging;

            VelopackApp.Build().Run();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Fatal GUI startup exception: {ex}");
            Console.Error.WriteLine($"Fatal GUI startup exception: {ex}");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

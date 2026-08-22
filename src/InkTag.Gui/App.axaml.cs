using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using InkTag.Core.Configuration;

namespace InkTag.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        var settingsService = new AppSettingsService();
        ApplyTheme(settingsService.Settings.ThemeMode);
    }

    public static void ApplyTheme(AppThemeMode mode)
    {
        if (Current != null)
        {
            Current.RequestedThemeVariant = mode switch
            {
                AppThemeMode.Light => ThemeVariant.Light,
                AppThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void About_Click(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var aboutWindow = new Views.AboutWindow();
            aboutWindow.ShowDialog(desktop.MainWindow);
        }
    }

    private void Settings_Click(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var settingsWindow = new Views.SettingsWindow();
            settingsWindow.ShowDialog(desktop.MainWindow);
        }
    }
}

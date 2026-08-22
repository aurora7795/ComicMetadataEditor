using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core.Configuration;
using InkTag.Gui.Services;

namespace InkTag.Gui.Views;

public partial class ApiKeyRequiredWindow : Window
{
    public const string ComicVineApiUrl = "https://comicvine.gamespot.com/api/";
    public bool KeyConfigured { get; private set; }

    public ApiKeyRequiredWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> EnsureApiKeyConfiguredAsync(Window? owner)
    {
        var settingsService = new AppSettingsService();
        if (!string.IsNullOrWhiteSpace(settingsService.GetEffectiveComicVineApiKey()))
        {
            return true;
        }

        var dialog = new ApiKeyRequiredWindow();
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        return dialog.KeyConfigured;
    }

    private void OpenApiUrl_Click(object? sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrlInBrowser(ComicVineApiUrl);
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(initialTabIndex: 1, focusApiKey: true);
        await settingsWindow.ShowDialog(this);

        var settingsService = new AppSettingsService();
        if (!string.IsNullOrWhiteSpace(settingsService.GetEffectiveComicVineApiKey()))
        {
            KeyConfigured = true;
            Close();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

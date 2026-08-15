using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsService _settingsService;

    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = new AppSettingsService();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var settings = _settingsService.Settings;
        ApiKeyTextBox.Text = settings.ComicVineApiKey;
        MergePolicyComboBox.SelectedIndex = settings.DefaultMergeMode == ScrapeMergeMode.OverwriteAll ? 1 : 0;
    }

    private async void TestApiKey_Click(object? sender, RoutedEventArgs e)
    {
        string key = ApiKeyTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(key))
        {
            StatusTextBlock.Text = "❌ Please enter an API key to test.";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            return;
        }

        StatusTextBlock.Text = "⏳ Validating API key with ComicVine...";
        StatusTextBlock.Foreground = Avalonia.Media.Brushes.Yellow;

        try
        {
            var provider = new ComicVineProvider();
            var query = new ComicSearchQuery { Series = "Spider-Man", IssueNumber = "1" };
            await provider.SearchAsync(query, key);

            StatusTextBlock.Text = "✅ Connection successful! API key is valid.";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Validation failed: {ex.Message}";
            StatusTextBlock.Foreground = Avalonia.Media.Brushes.OrangeRed;
        }
    }

    private void ClearCache_Click(object? sender, RoutedEventArgs e)
    {
        var cache = new ScraperCacheService();
        cache.Clear();
        StatusTextBlock.Text = "🗑️ Local response cache cleared.";
        StatusTextBlock.Foreground = Avalonia.Media.Brushes.Cyan;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Settings;
        settings.ComicVineApiKey = ApiKeyTextBox.Text?.Trim() ?? "";
        settings.DefaultMergeMode = MergePolicyComboBox.SelectedIndex == 1 
            ? ScrapeMergeMode.OverwriteAll 
            : ScrapeMergeMode.FillMissingOnly;

        _settingsService.SaveSettings(settings);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

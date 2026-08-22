using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using InkTag.Gui.Services;

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
        VisualMatchCheckBox.IsChecked = settings.AutoApplyOnVisualMatch;
        VisualThresholdTextBox.Text = ((int)(settings.VisualMatchConfidenceThreshold * 100)).ToString();
        DebugLoggingCheckBox.IsChecked = settings.EnableDebugLogging;
        ClearLegacyZipCommentsCheckBox.IsChecked = settings.ClearLegacyZipCommentsOnUpgrade;

        // Komga
        KomgaUrlTextBox.Text = settings.KomgaServerUrl;
        KomgaApiKeyTextBox.Text = settings.KomgaApiKey;
        KomgaUserTextBox.Text = settings.KomgaUser;
        KomgaPasswordTextBox.Text = settings.KomgaPassword;
        KomgaAutoSyncCheckBox.IsChecked = settings.KomgaAutoSyncOnSave;
        KomgaStoryArcsCheckBox.IsChecked = settings.KomgaSyncStoryArcsToCollections;

        if (settings.KomgaPathMappings.Count > 0)
        {
            KomgaLocalPrefixTextBox.Text = settings.KomgaPathMappings[0].LocalPrefix;
            KomgaServerPrefixTextBox.Text = settings.KomgaPathMappings[0].ServerPrefix;
        }
    }

    private async void TestKomga_Click(object? sender, RoutedEventArgs e)
    {
        string url = KomgaUrlTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(url))
        {
            KomgaStatusTextBlock.Text = "❌ Please enter a Komga Server URL (e.g. http://localhost:25600).";
            KomgaStatusTextBlock.Foreground = Avalonia.Media.Brushes.Red;
            return;
        }

        KomgaStatusTextBlock.Text = "⏳ Testing connection with Komga...";
        KomgaStatusTextBlock.Foreground = Avalonia.Media.Brushes.Yellow;

        try
        {
            using var client = new InkTag.Core.Komga.KomgaClient(
                url,
                KomgaApiKeyTextBox.Text?.Trim(),
                KomgaUserTextBox.Text?.Trim(),
                KomgaPasswordTextBox.Text?.Trim());

            bool success = await client.TestConnectionAsync();
            if (success)
            {
                var libraries = await client.GetLibrariesAsync();
                KomgaStatusTextBlock.Text = $"✅ Connection successful! Discovered {libraries.Count} Komga libraries.";
                KomgaStatusTextBlock.Foreground = Avalonia.Media.Brushes.LightGreen;
            }
            else
            {
                KomgaStatusTextBlock.Text = "❌ Server reached but authentication failed. Check API Key or Credentials.";
                KomgaStatusTextBlock.Foreground = Avalonia.Media.Brushes.OrangeRed;
            }
        }
        catch (Exception ex)
        {
            KomgaStatusTextBlock.Text = $"❌ Connection failed: {ex.Message}";
            KomgaStatusTextBlock.Foreground = Avalonia.Media.Brushes.OrangeRed;
        }
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
        settings.AutoApplyOnVisualMatch = VisualMatchCheckBox.IsChecked == true;
        settings.EnableDebugLogging = DebugLoggingCheckBox.IsChecked == true;
        settings.ClearLegacyZipCommentsOnUpgrade = ClearLegacyZipCommentsCheckBox.IsChecked == true;
        if (double.TryParse(VisualThresholdTextBox.Text, out double thresh))
        {
            settings.VisualMatchConfidenceThreshold = Math.Clamp(thresh > 1 ? thresh / 100.0 : thresh, 0.50, 1.0);
        }

        // Save Komga
        settings.KomgaServerUrl = KomgaUrlTextBox.Text?.Trim() ?? "";
        settings.KomgaApiKey = KomgaApiKeyTextBox.Text?.Trim() ?? "";
        settings.KomgaUser = KomgaUserTextBox.Text?.Trim() ?? "";
        settings.KomgaPassword = KomgaPasswordTextBox.Text?.Trim() ?? "";
        settings.KomgaAutoSyncOnSave = KomgaAutoSyncCheckBox.IsChecked == true;
        settings.KomgaSyncStoryArcsToCollections = KomgaStoryArcsCheckBox.IsChecked == true;

        string localPrefix = KomgaLocalPrefixTextBox.Text?.Trim() ?? "";
        string serverPrefix = KomgaServerPrefixTextBox.Text?.Trim() ?? "";
        settings.KomgaPathMappings.Clear();
        if (!string.IsNullOrEmpty(localPrefix) && !string.IsNullOrEmpty(serverPrefix))
        {
            settings.KomgaPathMappings.Add(new InkTag.Core.Komga.KomgaPathMapping
            {
                LocalPrefix = localPrefix,
                ServerPrefix = serverPrefix
            });
        }

        _settingsService.SaveSettings(settings);
        Close();
    }

    private void OpenApiUrl_Click(object? sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrlInBrowser("https://comicvine.gamespot.com/api/");
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

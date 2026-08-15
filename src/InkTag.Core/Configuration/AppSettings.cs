using System;
using System.IO;
using System.Text.Json;
using InkTag.Core.Scrapers;

namespace InkTag.Core.Configuration;

public class AppSettings
{
    public string ComicVineApiKey { get; set; } = string.Empty;
    public ScrapeMergeMode DefaultMergeMode { get; set; } = ScrapeMergeMode.FillMissingOnly;
    public double AutoMatchConfidenceThreshold { get; set; } = 0.85;
    public int CacheDurationHours { get; set; } = 168; // 7 days
}

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsFilePath;

    public AppSettings Settings { get; private set; }

    public AppSettingsService(string? customFilePath = null)
    {
        if (!string.IsNullOrEmpty(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "InkTag");
            
            _settingsFilePath = Path.Combine(configDir, "settings.json");
        }

        Settings = LoadSettings();
    }

    public string GetEffectiveComicVineApiKey()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ComicVineApiKey))
        {
            return Settings.ComicVineApiKey.Trim();
        }

        string? envKey = Environment.GetEnvironmentVariable("COMICVINE_API_KEY");
        return envKey?.Trim() ?? string.Empty;
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fallback to default on read error
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings ?? new AppSettings();
        try
        {
            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore write errors or log
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using InkTag.Core.Komga;
using InkTag.Core.Scrapers;

namespace InkTag.Core.Configuration;

public class AppSettings
{
    public string ComicVineApiKey { get; set; } = string.Empty;
    public ScrapeMergeMode DefaultMergeMode { get; set; } = ScrapeMergeMode.FillMissingOnly;
    public double AutoMatchConfidenceThreshold { get; set; } = 0.85;
    public bool AutoApplyOnVisualMatch { get; set; } = true;
    public double VisualMatchConfidenceThreshold { get; set; } = 0.90;
    public int CacheDurationHours { get; set; } = 168; // 7 days
    public bool EnableDebugLogging { get; set; } = false;
    public bool BulkScrapeAutoRenameFiles { get; set; } = false;
    public string BulkScrapeRenameTemplate { get; set; } = "{Series} #{Number:3} ({Year})";
    public List<string> AllowedRootPaths { get; set; } = new();
    public bool ClearLegacyZipCommentsOnUpgrade { get; set; } = true;
    public bool WriteTaggingAttributionToNotes { get; set; } = true;

    // Komga Server Settings
    public string KomgaServerUrl { get; set; } = string.Empty;
    public string KomgaApiKey { get; set; } = string.Empty;
    public string KomgaUser { get; set; } = string.Empty;
    public string KomgaPassword { get; set; } = string.Empty;
    public bool KomgaAutoSyncOnSave { get; set; } = false;
    public bool KomgaSyncStoryArcsToCollections { get; set; } = true;
    public List<KomgaPathMapping> KomgaPathMappings { get; set; } = new();
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

    public string GetEffectiveKomgaServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(Settings.KomgaServerUrl))
        {
            return Settings.KomgaServerUrl.Trim().TrimEnd('/');
        }

        string? envUrl = Environment.GetEnvironmentVariable("KOMGA_SERVER_URL");
        return envUrl?.Trim().TrimEnd('/') ?? string.Empty;
    }

    public string GetEffectiveKomgaApiKey()
    {
        if (!string.IsNullOrWhiteSpace(Settings.KomgaApiKey))
        {
            return Settings.KomgaApiKey.Trim();
        }

        string? envKey = Environment.GetEnvironmentVariable("KOMGA_API_KEY");
        return envKey?.Trim() ?? string.Empty;
    }

    public string GetEffectiveKomgaUser()
    {
        if (!string.IsNullOrWhiteSpace(Settings.KomgaUser))
        {
            return Settings.KomgaUser.Trim();
        }

        string? envUser = Environment.GetEnvironmentVariable("KOMGA_USER") ?? Environment.GetEnvironmentVariable("KOMGA_EMAIL");
        return envUser?.Trim() ?? string.Empty;
    }

    public string GetEffectiveKomgaPassword()
    {
        if (!string.IsNullOrWhiteSpace(Settings.KomgaPassword))
        {
            return Settings.KomgaPassword.Trim();
        }

        string? envPass = Environment.GetEnvironmentVariable("KOMGA_PASSWORD");
        return envPass?.Trim() ?? string.Empty;
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

    public void SaveSettings()
    {
        SaveSettings(Settings);
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings ?? new AppSettings();
        Logging.AppLogger.IsDebugEnabled = Settings.EnableDebugLogging;

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

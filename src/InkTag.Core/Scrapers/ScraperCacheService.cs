using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace InkTag.Core.Scrapers;

public class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string JsonData { get; set; } = string.Empty;
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ScraperCacheService
{
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly object _diskLock = new();

    public ScraperCacheService(string? customCachePath = null)
    {
        if (!string.IsNullOrEmpty(customCachePath))
        {
            _cacheFilePath = customCachePath;
        }
        else
        {
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "InkTag");
            _cacheFilePath = Path.Combine(cacheDir, "scraper_cache.json");
        }

        LoadCache();
    }

    public string? Get(string key, TimeSpan maxAge)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.CachedAt <= maxAge)
            {
                return entry.JsonData;
            }
            _cache.TryRemove(key, out _);
        }
        return null;
    }

    public void Set(string key, string jsonData)
    {
        var entry = new CacheEntry
        {
            Key = key,
            JsonData = jsonData,
            CachedAt = DateTimeOffset.UtcNow
        };

        _cache[key] = entry;
        SaveCache();
    }

    public void Clear()
    {
        _cache.Clear();
        SaveCache();
    }

    private void LoadCache()
    {
        lock (_diskLock)
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = File.ReadAllText(_cacheFilePath);
                    var list = JsonSerializer.Deserialize<CacheEntry[]>(json);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            _cache[item.Key] = item;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty cache on error
            }
        }
    }

    private void SaveCache()
    {
        lock (_diskLock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(_cache.Values);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch
            {
                // Ignore cache write errors
            }
        }
    }
}

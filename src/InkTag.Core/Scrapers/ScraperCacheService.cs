using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace InkTag.Core.Scrapers;

public class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string JsonData { get; set; } = string.Empty;
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ScraperCacheService : IDisposable
{
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly object _diskLock = new();
    private readonly Timer _debounceTimer;
    private volatile bool _isDirty = false;
    private bool _disposed = false;

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

        _debounceTimer = new Timer(OnDebounceTimerFired, null, Timeout.Infinite, Timeout.Infinite);

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
            _isDirty = true;
            ScheduleDebouncedSave();
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
        _isDirty = true;
        ScheduleDebouncedSave();
    }

    public void Clear()
    {
        _cache.Clear();
        _isDirty = true;
        Flush();
    }

    private void ScheduleDebouncedSave()
    {
        if (!_disposed)
        {
            // Debounce disk write by 2 seconds
            _debounceTimer.Change(2000, Timeout.Infinite);
        }
    }

    private void OnDebounceTimerFired(object? state)
    {
        Flush();
    }

    public void Flush()
    {
        if (!_isDirty) return;

        lock (_diskLock)
        {
            if (!_isDirty) return;

            try
            {
                string? dir = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(_cache.Values);
                File.WriteAllText(_cacheFilePath, json);
                _isDirty = false;
            }
            catch
            {
                // Ignore cache write errors
            }
        }
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _debounceTimer.Dispose();
            Flush();
        }
    }
}

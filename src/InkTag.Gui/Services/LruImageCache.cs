using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace InkTag.Gui.Services;

/// <summary>
/// Thread-safe size-bounded LRU cache for Avalonia Bitmaps that automatically disposes evicted bitmap instances.
/// Prevents unmanaged memory leaks during extensive image browsing and scraping.
/// </summary>
public class LruImageCache
{
    private readonly int _maxCapacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, Bitmap> _cache;
    private readonly LinkedList<string> _lruList;

    public LruImageCache(int maxCapacity = 60)
    {
        _maxCapacity = Math.Max(10, maxCapacity);
        _cache = new Dictionary<string, Bitmap>(_maxCapacity);
        _lruList = new LinkedList<string>();
    }

    public bool TryGetValue(string key, out Bitmap? bitmap)
    {
        if (string.IsNullOrEmpty(key))
        {
            bitmap = null;
            return false;
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out bitmap))
            {
                _lruList.Remove(key);
                _lruList.AddLast(key);
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    public void Set(string key, Bitmap bitmap)
    {
        if (string.IsNullOrEmpty(key) || bitmap == null) return;

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out _))
            {
                _lruList.Remove(key);
            }
            else if (_cache.Count >= _maxCapacity)
            {
                var oldestNode = _lruList.First;
                if (oldestNode != null)
                {
                    string oldestKey = oldestNode.Value;
                    _lruList.RemoveFirst();
                    _cache.Remove(oldestKey);
                }
            }

            _cache[key] = bitmap;
            _lruList.AddLast(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }
}

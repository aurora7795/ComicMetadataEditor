using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SharpCompress.Archives;

namespace InkTag.Gui.Services;

/// <summary>
/// Service for extracting and caching cover images from comic archives.
/// Implements single-pass archive scanning and size-capped LRU bitmap cache eviction.
/// </summary>
public class ArchiveCoverService
{
    private const int MaxCacheCapacity = 50;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, Bitmap> _coverCache = new();
    private readonly Dictionary<string, ulong> _hashCache = new();
    private readonly LinkedList<string> _lruOrder = new();

    public async Task<Bitmap?> LoadCoverAsync(string archivePath, CancellationToken cancellationToken)
    {
        var result = await LoadCoverWithHashAsync(archivePath, cancellationToken);
        return result.Bitmap;
    }

    public async Task<(Bitmap? Bitmap, ulong CoverHash)> LoadCoverWithHashAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            return (null, 0);
        }

        lock (_cacheLock)
        {
            if (_coverCache.TryGetValue(archivePath, out var cachedBitmap))
            {
                _lruOrder.Remove(archivePath);
                _lruOrder.AddLast(archivePath);
                _hashCache.TryGetValue(archivePath, out ulong cachedHash);
                return (cachedBitmap, cachedHash);
            }
        }

        return await Task.Run<(Bitmap?, ulong)>(() =>
        {
            try
            {
                var editor = new InkTag.Core.MetadataEditor();
                byte[]? bytes = editor.ExtractCoverImageBytes(archivePath);
                if (bytes == null || bytes.Length == 0)
                {
                    return (null, 0);
                }

                using var memoryStream = new MemoryStream(bytes);
                var bitmap = new Bitmap(memoryStream);
                ulong hash = InkTag.Core.Images.PerceptualHashService.ComputeDHash(bytes);

                lock (_cacheLock)
                {
                    if (_coverCache.TryGetValue(archivePath, out var existing))
                    {
                        existing.Dispose();
                        _lruOrder.Remove(archivePath);
                    }
                    else if (_coverCache.Count >= MaxCacheCapacity)
                    {
                        var oldestKey = _lruOrder.First?.Value;
                        if (oldestKey != null)
                        {
                            _lruOrder.RemoveFirst();
                            if (_coverCache.Remove(oldestKey, out var evictedBitmap))
                            {
                                evictedBitmap.Dispose();
                            }
                            _hashCache.Remove(oldestKey);
                        }
                    }

                    _coverCache[archivePath] = bitmap;
                    if (hash != 0)
                    {
                        _hashCache[archivePath] = hash;
                    }
                    _lruOrder.AddLast(archivePath);
                }

                return (bitmap, hash);
            }
            catch
            {
                return (null, 0);
            }
        }, cancellationToken);
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        ext = ext.ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif" || ext == ".bmp";
    }
}

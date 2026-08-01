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
    private readonly LinkedList<string> _lruOrder = new();

    public async Task<Bitmap?> LoadCoverAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        lock (_cacheLock)
        {
            if (_coverCache.TryGetValue(archivePath, out var cachedBitmap))
            {
                _lruOrder.Remove(archivePath);
                _lruOrder.AddLast(archivePath);
                return cachedBitmap;
            }
        }

        return await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.OpenArchive(stream);

                var imageEntries = archive.Entries
                    .Where(e => !e.IsDirectory && e.Key != null && IsImageFile(e.Key!))
                    .ToList();

                if (imageEntries.Count == 0)
                {
                    return null;
                }

                var bestEntry = imageEntries.FirstOrDefault(e => Path.GetFileName(e.Key!).Contains("cover", StringComparison.OrdinalIgnoreCase));
                if (bestEntry == null)
                {
                    bestEntry = imageEntries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).First();
                }

                using var memoryStream = new MemoryStream();
                bestEntry.OpenEntryStream().CopyTo(memoryStream);
                memoryStream.Position = 0;

                var bitmap = new Bitmap(memoryStream);

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
                        }
                    }

                    _coverCache[archivePath] = bitmap;
                    _lruOrder.AddLast(archivePath);
                }

                return bitmap;
            }
            catch
            {
                return null;
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

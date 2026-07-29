using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SharpCompress.Archives;

namespace AvaloniaApp.Services;

public class ArchiveCoverService
{
    private readonly ConcurrentDictionary<string, Bitmap> _coverCache = new();

    public async Task<Bitmap?> LoadCoverAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        if (_coverCache.TryGetValue(archivePath, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        return await Task.Run(() =>
        {
            try
            {
                // First pass: scan entry keys to find the alphabetically first image file
                string? bestImageKey = null;
                
                using (var stream = File.OpenRead(archivePath))
                using (var reader = SharpCompress.Readers.ReaderFactory.OpenReader(stream, new SharpCompress.Readers.ReaderOptions()))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory && reader.Entry.Key != null && IsImageFile(reader.Entry.Key))
                        {
                            if (bestImageKey == null || string.Compare(reader.Entry.Key, bestImageKey, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                bestImageKey = reader.Entry.Key;
                            }
                        }
                    }
                }

                if (bestImageKey == null)
                {
                    return null;
                }

                // Second pass: extract the stream for the best image key
                using (var stream = File.OpenRead(archivePath))
                using (var reader = SharpCompress.Readers.ReaderFactory.OpenReader(stream, new SharpCompress.Readers.ReaderOptions()))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory && reader.Entry.Key == bestImageKey)
                        {
                            using var entryStream = reader.OpenEntryStream();
                            using var memoryStream = new MemoryStream();
                            entryStream.CopyTo(memoryStream);
                            memoryStream.Position = 0;

                            var bitmap = new Bitmap(memoryStream);
                            _coverCache[archivePath] = bitmap;
                            return bitmap;
                        }
                    }
                }

                return null;
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
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif";
    }
}

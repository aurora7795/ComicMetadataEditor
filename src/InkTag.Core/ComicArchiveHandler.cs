using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using InkTag.Core.Images;
using InkTag.Core.Logging;
using InkTag.Core.Parsing;

namespace InkTag.Core;

/// <summary>
/// Internal handler for multi-tiered comic archive reading, stream extraction, and cover image processing.
/// </summary>
internal static class ComicArchiveHandler
{
    private static readonly string[] ValidImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];

    /// <summary>
    /// Opens a network-optimized FileStream with 64KB buffer and non-exclusive FileShare.ReadWrite.
    /// Uses FileOptions.None to ensure full compatibility with Linux FUSE mounts (GVFS, FTP, SSHFS, SMB).
    /// </summary>
    public static FileStream OpenReadOptimized(string filePath, int bufferSize = 65536)
    {
        return new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize,
            FileOptions.None);
    }

    /// <summary>
    /// Reads metadata from a CBZ/CBR archive using the multi-tiered strategy.
    /// </summary>
    public static ComicInfo ReadMetadata(
        string filePath,
        out bool hasEmbeddedXml,
        out bool usedSequentialFallback,
        CancellationToken cancellationToken = default)
    {
        hasEmbeddedXml = false;
        usedSequentialFallback = false;
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return new ComicInfo();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string fileName = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath) ?? "";

        // Fast in-memory path for .cbz (ZIP) archives
        if (ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            // 1. Fast random-access seek
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppLogger.LogDebug($"[ComicArchiveHandler] Attempting fast-path random-access seek for '{fileName}'...");
                using var fileStream = OpenReadOptimized(filePath);
                using var zipArchive = new System.IO.Compression.ZipArchive(fileStream, System.IO.Compression.ZipArchiveMode.Read);
                var entry = zipArchive.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var entryStream = entry.Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    ms.Position = 0;
                    var info = ComicInfoXmlSanitizer.DeserializeComicInfo(ms);
                    hasEmbeddedXml = true;

                    if (!string.IsNullOrWhiteSpace(zipArchive.Comment))
                    {
                        ComicBookInfoParser.TryMergeFromLegacyJson(info, zipArchive.Comment);
                    }

                    AppLogger.LogDebug($"[ComicArchiveHandler] Read metadata via fast-path seek for '{fileName}' in {sw.ElapsedMilliseconds}ms (Title: '{info.Title}', Series: '{info.Series}', Issue: '{info.Number}').");
                    return info;
                }

                // Check for legacy ComicBookInfo in zip comment
                if (!string.IsNullOrWhiteSpace(zipArchive.Comment) &&
                    ComicBookInfoParser.TryParse(zipArchive.Comment, out var cbiFromComment) && cbiFromComment != null)
                {
                    AppLogger.LogDebug($"[ComicArchiveHandler] Read legacy ComicBookInfo from zip comment for '{fileName}' in {sw.ElapsedMilliseconds}ms.");
                    return cbiFromComment;
                }

                // Check for internal ComicBookInfo.json entry
                var cbiEntry = zipArchive.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals("ComicBookInfo.json", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(e.FullName).Equals("ComicBookInfo", StringComparison.OrdinalIgnoreCase));
                if (cbiEntry != null)
                {
                    using var entryStream = cbiEntry.Open();
                    using var reader = new StreamReader(entryStream);
                    string json = reader.ReadToEnd();
                    if (ComicBookInfoParser.TryParse(json, out var cbiFromFile) && cbiFromFile != null)
                    {
                        AppLogger.LogDebug($"[ComicArchiveHandler] Read legacy ComicBookInfo.json for '{fileName}' in {sw.ElapsedMilliseconds}ms.");
                        return cbiFromFile;
                    }
                }

                AppLogger.LogDebug($"[ComicArchiveHandler] No ComicInfo.xml or legacy metadata found via fast-path seek in '{fileName}' ({sw.ElapsedMilliseconds}ms).");
                return new ComicInfo();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[ComicArchiveHandler] Fast-path seek failed for '{fileName}' ({ex.Message}). Retrying with sequential NonSeekableStream...");
            }

            // 2. Sequential forward-only streaming
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                usedSequentialFallback = true;
                var seqSw = System.Diagnostics.Stopwatch.StartNew();
                using var rawStream = OpenReadOptimized(filePath);
                using var nonSeekable = new NonSeekableStream(rawStream, cancellationToken);
                using var zipArchive = new System.IO.Compression.ZipArchive(nonSeekable, System.IO.Compression.ZipArchiveMode.Read);
                foreach (var entry in zipArchive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Path.GetFileName(entry.FullName).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        ms.Position = 0;
                        var info = ComicInfoXmlSanitizer.DeserializeComicInfo(ms);
                        hasEmbeddedXml = true;

                        if (!string.IsNullOrWhiteSpace(zipArchive.Comment))
                        {
                            ComicBookInfoParser.TryMergeFromLegacyJson(info, zipArchive.Comment);
                        }

                        AppLogger.LogDebug($"[ComicArchiveHandler] Read metadata via sequential NonSeekableStream for '{fileName}' in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                        return info;
                    }
                }

                if (!string.IsNullOrWhiteSpace(zipArchive.Comment) &&
                    ComicBookInfoParser.TryParse(zipArchive.Comment, out var seqCbi) && seqCbi != null)
                {
                    AppLogger.LogDebug($"[ComicArchiveHandler] Read legacy ComicBookInfo from zip comment in sequential mode for '{fileName}' in {seqSw.ElapsedMilliseconds}ms.");
                    return seqCbi;
                }

                AppLogger.LogDebug($"[ComicArchiveHandler] No ComicInfo.xml or legacy metadata found via sequential stream in '{fileName}' ({seqSw.ElapsedMilliseconds}ms).");
                return new ComicInfo();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[ComicArchiveHandler] Sequential streaming failed for '{fileName}' ({ex.Message}). Retrying with SharpCompress...");
            }
        }

        // Random-access in-memory path for .cbr (RAR) or fallback archives
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scSw = System.Diagnostics.Stopwatch.StartNew();
            using var stream = OpenReadOptimized(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new SharpCompress.Readers.ReaderOptions { LookForHeader = true });

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.IsDirectory && 
                    entry.Key != null &&
                    Path.GetFileName(entry.Key).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    ms.Position = 0;
                    var info = ComicInfoXmlSanitizer.DeserializeComicInfo(ms);
                    hasEmbeddedXml = true;
                    AppLogger.LogDebug($"[ComicArchiveHandler] Read metadata via SharpCompress fallback for '{fileName}' in {scSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                    return info;
                }
            }

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.IsDirectory &&
                    entry.Key != null &&
                    (Path.GetFileName(entry.Key).Equals("ComicBookInfo.json", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(entry.Key).Equals("ComicBookInfo", StringComparison.OrdinalIgnoreCase)))
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var reader = new StreamReader(entryStream);
                    string json = reader.ReadToEnd();
                    if (ComicBookInfoParser.TryParse(json, out var scCbi) && scCbi != null)
                    {
                        AppLogger.LogDebug($"[ComicArchiveHandler] Read legacy ComicBookInfo.json via SharpCompress for '{fileName}' in {scSw.ElapsedMilliseconds}ms.");
                        return scCbi;
                    }
                }
            }

            AppLogger.LogDebug($"[ComicArchiveHandler] No ComicInfo.xml or legacy metadata found via SharpCompress in '{fileName}' ({scSw.ElapsedMilliseconds}ms).");
            return new ComicInfo();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to read archive metadata from '{filePath}': {ex.Message}");
        }

        return new ComicInfo();
    }

    public static ComicInfo ReadMetadata(string filePath, CancellationToken cancellationToken = default) =>
        ReadMetadata(filePath, out _, out _, cancellationToken);

    public static Task<ComicInfo> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadMetadata(filePath, out _, out _, cancellationToken), cancellationToken);
    }

    public static string? ExtractCoverImage(string comicFilePath, string outputFilePath)
    {
        if (!File.Exists(comicFilePath))
        {
            throw new FileNotFoundException($"Comic file not found: {comicFilePath}", comicFilePath);
        }

        using Stream stream = File.OpenRead(comicFilePath);
        using var archive = ArchiveFactory.OpenArchive(stream);

        var imageEntries = archive.Entries
            .Where(e => !e.IsDirectory && e.Key != null && ValidImageExtensions.Contains(Path.GetExtension(e.Key).ToLowerInvariant()))
            .ToList();

        if (imageEntries.Count == 0)
        {
            return null;
        }

        var bestEntry = imageEntries.FirstOrDefault(e => Path.GetFileName(e.Key!).Contains("cover", StringComparison.OrdinalIgnoreCase))
                     ?? imageEntries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).First();

        string? dir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using (var fs = File.Create(outputFilePath))
        {
            bestEntry.WriteTo(fs);
        }

        return outputFilePath;
    }

    public static byte[]? ExtractCoverImageBytes(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        string ext = Path.GetExtension(filePath) ?? "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string fileName = Path.GetFileName(filePath);

        if (ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = OpenReadOptimized(filePath);
                using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                var imageEntries = zip.Entries
                    .Where(e => IsImageFileName(e.FullName))
                    .ToList();

                if (imageEntries.Count > 0)
                {
                    var bestEntry = imageEntries.FirstOrDefault(e => Path.GetFileName(e.FullName).Contains("cover", StringComparison.OrdinalIgnoreCase))
                                 ?? imageEntries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).First();

                    using var entryStream = bestEntry.Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    byte[] result = ms.ToArray();
                    AppLogger.LogDebug($"[ComicArchiveHandler] Extracted cover for '{fileName}' via fast seek ({bestEntry.FullName}, {result.Length} bytes) in {sw.ElapsedMilliseconds}ms.");
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[ComicArchiveHandler] Cover fast seek failed for '{fileName}' ({ex.Message}). Retrying with sequential NonSeekableStream...");
            }

            try
            {
                var seqSw = System.Diagnostics.Stopwatch.StartNew();
                using var rawStream = OpenReadOptimized(filePath);
                using var nonSeekable = new NonSeekableStream(rawStream);
                using var zip = new System.IO.Compression.ZipArchive(nonSeekable, System.IO.Compression.ZipArchiveMode.Read);

                byte[]? firstImage = null;
                foreach (var entry in zip.Entries)
                {
                    if (IsImageFileName(entry.FullName))
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        byte[] bytes = ms.ToArray();

                        if (Path.GetFileName(entry.FullName).Contains("cover", StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.LogDebug($"[ComicArchiveHandler] Extracted explicit cover for '{fileName}' via sequential stream in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                            return bytes;
                        }

                        firstImage ??= bytes;
                    }
                }

                if (firstImage != null)
                {
                    AppLogger.LogDebug($"[ComicArchiveHandler] Extracted first image cover for '{fileName}' via sequential stream in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                    return firstImage;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[ComicArchiveHandler] Cover sequential streaming failed for '{fileName}' ({ex.Message}). Retrying with SharpCompress...");
            }
        }

        try
        {
            var scSw = System.Diagnostics.Stopwatch.StartNew();
            using var stream = OpenReadOptimized(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new SharpCompress.Readers.ReaderOptions { LookForHeader = true });

            var imageEntries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null && IsImageFileName(e.Key))
                .ToList();

            if (imageEntries.Count > 0)
            {
                var bestEntry = imageEntries.FirstOrDefault(e => Path.GetFileName(e.Key!).Contains("cover", StringComparison.OrdinalIgnoreCase))
                             ?? imageEntries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).First();

                using var ms = new MemoryStream();
                bestEntry.OpenEntryStream().CopyTo(ms);
                byte[] result = ms.ToArray();
                AppLogger.LogDebug($"[ComicArchiveHandler] Extracted cover for '{fileName}' via SharpCompress fallback in {scSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                return result;
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.LogDebug($"[ComicArchiveHandler] Cover extraction failed for '{fileName}': {ex.Message}");
            return null;
        }
    }

    public static Task<byte[]?> ExtractCoverImageBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExtractCoverImageBytes(filePath), cancellationToken);
    }

    public static ulong GetCoverHash(string filePath)
    {
        var bytes = ExtractCoverImageBytes(filePath);
        return bytes != null && bytes.Length > 0 ? PerceptualHashService.ComputeDHash(bytes) : 0;
    }

    public static Task<ulong> GetCoverHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetCoverHash(filePath), cancellationToken);
    }

    private static bool IsImageFileName(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        ext = ext.ToLowerInvariant();
        return ValidImageExtensions.Contains(ext);
    }
}

/// <summary>
/// Stream wrapper that hides CanSeek / Seek capabilities, forcing ZipArchive to read sequentially
/// from byte 0 without issuing backwards seek syscalls. Essential for GVFS FTP / FUSE virtual mounts.
/// </summary>
internal sealed class NonSeekableStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _cancellationToken;

    public NonSeekableStream(Stream inner, CancellationToken cancellationToken = default)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cancellationToken = cancellationToken;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        return _inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        return _inner.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        return _inner.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        return _inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}

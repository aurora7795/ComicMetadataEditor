using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
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

    public static List<string> GetImageEntries(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return new List<string>();

        string ext = Path.GetExtension(filePath) ?? "";
        if (ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = OpenReadOptimized(filePath);
                using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                return zip.Entries
                    .Where(e => !e.FullName.EndsWith('/') && IsImageFileName(e.FullName) && !IsIgnoredSystemEntry(e.FullName))
                    .Select(e => e.FullName)
                    .OrderBy(name => Path.GetFileName(name), NaturalStringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[ComicArchiveHandler] Fast seek GetImageEntries failed for '{Path.GetFileName(filePath)}' ({ex.Message}). Retrying with SharpCompress...");
            }
        }

        try
        {
            using var stream = OpenReadOptimized(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new SharpCompress.Readers.ReaderOptions { LookForHeader = true });
            return archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null && IsImageFileName(e.Key) && !IsIgnoredSystemEntry(e.Key))
                .Select(e => e.Key!)
                .OrderBy(name => Path.GetFileName(name), NaturalStringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[ComicArchiveHandler] Failed to get image entries for '{Path.GetFileName(filePath)}': {ex.Message}");
            return new List<string>();
        }
    }

    public static string? ExtractCoverImage(string comicFilePath, string outputFilePath, int pageIndex = 0)
    {
        if (!File.Exists(comicFilePath))
        {
            throw new FileNotFoundException($"Comic file not found: {comicFilePath}", comicFilePath);
        }

        byte[]? bytes = ExtractCoverImageBytes(comicFilePath, pageIndex);
        if (bytes == null || bytes.Length == 0) return null;

        string? dir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(outputFilePath, bytes);
        return outputFilePath;
    }

    public static byte[]? ExtractCoverImageBytes(string filePath, int pageIndex = 0)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || pageIndex < 0) return null;

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
                    .Where(e => !e.FullName.EndsWith('/') && IsImageFileName(e.FullName) && !IsIgnoredSystemEntry(e.FullName))
                    .OrderBy(e => Path.GetFileName(e.FullName), NaturalStringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (imageEntries.Count > pageIndex)
                {
                    var targetEntry = imageEntries[pageIndex];
                    using var entryStream = targetEntry.Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    byte[] result = ms.ToArray();
                    AppLogger.LogDebug($"[ComicArchiveHandler] Extracted page {pageIndex} for '{fileName}' via fast seek ({targetEntry.FullName}, {result.Length} bytes) in {sw.ElapsedMilliseconds}ms.");
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

                var collected = new List<(string Name, byte[] Bytes)>();
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith('/') && IsImageFileName(entry.FullName) && !IsIgnoredSystemEntry(entry.FullName))
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        collected.Add((entry.FullName, ms.ToArray()));
                    }
                }

                if (collected.Count > 0)
                {
                    var sorted = collected
                        .OrderBy(c => Path.GetFileName(c.Name), NaturalStringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (sorted.Count > pageIndex)
                    {
                        AppLogger.LogDebug($"[ComicArchiveHandler] Extracted page {pageIndex} for '{fileName}' via sequential stream in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                        return sorted[pageIndex].Bytes;
                    }
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
                .Where(e => !e.IsDirectory && e.Key != null && IsImageFileName(e.Key) && !IsIgnoredSystemEntry(e.Key))
                .OrderBy(e => Path.GetFileName(e.Key!), NaturalStringComparer.OrdinalIgnoreCase)
                .ToList();

            if (imageEntries.Count > pageIndex)
            {
                var targetEntry = imageEntries[pageIndex];
                using var ms = new MemoryStream();
                targetEntry.OpenEntryStream().CopyTo(ms);
                byte[] result = ms.ToArray();
                AppLogger.LogDebug($"[ComicArchiveHandler] Extracted page {pageIndex} for '{fileName}' via SharpCompress fallback in {scSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                return result;
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.LogDebug($"[ComicArchiveHandler] Cover extraction failed for '{fileName}' at index {pageIndex}: {ex.Message}");
            return null;
        }
    }

    public static Task<byte[]?> ExtractCoverImageBytesAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExtractCoverImageBytes(filePath, pageIndex), cancellationToken);
    }

    public static ulong GetCoverHash(string filePath, int pageIndex = 0)
    {
        var bytes = ExtractCoverImageBytes(filePath, pageIndex);
        return bytes != null && bytes.Length > 0 ? PerceptualHashService.ComputeDHash(bytes) : 0;
    }

    public static Task<ulong> GetCoverHashAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetCoverHash(filePath, pageIndex), cancellationToken);
    }

    public static List<(int PageIndex, ulong Hash, byte[] Bytes)> GetCandidateCoverHashes(string filePath, int maxPages = 2)
    {
        var results = new List<(int PageIndex, ulong Hash, byte[] Bytes)>();
        for (int i = 0; i < maxPages; i++)
        {
            byte[]? bytes = ExtractCoverImageBytes(filePath, i);
            if (bytes == null || bytes.Length == 0) break;
            ulong hash = PerceptualHashService.ComputeDHash(bytes);
            results.Add((i, hash, bytes));
        }
        return results;
    }

    public static PageRemovalResult StripFirstPage(string filePath) => RemoveArchivePages(filePath, new[] { 0 });

    public static PageRemovalResult RemoveArchivePages(string filePath, IEnumerable<int> pageIndices)
    {
        var result = new PageRemovalResult { FilePath = filePath };

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            result.ErrorMessage = $"Comic file not found: '{filePath}'";
            return result;
        }

        var indicesToRemove = new HashSet<int>(pageIndices.Where(i => i >= 0));
        if (indicesToRemove.Count == 0)
        {
            result.Success = true;
            return result;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        string? tempCbzPath = null;
        string? backupOriginalPath = null;
        string? backupTargetPath = null;
        string originalExtension = Path.GetExtension(filePath) ?? "";
        string targetPath = originalExtension.Equals(".cbr", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(filePath, ".cbz")
            : filePath;

        try
        {
            // 1. Extract archive entries safely
            using (Stream stream = File.OpenRead(filePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(tempDir, new SharpCompress.Common.ExtractionOptions { Overwrite = true, ExtractFullPath = false });
                    }
                }
            }

            // Zip-Slip defense
            string canonicalTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string extractedFile in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string canonicalFile = Path.GetFullPath(extractedFile);
                if (!canonicalFile.StartsWith(canonicalTempDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Archive entry '{extractedFile}' escapes extraction target directory.");
                }
            }

            // 2. Identify sorted image files
            var imageFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsImageFileName(f) && !IsIgnoredSystemEntry(f))
                .OrderBy(f => Path.GetFileName(f), NaturalStringComparer.OrdinalIgnoreCase)
                .ToList();

            result.OriginalPageCount = imageFiles.Count;

            var validIndicesToRemove = indicesToRemove.Where(idx => idx >= 0 && idx < imageFiles.Count).ToHashSet();
            if (validIndicesToRemove.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "None of the specified page indices exist in the comic archive.";
                result.FinalPageCount = imageFiles.Count;
                return result;
            }

            if (validIndicesToRemove.Count >= imageFiles.Count)
            {
                throw new InvalidOperationException("Cannot remove all page images from comic archive.");
            }

            // Delete targeted image files
            for (int i = 0; i < imageFiles.Count; i++)
            {
                if (validIndicesToRemove.Contains(i))
                {
                    string fileToDelete = imageFiles[i];
                    string fileName = Path.GetFileName(fileToDelete);
                    try
                    {
                        File.Delete(fileToDelete);
                        result.RemovedEntries.Add(fileName);
                        result.RemovedCount++;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"Failed to delete entry '{fileName}': {ex.Message}");
                    }
                }
            }

            result.FinalPageCount = result.OriginalPageCount - result.RemovedCount;

            // 3. Update ComicInfo.xml if present
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            ComicInfo comicInfo;

            if (File.Exists(xmlPath))
            {
                try
                {
                    string xmlContent = File.ReadAllText(xmlPath);
                    comicInfo = ComicInfoXmlSanitizer.DeserializeComicInfo(xmlContent);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Failed to parse ComicInfo.xml during page removal: {ex.Message}. Initializing fresh metadata.");
                    comicInfo = new ComicInfo();
                }
            }
            else
            {
                comicInfo = new ComicInfo();
            }

            // Update PageCount
            if (comicInfo.PageCount.HasValue)
            {
                comicInfo.PageCount = Math.Max(1, comicInfo.PageCount.Value - result.RemovedCount);
            }
            else
            {
                comicInfo.PageCount = result.FinalPageCount;
            }

            // Renumber Pages collection
            if (comicInfo.Pages?.Page != null && comicInfo.Pages.Page.Length > 0)
            {
                var remainingPages = comicInfo.Pages.Page
                    .Where(p => !validIndicesToRemove.Contains(p.Image))
                    .OrderBy(p => p.Image)
                    .ToList();

                for (int i = 0; i < remainingPages.Count; i++)
                {
                    remainingPages[i].Image = i;
                }

                if (remainingPages.Count > 0 && !remainingPages.Any(p => string.Equals(p.Type, "FrontCover", StringComparison.OrdinalIgnoreCase)))
                {
                    remainingPages[0].Type = "FrontCover";
                }

                comicInfo.Pages.Page = remainingPages.ToArray();
            }

            // Write updated ComicInfo.xml
            using (var fs = new FileStream(xmlPath, FileMode.Create, FileAccess.Write))
            {
                ComicInfoXmlSanitizer.SerializeComicInfo(comicInfo, fs);
            }

            // 4. Create new CBZ archive
            tempCbzPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz.tmp");
            using (var zipStream = File.OpenWrite(tempCbzPath))
            using (var writer = new SharpCompress.Writers.Zip.ZipWriter(zipStream, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate)))
            {
                foreach (string file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                    if (IsIgnoredSystemEntry(relativePath)) continue;
                    writer.Write(relativePath, file);
                }
            }

            // Verify integrity
            using (var testStream = File.OpenRead(tempCbzPath))
            using (var testArchive = ArchiveFactory.OpenArchive(testStream))
            {
                if (!testArchive.Entries.Any(e => !e.IsDirectory))
                {
                    throw new InvalidDataException("Generated temporary archive contains no entries.");
                }
            }

            // Atomic swap
            backupOriginalPath = filePath + ".bak";
            backupTargetPath = targetPath + ".bak";

            if (File.Exists(backupOriginalPath)) File.Delete(backupOriginalPath);
            if (File.Exists(backupTargetPath) && backupTargetPath != backupOriginalPath) File.Delete(backupTargetPath);

            File.Move(filePath, backupOriginalPath);

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempCbzPath, targetPath);
            tempCbzPath = null;

            if (File.Exists(backupOriginalPath))
            {
                File.Delete(backupOriginalPath);
            }

            result.Success = true;
            result.FilePath = targetPath;
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to remove pages from '{filePath}': {ex.Message}", ex);
            result.Success = false;
            result.ErrorMessage = ex.Message;

            if (backupOriginalPath != null && File.Exists(backupOriginalPath))
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    File.Move(backupOriginalPath, filePath);
                }
                catch { }
            }

            return result;
        }
        finally
        {
            if (tempCbzPath != null && File.Exists(tempCbzPath))
            {
                try { File.Delete(tempCbzPath); } catch { }
            }
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    public static bool IsIgnoredSystemEntry(string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return true;
        string fileName = Path.GetFileName(entryName);
        if (fileName.StartsWith('.') || fileName.StartsWith("._", StringComparison.Ordinal)) return true;
        string norm = entryName.Replace('\\', '/');
        return norm.Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
               norm.Contains("/.AppleDouble/", StringComparison.OrdinalIgnoreCase) ||
               norm.Contains("/.Trash/", StringComparison.OrdinalIgnoreCase) ||
               norm.Contains("/.Trash-", StringComparison.OrdinalIgnoreCase) ||
               norm.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
               norm.StartsWith(".AppleDouble/", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);
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

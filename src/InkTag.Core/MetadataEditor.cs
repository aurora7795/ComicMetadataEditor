using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Serialization;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using System.Xml;
using System.Xml.Schema;
using InkTag.Core.Logging;

namespace InkTag.Core;

public class BulkEditReport
{
    public int TotalFound { get; set; }
    public List<string> Successes { get; } = new();
    public List<(string Path, Exception Exception)> Failures { get; } = new();
}

public record MetadataDiffItem(string PropertyName, object? OldValue, object? NewValue);

public class MetadataEditor
{
    /// <summary>
    /// Bulk edits the metadata in all CBR and CBZ files within the specified directory.
    /// </summary>
    /// <param name="directoryPath">The path to the directory containing CBR or CBZ files.</param>
    /// <param name="editAction">An action to perform on the ComicInfo object for each file.</param>
    /// <param name="recursive">If true, searches subdirectories recursively.</param>
    /// <returns>A report containing statistics and failure logs.</returns>
    public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction, bool recursive = false)
    {
        var report = new BulkEditReport();

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Support both .cbr and .cbz files
        var comicFiles = Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
            .ToList();

        report.TotalFound = comicFiles.Count;

        foreach (var file in comicFiles)
        {
            try
            {
                EditMetadata(file, editAction);
                report.Successes.Add(file);
            }
            catch (Exception ex)
            {
                report.Failures.Add((file, ex));
            }
        }

        return report;
    }

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

    public ComicInfo ReadMetadata(string filePath, CancellationToken cancellationToken = default) => 
        ReadMetadata(filePath, out _, cancellationToken);

    public ComicInfo ReadMetadata(string filePath, out bool usedSequentialFallback, CancellationToken cancellationToken = default)
    {
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
                AppLogger.LogDebug($"[MetadataEditor] Attempting fast-path random-access seek for '{fileName}'...");
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
                    var info = DeserializeComicInfo(ms);
                    AppLogger.LogDebug($"[MetadataEditor] Read metadata via fast-path seek for '{fileName}' in {sw.ElapsedMilliseconds}ms (Title: '{info.Title}', Series: '{info.Series}', Issue: '{info.Number}').");
                    return info;
                }

                AppLogger.LogDebug($"[MetadataEditor] No ComicInfo.xml found via fast-path seek in '{fileName}' ({sw.ElapsedMilliseconds}ms).");
                return new ComicInfo();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[MetadataEditor] Fast-path seek failed for '{fileName}' ({ex.Message}). Retrying with sequential NonSeekableStream...");
            }

            // 2. Sequential forward-only streaming (requires 0 backwards seeking)
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
                        var info = DeserializeComicInfo(ms);
                        AppLogger.LogDebug($"[MetadataEditor] Read metadata via sequential NonSeekableStream for '{fileName}' in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                        return info;
                    }
                }

                AppLogger.LogDebug($"[MetadataEditor] No ComicInfo.xml found via sequential stream in '{fileName}' ({seqSw.ElapsedMilliseconds}ms).");
                return new ComicInfo();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[MetadataEditor] Sequential streaming failed for '{fileName}' ({ex.Message}). Retrying with SharpCompress...");
            }
        }

        // Random-access in-memory path for .cbr (RAR) or fallback archives
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scSw = System.Diagnostics.Stopwatch.StartNew();
            using var stream = OpenReadOptimized(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions { LookForHeader = true });

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
                    var info = DeserializeComicInfo(ms);
                    AppLogger.LogDebug($"[MetadataEditor] Read metadata via SharpCompress fallback for '{fileName}' in {scSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                    return info;
                }
            }

            AppLogger.LogDebug($"[MetadataEditor] No ComicInfo.xml found via SharpCompress in '{fileName}' ({scSw.ElapsedMilliseconds}ms).");
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

    private static ComicInfo DeserializeComicInfo(Stream stream)
    {
        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
            return (ComicInfo)serializer.Deserialize(stream)!;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to deserialize ComicInfo XML: {ex.Message}");
            return new ComicInfo();
        }
    }

    public void EditMetadata(string filePath, Action<ComicInfo> editAction)
    {
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
            // 1. Extract the archive contents safely
            using (Stream stream = File.OpenRead(filePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(tempDir, new ExtractionOptions { Overwrite = true, ExtractFullPath = false });
                    }
                }
            }

            // Verify all extracted files remain strictly contained within tempDir
            string canonicalTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string extractedFile in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string canonicalFile = Path.GetFullPath(extractedFile);
                if (!canonicalFile.StartsWith(canonicalTempDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Archive entry '{extractedFile}' escapes extraction target directory.");
                }
            }

            // 2. Find and deserialize / create ComicInfo.xml
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            ComicInfo comicInfo;

            if (File.Exists(xmlPath))
            {
                // Validate the XML against the official schema before deserialization
                ValidateXml(xmlPath);
                using (FileStream fs = new FileStream(xmlPath, FileMode.Open, FileAccess.Read))
                {
                    comicInfo = DeserializeComicInfo(fs);
                }
            }
            else
            {
                comicInfo = new ComicInfo();
            }

            // Apply edits
            editAction(comicInfo);

            // Serialize back to XML
            using (FileStream fs = new FileStream(xmlPath, FileMode.Create, FileAccess.Write))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
                serializer.Serialize(fs, comicInfo);
            }

            // 3. Safe repack: Repack into a temporary CBZ archive inside the temporary path
            tempCbzPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cbz.tmp");
            using (Stream stream = File.OpenWrite(tempCbzPath))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                foreach (var file in Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories))
                {
                    string entryName = GetRelativePath(tempDir, file).Replace('\\', '/');
                    writer.Write(entryName, file);
                }
            }

            // 4. Validate the repackaged temp archive
            // Ensure size > 0 and contains entries
            FileInfo tempCbzInfo = new FileInfo(tempCbzPath);
            if (!tempCbzInfo.Exists || tempCbzInfo.Length == 0)
            {
                throw new InvalidDataException("Generated temporary archive is empty or invalid.");
            }

            // Verify readability using SharpCompress ArchiveFactory
            using (Stream stream = File.OpenRead(tempCbzPath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                if (!archive.Entries.Any(e => !e.IsDirectory))
                {
                    throw new InvalidDataException("Generated temporary archive contains no entries.");
                }
            }

            // 5. Atomic-like Swap
            // Back up the target path if it already exists (could be same as filePath if already a .cbz,
            // or could be different if converting a .cbr to .cbz where a .cbz already exists).
            if (File.Exists(targetPath))
            {
                backupTargetPath = targetPath + "." + Guid.NewGuid().ToString() + ".bak";
                File.Move(targetPath, backupTargetPath);
            }

            // If the original file was different from target path (e.g. .cbr converting to .cbz),
            // we must also back up the original file so we can delete it only on successful swap.
            if (!filePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                backupOriginalPath = filePath + "." + Guid.NewGuid().ToString() + ".bak";
                File.Move(filePath, backupOriginalPath);
            }

            try
            {
                // Move the validated temp CBZ to targetPath
                File.Move(tempCbzPath, targetPath);
                tempCbzPath = null; // Successfully transferred ownership
            }
            catch (Exception)
            {
                // Rollback swap
                if (backupTargetPath != null && File.Exists(backupTargetPath))
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(backupTargetPath, targetPath);
                    backupTargetPath = null;
                }

                if (backupOriginalPath != null && File.Exists(backupOriginalPath))
                {
                    File.Move(backupOriginalPath, filePath);
                    backupOriginalPath = null;
                }

                throw;
            }

            // 6. Final Clean up of Backups (Success case)
            if (backupTargetPath != null && File.Exists(backupTargetPath))
            {
                File.Delete(backupTargetPath);
            }
            if (backupOriginalPath != null && File.Exists(backupOriginalPath))
            {
                File.Delete(backupOriginalPath);
            }
        }
        finally
        {
            // Clean up temp directory
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore directory cleanup exceptions to not mask real error
                }
            }

            // Clean up temporary zip file if it wasn't successfully moved
            if (tempCbzPath != null && File.Exists(tempCbzPath))
            {
                try
                {
                    File.Delete(tempCbzPath);
                }
                catch
                {
                    // Ignore temp file cleanup exceptions
                }
            }
        }
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
        return Path.GetRelativePath(relativeTo, path);
    }

    // Validates an XML file against the embedded ComicInfo XSD.
    internal static void ValidateXml(string xmlPath)
    {
        if (!File.Exists(xmlPath))
        {
            return;
        }

        var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schema", "ComicInfo.xsd");
        if (!File.Exists(schemaPath))
        {
            AppLogger.LogWarning($"XSD schema not found at '{schemaPath}'. Skipping validation.");
            return;
        }

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema
        };
        settings.Schemas.Add(null, schemaPath);
        settings.ValidationEventHandler += (sender, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                AppLogger.LogWarning($"Schema validation error in {xmlPath}: {args.Message}");
            }
            else
            {
                AppLogger.LogWarning($"Schema validation warning in {xmlPath}: {args.Message}");
            }
        };

        try
        {
            using var reader = XmlReader.Create(xmlPath, settings);
            while (reader.Read()) { }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Schema validation exception in {xmlPath}: {ex.Message}");
        }
    }

    #region AI Agent Helper APIs

    /// <summary>
    /// Reads comic metadata and serializes it as clean JSON.
    /// </summary>
    public string ReadMetadataAsJson(string filePath)
    {
        var info = ReadMetadata(filePath);
        return JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Edits metadata using a JSON patch object string.
    /// </summary>
    public void EditMetadataFromJson(string filePath, string jsonPatch)
    {
        EditMetadata(filePath, comic => ApplyJsonPatch(comic, jsonPatch));
    }

    /// <summary>
    /// Bulk edits comic files in a directory using a JSON patch object string.
    /// </summary>
    public BulkEditReport BulkEditMetadataFromJson(string directoryPath, string jsonPatch, bool recursive = false)
    {
        return BulkEditMetadata(directoryPath, comic => ApplyJsonPatch(comic, jsonPatch), recursive);
    }

    /// <summary>
    /// Compares original metadata with a proposed JSON patch and returns property-level diffs.
    /// </summary>
    public List<MetadataDiffItem> GetMetadataDiff(string filePath, string jsonPatch)
    {
        var current = ReadMetadata(filePath);
        var updated = current.Clone();
        ApplyJsonPatch(updated, jsonPatch);

        var diffs = new List<MetadataDiffItem>();
        var properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var oldVal = prop.GetValue(current);
            var newVal = prop.GetValue(updated);

            if (!Equals(oldVal, newVal))
            {
                diffs.Add(new MetadataDiffItem(prop.Name, oldVal, newVal));
            }
        }

        return diffs;
    }

    /// <summary>
    /// Extracts the front cover or first image entry from a CBZ or CBR comic archive to outputFilePath.
    /// Returns outputFilePath if successful, or null if no image entries are found.
    /// </summary>
    public string? ExtractCoverImage(string comicFilePath, string outputFilePath)
    {
        if (!File.Exists(comicFilePath))
        {
            throw new FileNotFoundException($"Comic file not found: {comicFilePath}");
        }

        string[] validExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

        using (Stream stream = File.OpenRead(comicFilePath))
        using (var archive = ArchiveFactory.OpenArchive(stream))
        {
            var imageEntries = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null && validExtensions.Contains(Path.GetExtension(e.Key).ToLowerInvariant()))
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
    }

    /// <summary>
    /// Exports the JSON Schema for ComicInfo objects.
    /// </summary>
    public static string ExportJsonSchema()
    {
        var properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                p => p.Name,
                p => new
                {
                    type = GetJsonTypeName(p.PropertyType),
                    nullable = true,
                    description = $"ComicInfo field '{p.Name}'"
                }
            );

        var schema = new
        {
            type = "object",
            title = "ComicInfo",
            description = "XML metadata schema standard for comic archives (.cbz / .cbr)",
            properties = properties
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Mutates a ComicInfo object in-place using key-value pairs in a JSON patch string.
    /// Returns a list of warning messages for unrecognized or unwriteable property names.
    /// </summary>
    public static List<string> ApplyJsonPatch(ComicInfo comicInfo, string jsonPatch)
    {
        var warnings = new List<string>();
        using var doc = JsonDocument.Parse(jsonPatch);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("JSON patch must be a JSON object string.");
        }

        var properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propMap = properties.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var jsonProp in root.EnumerateObject())
        {
            if (propMap.TryGetValue(jsonProp.Name, out var prop) && prop.CanWrite)
            {
                var value = ConvertJsonElement(jsonProp.Value, prop.PropertyType);
                prop.SetValue(comicInfo, value);
            }
            else
            {
                warnings.Add($"Unknown or unwriteable property '{jsonProp.Name}'");
            }
        }

        return warnings;
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
            return null;

        Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(string))
            return element.GetString();

        if (underlyingType == typeof(int))
            return element.GetInt32();

        if (underlyingType == typeof(long))
            return element.GetInt64();

        if (underlyingType == typeof(bool))
            return element.GetBoolean();

        if (underlyingType == typeof(double))
            return element.GetDouble();

        if (underlyingType.IsEnum)
        {
            if (element.ValueKind == JsonValueKind.String)
                return Enum.Parse(underlyingType, element.GetString()!, true);
            if (element.ValueKind == JsonValueKind.Number)
                return Enum.ToObject(underlyingType, element.GetInt32());
        }

        return null;
    }

    private static string GetJsonTypeName(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(double)) return "number";
        if (underlying == typeof(bool)) return "boolean";
        return "string";
    }

    public byte[]? ExtractCoverImageBytes(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

        string ext = Path.GetExtension(filePath) ?? "";

        // Fast path for .cbz (ZIP)
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
                    AppLogger.LogDebug($"[MetadataEditor] Extracted cover for '{fileName}' via fast seek ({bestEntry.FullName}, {result.Length} bytes) in {sw.ElapsedMilliseconds}ms.");
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[MetadataEditor] Cover fast seek failed for '{fileName}' ({ex.Message}). Retrying with sequential NonSeekableStream...");
            }

            // 2. Sequential forward-only streaming
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
                            AppLogger.LogDebug($"[MetadataEditor] Extracted explicit cover for '{fileName}' via sequential stream in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                            return bytes;
                        }

                        firstImage ??= bytes;
                    }
                }

                if (firstImage != null)
                {
                    AppLogger.LogDebug($"[MetadataEditor] Extracted first image cover for '{fileName}' via sequential stream in {seqSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                    return firstImage;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug($"[MetadataEditor] Cover sequential streaming failed for '{fileName}' ({ex.Message}). Retrying with SharpCompress...");
            }
        }

        try
        {
            var scSw = System.Diagnostics.Stopwatch.StartNew();
            using var stream = OpenReadOptimized(filePath);
            using var archive = ArchiveFactory.OpenArchive(stream, new ReaderOptions { LookForHeader = true });

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
                AppLogger.LogDebug($"[MetadataEditor] Extracted cover for '{fileName}' via SharpCompress fallback in {scSw.ElapsedMilliseconds}ms (Total: {sw.ElapsedMilliseconds}ms).");
                return result;
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.LogDebug($"[MetadataEditor] Cover extraction failed for '{fileName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Computes the 64-bit perceptual dHash of the comic's cover image.
    /// </summary>
    public ulong GetCoverHash(string filePath)
    {
        var bytes = ExtractCoverImageBytes(filePath);
        return bytes != null && bytes.Length > 0 ? Images.PerceptualHashService.ComputeDHash(bytes) : 0;
    }

    private static bool IsImageFileName(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        ext = ext.ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif" || ext == ".bmp";
    }

    #endregion
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


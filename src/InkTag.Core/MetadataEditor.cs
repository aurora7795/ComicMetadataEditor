using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Exceptions;
using InkTag.Core.Logging;
using InkTag.Core.Parsing;

namespace InkTag.Core;

public class BulkEditReport
{
    public int TotalFound { get; set; }
    public List<string> Successes { get; } = new();
    public List<(string Path, Exception Exception)> Failures { get; } = new();
}

public class PageRemovalResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int RemovedCount { get; set; }
    public List<string> RemovedEntries { get; set; } = new();
    public int OriginalPageCount { get; set; }
    public int FinalPageCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public record MetadataDiffItem(string PropertyName, object? OldValue, object? NewValue);

public record BulkEditProgress(int Processed, int TotalFound, string CurrentFile, bool IsSuccess, Exception? Exception = null);

/// <summary>
/// Primary entry point and façade for reading, editing, and managing comic book metadata (.cbz / .cbr).
/// Delegates specialized operations to ComicArchiveHandler, ArchiveSwapService, and ComicInfoXmlSanitizer.
/// </summary>
public class MetadataEditor
{
    /// <summary>
    /// Checks whether a given path is a valid comic archive (.cbz / .cbr), excluding AppleDouble, macOS resource forks, and hidden system files.
    /// </summary>
    public static bool IsSupportedComicFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith('.') || fileName.StartsWith("._", StringComparison.Ordinal))
        {
            return false;
        }

        string normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/.AppleDouble/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.Trash/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.Trash-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bulk edits the metadata in all CBR and CBZ files within the specified directory.
    /// Uses lazy file enumeration to minimize memory allocations across large collections.
    /// </summary>
    public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction, bool recursive = false)
    {
        var report = new BulkEditReport();

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Use EnumerateFiles for lazy streaming
        var comicFiles = Directory.EnumerateFiles(directoryPath, "*.*", searchOption)
            .Where(IsSupportedComicFile)
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
    /// Asynchronously bulk edits metadata in all comic archives within the specified directory.
    /// </summary>
    public async Task<BulkEditReport> BulkEditMetadataAsync(
        string directoryPath,
        Action<ComicInfo> editAction,
        bool recursive = false,
        IProgress<BulkEditProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new BulkEditReport();

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var comicFiles = await Task.Run(() =>
            Directory.EnumerateFiles(directoryPath, "*.*", searchOption)
                .Where(IsSupportedComicFile)
                .ToList(), cancellationToken).ConfigureAwait(false);

        report.TotalFound = comicFiles.Count;
        int processedCount = 0;

        foreach (var file in comicFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await EditMetadataAsync(file, editAction, cancellationToken: cancellationToken).ConfigureAwait(false);
                report.Successes.Add(file);
                processedCount++;
                progress?.Report(new BulkEditProgress(processedCount, report.TotalFound, file, true));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Failures.Add((file, ex));
                processedCount++;
                progress?.Report(new BulkEditProgress(processedCount, report.TotalFound, file, false, ex));
            }
        }

        return report;
    }

    /// <summary>
    /// Opens a network-optimized FileStream with 64KB buffer and non-exclusive FileShare.ReadWrite.
    /// </summary>
    public static FileStream OpenReadOptimized(string filePath, int bufferSize = 65536) =>
        ComicArchiveHandler.OpenReadOptimized(filePath, bufferSize);

    public bool HasMetadata(string filePath, CancellationToken cancellationToken = default)
    {
        ReadMetadata(filePath, out bool hasEmbeddedXml, out _, cancellationToken);
        return hasEmbeddedXml;
    }

    public ComicInfo ReadMetadata(string filePath, CancellationToken cancellationToken = default) => 
        ComicArchiveHandler.ReadMetadata(filePath, out _, out _, cancellationToken);

    public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, CancellationToken cancellationToken = default) =>
        ComicArchiveHandler.ReadMetadata(filePath, out hasEmbeddedXml, out _, cancellationToken);

    public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, out bool usedSequentialFallback, CancellationToken cancellationToken = default) =>
        ComicArchiveHandler.ReadMetadata(filePath, out hasEmbeddedXml, out usedSequentialFallback, cancellationToken);

    public Task<ComicInfo> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default) =>
        ComicArchiveHandler.ReadMetadataAsync(filePath, cancellationToken);

    public void EditMetadata(
        string filePath,
        Action<ComicInfo> editAction,
        string? batchJobId = null,
        string? changeReason = null,
        string? coverDHash = null,
        string? matchedThumbnailUrl = null,
        double? matchConfidence = null,
        double? visualSimilarity = null)
    {
        ArchiveSwapService.EditMetadata(
            filePath,
            editAction,
            batchJobId,
            changeReason,
            coverDHash,
            matchedThumbnailUrl,
            matchConfidence,
            visualSimilarity);
    }

    public Task EditMetadataAsync(
        string filePath,
        Action<ComicInfo> editAction,
        string? batchJobId = null,
        string? changeReason = null,
        string? coverDHash = null,
        string? matchedThumbnailUrl = null,
        double? matchConfidence = null,
        double? visualSimilarity = null,
        CancellationToken cancellationToken = default)
    {
        return ArchiveSwapService.EditMetadataAsync(
            filePath,
            editAction,
            batchJobId,
            changeReason,
            coverDHash,
            matchedThumbnailUrl,
            matchConfidence,
            visualSimilarity,
            cancellationToken);
    }

    /// <summary>
    /// Replaces the embedded ComicInfo.xml in a comic archive with the provided XML string content.
    /// </summary>
    public void UpdateMetadataXml(string filePath, string xmlContent, bool createBackup = true) =>
        ArchiveSwapService.UpdateMetadataXml(filePath, xmlContent, createBackup);

    /// <summary>
    /// Asynchronously replaces the embedded ComicInfo.xml in a comic archive with the provided XML string content.
    /// </summary>
    public Task UpdateMetadataXmlAsync(string filePath, string xmlContent, bool createBackup = true, CancellationToken cancellationToken = default) =>
        Task.Run(() => ArchiveSwapService.UpdateMetadataXml(filePath, xmlContent, createBackup, cancellationToken), cancellationToken);

    internal static ComicInfo DeserializeComicInfo(Stream stream) =>
        ComicInfoXmlSanitizer.DeserializeComicInfo(stream);

    internal static ComicInfo DeserializeComicInfo(string rawXml) =>
        ComicInfoXmlSanitizer.DeserializeComicInfo(rawXml);

    internal static string SanitizeComicInfoXml(string rawXml) =>
        ComicInfoXmlSanitizer.SanitizeComicInfoXml(rawXml);

    internal static ComicInfo FallbackParseComicInfoXml(string rawXml) =>
        ComicInfoXmlSanitizer.FallbackParseComicInfoXml(rawXml);

    internal static void ValidateXml(string xmlPath) =>
        ComicInfoXmlSanitizer.ValidateXml(xmlPath);

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
    /// Gets naturally sorted image entry paths contained inside a comic archive.
    /// </summary>
    public List<string> GetImageEntries(string filePath) =>
        ComicArchiveHandler.GetImageEntries(filePath);

    /// <summary>
    /// Extracts a specific 0-based page image (default 0 / cover) from a CBZ or CBR comic archive to outputFilePath.
    /// </summary>
    public string? ExtractCoverImage(string comicFilePath, string outputFilePath, int pageIndex = 0) =>
        ComicArchiveHandler.ExtractCoverImage(comicFilePath, outputFilePath, pageIndex);

    /// <summary>
    /// Strips the first page (index 0 / provider title page) from a comic archive.
    /// </summary>
    public PageRemovalResult StripFirstPage(string filePath) =>
        ComicArchiveHandler.StripFirstPage(filePath);

    /// <summary>
    /// Removes one or more pages by 0-based page index from a CBZ or CBR comic archive.
    /// Updates ComicInfo.xml PageCount and renumbers Pages collection elements.
    /// Safely repacks the archive using atomic temporary swap and backup rollback.
    /// </summary>
    public PageRemovalResult RemoveArchivePages(string filePath, IEnumerable<int> pageIndices) =>
        ComicArchiveHandler.RemoveArchivePages(filePath, pageIndices);

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

    /// <summary>
    /// Extracts the raw image bytes for a specific 0-based page index (default 0 / cover) from a CBZ or CBR archive.
    /// </summary>
    public byte[]? ExtractCoverImageBytes(string filePath, int pageIndex = 0) =>
        ComicArchiveHandler.ExtractCoverImageBytes(filePath, pageIndex);

    public Task<byte[]?> ExtractCoverImageBytesAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default) =>
        ComicArchiveHandler.ExtractCoverImageBytesAsync(filePath, pageIndex, cancellationToken);

    /// <summary>
    /// Computes the 64-bit perceptual dHash of the comic's cover or specific page image.
    /// </summary>
    public ulong GetCoverHash(string filePath, int pageIndex = 0) =>
        ComicArchiveHandler.GetCoverHash(filePath, pageIndex);

    public Task<ulong> GetCoverHashAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default) =>
        ComicArchiveHandler.GetCoverHashAsync(filePath, pageIndex, cancellationToken);

    /// <summary>
    /// Extracts cover hashes and raw byte payloads for up to maxPages (default first 2 pages) in order.
    /// Useful for evaluating candidate cover vs. provider intro pages.
    /// </summary>
    public List<(int PageIndex, ulong Hash, byte[] Bytes)> GetCandidateCoverHashes(string filePath, int maxPages = 2) =>
        ComicArchiveHandler.GetCandidateCoverHashes(filePath, maxPages);

    public static bool IsIgnoredSystemEntry(string? entryName) =>
        ComicArchiveHandler.IsIgnoredSystemEntry(entryName);

    #endregion
}

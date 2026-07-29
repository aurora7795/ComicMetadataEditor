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

namespace ComicMetadataEditor;

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
    /// <returns>A report containing statistics and failure logs.</returns>
    public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)
    {
        var report = new BulkEditReport();

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        // Support both .cbr and .cbz files
        var comicFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
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

    public ComicInfo ReadMetadata(string filePath)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            using (Stream stream = File.OpenRead(filePath))
            using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
            {
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory && 
                        reader.Entry.Key != null &&
                        Path.GetFileName(reader.Entry.Key).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        reader.WriteEntryToDirectory(tempDir, new ExtractionOptions { Overwrite = true, ExtractFullPath = false });
                    }
                }
            }

            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            if (File.Exists(xmlPath))
            {
                ValidateXml(xmlPath);
                XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
                using (FileStream fs = new FileStream(xmlPath, FileMode.Open, FileAccess.Read))
                {
                    return (ComicInfo)serializer.Deserialize(fs)!;
                }
            }
            return new ComicInfo();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
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
            // 1. Extract the archive contents
            using (Stream stream = File.OpenRead(filePath))
            using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
            {
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                    {
                        reader.WriteEntryToDirectory(tempDir, new ExtractionOptions());
                    }
                }
            }

            // 2. Find and deserialize / create ComicInfo.xml
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            ComicInfo comicInfo;

            if (File.Exists(xmlPath))
            {
                // Validate the XML against the official schema before deserialization
                ValidateXml(xmlPath);
                XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
                using (FileStream fs = new FileStream(xmlPath, FileMode.Open, FileAccess.Read))
                {
                    comicInfo = (ComicInfo)serializer.Deserialize(fs)!;
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

            // Verify readability using SharpCompress Reader
            using (Stream stream = File.OpenRead(tempCbzPath))
            using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
            {
                bool hasEntries = false;
                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                    {
                        hasEntries = true;
                    }
                }
                if (!hasEntries)
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
        if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentNullException(nameof(relativeTo));
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

        Uri uri1 = new Uri(relativeTo + (relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()) ? "" : Path.DirectorySeparatorChar.ToString()));
        Uri uri2 = new Uri(path);

        Uri relativeUri = uri1.MakeRelativeUri(uri2);

        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }
    // Validates an XML file against the embedded ComicInfo XSD.
    private static void ValidateXml(string xmlPath)
    {
        // Resolve schema path relative to the executing assembly's base directory.
        var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schema", "ComicInfo.xsd");
        if (!File.Exists(schemaPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: XSD schema not found at '{schemaPath}'. Skipping validation.");
            Console.ResetColor();
            return;
        }

        var settings = new XmlReaderSettings();
        settings.ValidationType = ValidationType.Schema;
        settings.Schemas.Add(null, schemaPath);
        settings.ValidationEventHandler += (sender, args) =>
        {
            Console.WriteLine($"Schema validation warning: {args.Message}");
        };

        using var reader = XmlReader.Create(xmlPath, settings);
        // Read entire document to trigger validation.
        while (reader.Read()) { }
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
    public BulkEditReport BulkEditMetadataFromJson(string directoryPath, string jsonPatch)
    {
        return BulkEditMetadata(directoryPath, comic => ApplyJsonPatch(comic, jsonPatch));
    }

    /// <summary>
    /// Compares original metadata with a proposed JSON patch and returns property-level diffs.
    /// </summary>
    public List<MetadataDiffItem> GetMetadataDiff(string filePath, string jsonPatch)
    {
        var current = ReadMetadata(filePath);
        var updated = ReadMetadata(filePath);
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

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            using (Stream stream = File.OpenRead(comicFilePath))
            using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
            {
                string? coverCandidatePath = null;
                var imageFiles = new List<string>();

                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory || reader.Entry.Key == null) continue;

                    string fileName = Path.GetFileName(reader.Entry.Key);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();

                    if (validExtensions.Contains(ext))
                    {
                        string targetPath = Path.Combine(tempDir, Guid.NewGuid().ToString() + ext);
                        using (var fs = File.OpenWrite(targetPath))
                        {
                            reader.WriteEntryTo(fs);
                        }
                        imageFiles.Add(targetPath);

                        if (fileName.Contains("cover", StringComparison.OrdinalIgnoreCase) && coverCandidatePath == null)
                        {
                            coverCandidatePath = targetPath;
                        }
                    }
                }

                if (coverCandidatePath == null && imageFiles.Count > 0)
                {
                    coverCandidatePath = imageFiles.OrderBy(f => f).First();
                }

                if (coverCandidatePath != null)
                {
                    string? dir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(coverCandidatePath, outputFilePath, true);
                    return outputFilePath;
                }

                return null;
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
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
    /// </summary>
    public static void ApplyJsonPatch(ComicInfo comicInfo, string jsonPatch)
    {
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
        }
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

    #endregion
}


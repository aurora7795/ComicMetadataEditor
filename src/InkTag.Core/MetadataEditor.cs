using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
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

    internal static ComicInfo DeserializeComicInfo(Stream stream)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            string rawXml = reader.ReadToEnd();
            return DeserializeComicInfo(rawXml);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to read ComicInfo XML stream: {ex.Message}");
            return new ComicInfo();
        }
    }

    internal static ComicInfo DeserializeComicInfo(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return new ComicInfo();
        }

        try
        {
            string sanitizedXml = SanitizeComicInfoXml(rawXml);
            XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
            using var stringReader = new StringReader(sanitizedXml);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            });
            var result = (ComicInfo)serializer.Deserialize(xmlReader)!;
            return result ?? new ComicInfo();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to deserialize ComicInfo XML ({ex.Message}). Attempting fallback parsing...");
            try
            {
                return FallbackParseComicInfoXml(rawXml);
            }
            catch (Exception fallbackEx)
            {
                AppLogger.LogWarning($"Fallback XML parser failed: {fallbackEx.Message}");
                return new ComicInfo();
            }
        }
    }

    internal static string SanitizeComicInfoXml(string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return "<ComicInfo />";

        // 1. Strip invalid XML 1.0 control characters (except \t, \n, \r)
        var sb = new StringBuilder(rawXml.Length);
        foreach (char c in rawXml)
        {
            if (c == 0x9 || c == 0xA || c == 0xD ||
                (c >= 0x20 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD))
            {
                sb.Append(c);
            }
        }
        string cleaned = sb.ToString();

        try
        {
            // 2. Parse into XDocument for tree sanitization
            var xdoc = XDocument.Parse(cleaned, LoadOptions.PreserveWhitespace);
            var root = xdoc.Root;
            if (root == null) return cleaned;

            // Remove any xmlns declarations to prevent serialization namespace mismatch
            root.Attributes().Where(a => a.IsNamespaceDeclaration || a.Name.LocalName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)).Remove();
            root.Name = "ComicInfo";

            var elementsToRemove = new List<XElement>();

            // List of numeric elements where empty or non-numeric inner text causes XmlSerializer format errors
            var integerElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Count", "Volume", "AlternateCount", "Year", "Month", "Day", "PageCount"
            };

            foreach (var elem in root.Elements())
            {
                string localName = elem.Name.LocalName;
                string val = elem.Value?.Trim() ?? "";

                if (integerElements.Contains(localName))
                {
                    if (string.IsNullOrEmpty(val) || !int.TryParse(val, out int intVal))
                    {
                        elementsToRemove.Add(elem);
                    }
                    else if (localName.Equals("Volume", StringComparison.OrdinalIgnoreCase))
                    {
                        if (intVal < 0) elementsToRemove.Add(elem);
                        else elem.Value = intVal.ToString();
                    }
                    else
                    {
                        // ComicRack uses 0 or -1 or empty tags for undefined Year, Month, Day, Count, PageCount
                        if (intVal <= 0) elementsToRemove.Add(elem);
                        else elem.Value = intVal.ToString();
                    }
                }
                else if (localName.Equals("CommunityRating", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(val) || !decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal ratingVal))
                    {
                        elementsToRemove.Add(elem);
                    }
                    else
                    {
                        elem.Value = ratingVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                else if (localName.Equals("Manga", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(val) || val.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "Unknown";
                    }
                    else if (val.Equals("1", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "Yes";
                    }
                    else if (val.Equals("0", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase) || val.Equals("no", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "No";
                    }
                    else if (val.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 || val.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase))
                    {
                        elem.Value = "YesAndRightToLeft";
                    }
                    else
                    {
                        elementsToRemove.Add(elem);
                    }
                }
                else if (localName.Equals("BlackAndWhite", StringComparison.OrdinalIgnoreCase))
                {
                    if (val.Equals("1", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        elem.Value = "Yes";
                    else if (val.Equals("0", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
                        elem.Value = "No";
                    else if (string.IsNullOrEmpty(val))
                        elementsToRemove.Add(elem);
                }
                else if (localName.Equals("Pages", StringComparison.OrdinalIgnoreCase))
                {
                    int pageIdx = 0;
                    foreach (var pageElem in elem.Elements().Where(p => p.Name.LocalName.Equals("Page", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Validate Image attribute
                        var imgAttr = pageElem.Attribute("Image");
                        if (imgAttr == null || !int.TryParse(imgAttr.Value, out _))
                        {
                            pageElem.SetAttributeValue("Image", pageIdx);
                        }

                        // Clean integer attributes
                        foreach (string attrName in new[] { "ImageSize", "ImageWidth", "ImageHeight" })
                        {
                            var attr = pageElem.Attribute(attrName);
                            if (attr != null && (!long.TryParse(attr.Value, out long numVal) || numVal < 0))
                            {
                                attr.Remove();
                            }
                        }

                        // Clean DoublePage boolean attribute
                        var dpAttr = pageElem.Attribute("DoublePage");
                        if (dpAttr != null)
                        {
                            if (dpAttr.Value.Equals("1", StringComparison.OrdinalIgnoreCase) || dpAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                                dpAttr.Value = "true";
                            else if (dpAttr.Value.Equals("0", StringComparison.OrdinalIgnoreCase) || dpAttr.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
                                dpAttr.Value = "false";
                            else if (!bool.TryParse(dpAttr.Value, out _))
                                dpAttr.Remove();
                        }

                        pageIdx++;
                    }
                }
            }

            foreach (var toRemove in elementsToRemove)
            {
                toRemove.Remove();
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            // If XDocument parsing fails on malformed XML, regex strip empty numeric tags
            string fixedXml = Regex.Replace(cleaned, @"<(Count|Volume|AlternateCount|Year|Month|Day|PageCount)[^>]*>\s*</\1>", "");
            fixedXml = Regex.Replace(fixedXml, @"<(Count|Volume|AlternateCount|Year|Month|Day|PageCount)[^>]*/>", "");
            fixedXml = Regex.Replace(fixedXml, @"<(Manga)[^>]*>\s*(true|1)\s*</\1>", "<Manga>Yes</Manga>", RegexOptions.IgnoreCase);
            fixedXml = Regex.Replace(fixedXml, @"<(Manga)[^>]*>\s*(false|0)\s*</\1>", "<Manga>No</Manga>", RegexOptions.IgnoreCase);
            return fixedXml;
        }
    }

    internal static ComicInfo FallbackParseComicInfoXml(string rawXml)
    {
        var info = new ComicInfo();
        string GetTag(string tagName)
        {
            var match = Regex.Match(rawXml, $@"<{tagName}[^>]*>(.*?)</{tagName}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        int? GetIntTag(string tagName)
        {
            string s = GetTag(tagName);
            return int.TryParse(s, out int val) && val >= 0 ? val : null;
        }

        info.Title = GetTag("Title") is { Length: > 0 } t ? t : null;
        info.Series = GetTag("Series") is { Length: > 0 } s ? s : null;
        info.Number = GetTag("Number") is { Length: > 0 } n ? n : null;
        info.Count = GetIntTag("Count");
        info.Volume = GetIntTag("Volume");
        info.AlternateSeries = GetTag("AlternateSeries") is { Length: > 0 } aser ? aser : null;
        info.AlternateNumber = GetTag("AlternateNumber") is { Length: > 0 } anum ? anum : null;
        info.AlternateCount = GetIntTag("AlternateCount");
        info.Summary = GetTag("Summary") is { Length: > 0 } sum ? sum : null;
        info.Notes = GetTag("Notes") is { Length: > 0 } not ? not : null;
        info.Year = GetIntTag("Year");
        info.Month = GetIntTag("Month");
        info.Day = GetIntTag("Day");
        info.Writer = GetTag("Writer") is { Length: > 0 } w ? w : null;
        info.Penciller = GetTag("Penciller") is { Length: > 0 } pen ? pen : null;
        info.Inker = GetTag("Inker") is { Length: > 0 } ink ? ink : null;
        info.Colorist = GetTag("Colorist") is { Length: > 0 } col ? col : null;
        info.Letterer = GetTag("Letterer") is { Length: > 0 } let ? let : null;
        info.CoverArtist = GetTag("CoverArtist") is { Length: > 0 } cov ? cov : null;
        info.Editor = GetTag("Editor") is { Length: > 0 } ed ? ed : null;
        info.Publisher = GetTag("Publisher") is { Length: > 0 } pub ? pub : null;
        info.Imprint = GetTag("Imprint") is { Length: > 0 } imp ? imp : null;
        info.Genre = GetTag("Genre") is { Length: > 0 } gen ? gen : null;
        info.Tags = GetTag("Tags") is { Length: > 0 } tag ? tag : null;
        info.Web = GetTag("Web") is { Length: > 0 } web ? web : null;
        info.PageCount = GetIntTag("PageCount");
        info.LanguageISO = GetTag("LanguageISO") is { Length: > 0 } lang ? lang : null;
        info.Format = GetTag("Format") is { Length: > 0 } fmt ? fmt : null;
        info.BlackAndWhite = GetTag("BlackAndWhite") is { Length: > 0 } bw ? bw : null;
        info.Characters = GetTag("Characters") is { Length: > 0 } ch ? ch : null;
        info.Teams = GetTag("Teams") is { Length: > 0 } tm ? tm : null;
        info.Locations = GetTag("Locations") is { Length: > 0 } loc ? loc : null;
        info.ScanInformation = GetTag("ScanInformation") is { Length: > 0 } scn ? scn : null;
        info.StoryArc = GetTag("StoryArc") is { Length: > 0 } arc ? arc : null;
        info.SeriesGroup = GetTag("SeriesGroup") is { Length: > 0 } sg ? sg : null;
        info.AgeRating = GetTag("AgeRating") is { Length: > 0 } ar ? ar : null;
        info.MainCharacterOrTeam = GetTag("MainCharacterOrTeam") is { Length: > 0 } mct ? mct : null;
        info.Review = GetTag("Review") is { Length: > 0 } rev ? rev : null;

        string crStr = GetTag("CommunityRating");
        if (decimal.TryParse(crStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal crVal))
            info.CommunityRating = crVal;

        string mangaStr = GetTag("Manga");
        if (mangaStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) || mangaStr.Equals("1") || mangaStr.Equals("true", StringComparison.OrdinalIgnoreCase))
            info.Manga = MangaDirection.Yes;
        else if (mangaStr.Equals("YesAndRightToLeft", StringComparison.OrdinalIgnoreCase) || mangaStr.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0)
            info.Manga = MangaDirection.YesAndRightToLeft;
        else if (mangaStr.Equals("No", StringComparison.OrdinalIgnoreCase) || mangaStr.Equals("0") || mangaStr.Equals("false", StringComparison.OrdinalIgnoreCase))
            info.Manga = MangaDirection.No;

        return info;
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
            string? existingXmlFile = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

            ComicInfo comicInfo;
            if (existingXmlFile != null && File.Exists(existingXmlFile))
            {
                try
                {
                    // Validate the XML against the official schema before deserialization (logs warnings)
                    ValidateXml(existingXmlFile);
                    string xmlContent = File.ReadAllText(existingXmlFile);
                    comicInfo = DeserializeComicInfo(xmlContent);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Failed to read existing ComicInfo.xml during edit: {ex.Message}. Initializing new metadata.");
                    comicInfo = new ComicInfo();
                }

                // If existing XML was in a subfolder or different case, remove it so clean root ComicInfo.xml is written
                if (!existingXmlFile.Equals(Path.Combine(tempDir, "ComicInfo.xml"), StringComparison.Ordinal))
                {
                    try { File.Delete(existingXmlFile); } catch { }
                }
            }
            else
            {
                comicInfo = new ComicInfo();
            }

            // Apply edits
            editAction(comicInfo);

            // Serialize back to clean XML in root of temp directory
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
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
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
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


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            throw new XmlSchemaValidationException($"XML validation error: {args.Message}", args.Exception);
        };

        using var reader = XmlReader.Create(xmlPath, settings);
        // Read entire document to trigger validation.
        while (reader.Read()) { }
    }
}


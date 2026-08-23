using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using InkTag.Core.Exceptions;
using InkTag.Core.Logging;

namespace InkTag.Core;

/// <summary>
/// Internal service responsible for extracting, modifying, repacking, and atomically swapping
/// comic archives with robust .bak rollback guarantees, integrity validation, and zip-slip protection.
/// </summary>
internal static class ArchiveSwapService
{
    /// <summary>
    /// Safely performs an in-place metadata edit on a comic archive (.cbz / .cbr).
    /// Converts CBR archives to CBZ format on save.
    /// </summary>
    public static void EditMetadata(
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
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(editAction);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Comic file not found: {filePath}", filePath);
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
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Extract the archive contents safely
            using (Stream stream = File.OpenRead(filePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(tempDir, new ExtractionOptions { Overwrite = true, ExtractFullPath = false });
                    }
                }
            }

            // Verify all extracted files remain strictly contained within tempDir (Zip-Slip defense)
            string canonicalTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string extractedFile in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string canonicalFile = Path.GetFullPath(extractedFile);
                if (!canonicalFile.StartsWith(canonicalTempDir, StringComparison.OrdinalIgnoreCase))
                {
                    var unsafeEx = new InvalidDataException($"Archive entry '{extractedFile}' escapes extraction target directory.");
                    throw new UnsafeArchiveEntryException(unsafeEx.Message, extractedFile, filePath, unsafeEx);
                }
            }

            // 2. Find and deserialize / create ComicInfo.xml
            string? existingXmlFile = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

            ComicInfo comicInfo;
            string? originalXmlContent = null;
            if (existingXmlFile != null && File.Exists(existingXmlFile))
            {
                try
                {
                    // Validate the XML against the official schema before deserialization (logs warnings)
                    ComicInfoXmlSanitizer.ValidateXml(existingXmlFile);
                    originalXmlContent = File.ReadAllText(existingXmlFile);
                    comicInfo = ComicInfoXmlSanitizer.DeserializeComicInfo(originalXmlContent);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Failed to read existing ComicInfo.xml during edit: {ex.Message}. Initializing new metadata.");
                    comicInfo = new ComicInfo();
                }

                // If existing XML was in a subfolder or different case, remove it so clean root ComicInfo.xml is written
                if (!existingXmlFile.Equals(Path.Combine(tempDir, "ComicInfo.xml"), StringComparison.Ordinal))
                {
                    try
                    {
                        File.Delete(existingXmlFile);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogDebug($"Failed to clean up legacy ComicInfo.xml at '{existingXmlFile}': {ex.Message}");
                    }
                }
            }
            else
            {
                // If no ComicInfo.xml existed in archive, read existing legacy metadata (e.g. from zip comment)
                comicInfo = ComicArchiveHandler.ReadMetadata(filePath, cancellationToken);
            }

            // Create automated pre-write metadata backup snapshot with provenance
            try
            {
                var backupService = new InkTag.Core.Backup.MetadataBackupService();
                backupService.CreateBackup(
                    filePath,
                    originalXmlContent,
                    "EditMetadata",
                    batchJobId: batchJobId,
                    coverDHash: coverDHash,
                    matchedThumbnailUrl: matchedThumbnailUrl,
                    matchConfidence: matchConfidence,
                    visualSimilarity: visualSimilarity,
                    changeReason: changeReason);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Pre-write metadata backup failed for '{filePath}': {ex.Message}");
            }

            // Clean up legacy ComicBookInfo.json files so the repacked archive contains only ComicInfo.xml
            foreach (var legacyCbiFile in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).Equals("ComicBookInfo.json", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Equals("ComicBookInfo", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    File.Delete(legacyCbiFile);
                }
                catch (Exception ex)
                {
                    AppLogger.LogDebug($"Failed to delete legacy CBI file '{legacyCbiFile}': {ex.Message}");
                }
            }

            // Apply caller modifications
            editAction(comicInfo);

            // Serialize back to clean XML in root of temp directory
            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            using (FileStream fs = new FileStream(xmlPath, FileMode.Create, FileAccess.Write))
            {
                ComicInfoXmlSanitizer.SerializeComicInfo(comicInfo, fs);
            }

            // 3. Safe repack: Repack into a temporary CBZ archive inside the temporary path
            tempCbzPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cbz.tmp");
            using (Stream stream = File.OpenWrite(tempCbzPath))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entryName = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                    writer.Write(entryName, file);
                }
            }

            // 4. Validate the repackaged temp archive
            FileInfo tempCbzInfo = new FileInfo(tempCbzPath);
            if (!tempCbzInfo.Exists || tempCbzInfo.Length == 0)
            {
                var corruptEx = new InvalidDataException("Generated temporary archive is empty or invalid.");
                throw new ComicArchiveCorruptException(corruptEx.Message, tempCbzPath, corruptEx);
            }

            using (Stream stream = File.OpenRead(tempCbzPath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                if (!archive.Entries.Any(e => !e.IsDirectory))
                {
                    var corruptEx = new InvalidDataException("Generated temporary archive contains no entries.");
                    throw new ComicArchiveCorruptException(corruptEx.Message, tempCbzPath, corruptEx);
                }
            }

            // 5. Atomic-like Swap
            if (File.Exists(targetPath))
            {
                backupTargetPath = targetPath + "." + Guid.NewGuid().ToString() + ".bak";
                File.Move(targetPath, backupTargetPath);
            }

            if (!filePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                backupOriginalPath = filePath + "." + Guid.NewGuid().ToString() + ".bak";
                File.Move(filePath, backupOriginalPath);
            }

            try
            {
                File.Move(tempCbzPath, targetPath);
                tempCbzPath = null;
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

            // 6. Final clean up of backups
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
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch (Exception ex) { AppLogger.LogDebug($"Temp directory cleanup notice for '{tempDir}': {ex.Message}"); }
            }

            if (tempCbzPath != null && File.Exists(tempCbzPath))
            {
                try { File.Delete(tempCbzPath); } catch (Exception ex) { AppLogger.LogDebug($"Temporary zip cleanup notice for '{tempCbzPath}': {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Asynchronously performs an in-place metadata edit on a comic archive.
    /// </summary>
    public static Task EditMetadataAsync(
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
        return Task.Run(() => EditMetadata(
            filePath,
            editAction,
            batchJobId,
            changeReason,
            coverDHash,
            matchedThumbnailUrl,
            matchConfidence,
            visualSimilarity,
            cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Replaces the embedded ComicInfo.xml in a comic archive with the provided XML string content.
    /// </summary>
    public static void UpdateMetadataXml(string filePath, string xmlContent, bool createBackup = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(xmlContent);
        cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();

            using (Stream stream = File.OpenRead(filePath))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(tempDir, new ExtractionOptions { Overwrite = true, ExtractFullPath = false });
                    }
                }
            }

            if (createBackup)
            {
                string? existingXmlFile = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                string? originalXml = (existingXmlFile != null && File.Exists(existingXmlFile)) ? File.ReadAllText(existingXmlFile) : null;
                try
                {
                    var backupService = new InkTag.Core.Backup.MetadataBackupService();
                    backupService.CreateBackup(filePath, originalXml, "UpdateMetadataXml");
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Pre-write backup failed during UpdateMetadataXml for '{filePath}': {ex.Message}");
                }
            }

            string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
            File.WriteAllText(xmlPath, xmlContent, Encoding.UTF8);

            tempCbzPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cbz.tmp");
            using (Stream stream = File.OpenWrite(tempCbzPath))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entryName = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                    writer.Write(entryName, file);
                }
            }

            FileInfo tempCbzInfo = new FileInfo(tempCbzPath);
            if (!tempCbzInfo.Exists || tempCbzInfo.Length == 0)
            {
                var corruptEx = new InvalidDataException("Generated temporary archive is empty or invalid.");
                throw new ComicArchiveCorruptException(corruptEx.Message, tempCbzPath, corruptEx);
            }

            if (File.Exists(targetPath))
            {
                backupTargetPath = targetPath + ".target.bak." + Guid.NewGuid().ToString("N");
                File.Move(targetPath, backupTargetPath);
            }
            if (!targetPath.Equals(filePath, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
            {
                backupOriginalPath = filePath + ".orig.bak." + Guid.NewGuid().ToString("N");
                File.Move(filePath, backupOriginalPath);
            }

            File.Move(tempCbzPath, targetPath);

            if (backupTargetPath != null && File.Exists(backupTargetPath)) File.Delete(backupTargetPath);
            if (backupOriginalPath != null && File.Exists(backupOriginalPath)) File.Delete(backupOriginalPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            if (tempCbzPath != null && File.Exists(tempCbzPath))
            {
                try { File.Delete(tempCbzPath); } catch { }
            }
        }
    }
}

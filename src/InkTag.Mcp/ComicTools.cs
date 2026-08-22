using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using InkTag.Core;
using ModelContextProtocol.Server;

namespace InkTag.Mcp;

[McpServerToolType]
public static class ComicTools
{
    private static readonly MetadataEditor _editor = new();
    private static readonly InkTag.Core.Configuration.AppSettingsService _settingsService = new();

    public static bool? ReadOnlyOverride { get; set; }

    public static bool IsReadOnlyMode => ReadOnlyOverride ?? CheckReadOnlyEnvironment();

    private static bool CheckReadOnlyEnvironment()
    {
        string? env = Environment.GetEnvironmentVariable("INKTAG_MCP_READ_ONLY");
        return string.Equals(env, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that write operations are permitted in the current MCP server session.
    /// </summary>
    public static void EnsureWriteAccess(string operationName)
    {
        if (IsReadOnlyMode)
        {
            throw new UnauthorizedAccessException($"Access denied: Cannot perform '{operationName}' because the InkTag MCP server is running in strict READ-ONLY mode (INKTAG_MCP_READ_ONLY=true or --read-only).");
        }
    }

    /// <summary>
    /// Validates that a file or directory path is contained within the configured or default allowed roots.
    /// </summary>
    public static void ValidatePathAccess(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string fullPath = Path.GetFullPath(path);
        
        var allowedRoots = new List<string>(_settingsService.Settings.AllowedRootPaths ?? new List<string>());
        string? envRoots = Environment.GetEnvironmentVariable("INKTAG_ALLOWED_ROOT_PATHS");
        if (!string.IsNullOrWhiteSpace(envRoots))
        {
            char[] separators = OperatingSystem.IsWindows()
                ? new[] { ';', ',' }
                : new[] { ';', ':', ',' };
            var parsedEnvRoots = envRoots.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allowedRoots.AddRange(parsedEnvRoots);
        }

        // If no explicit allowed roots configured, default to user profile, current directory, and temp directory
        if (allowedRoots.Count == 0)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string currentDir = Environment.CurrentDirectory;
            string tempDir = Path.GetTempPath();

            if (!string.IsNullOrEmpty(userProfile)) allowedRoots.Add(userProfile);
            if (!string.IsNullOrEmpty(currentDir)) allowedRoots.Add(currentDir);
            if (!string.IsNullOrEmpty(tempDir)) allowedRoots.Add(tempDir);
        }

        bool isAllowed = false;
        foreach (var root in allowedRoots)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed)
        {
            throw new UnauthorizedAccessException($"Access denied: The path '{path}' is outside the allowed directories ({string.Join(", ", allowedRoots)}).");
        }
    }

    [McpServerTool, Description("Reads XML metadata embedded in a CBZ or CBR archive and returns it as JSON.")]
    public static string ReadComicMetadata(
        [Description("Path to comic archive (.cbz / .cbr)")] string path)
    {
        ValidatePathAccess(path);
        var metadata = _editor.ReadMetadata(path);
        string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        return $"Metadata for {Path.GetFileName(path)}:\n{json}";
    }

    [McpServerTool, Description("Updates metadata properties in a comic archive or directory using a JSON patch. Defaults to dryRun=true (preview only). Set dryRun=false to commit changes.")]
    public static string UpdateComicMetadata(
        [Description("Target file or directory path")] string path,
        [Description("Key-value property updates (e.g. {\"Writer\": \"Stan Lee\"})")] JsonElement patch,
        [Description("If true (default), previews diffs without modifying files on disk. Set dryRun=false to write changes.")] bool dryRun = true,
        [Description("If true, updates files in subdirectories recursively.")] bool recursive = false)
    {
        ValidatePathAccess(path);
        if (!dryRun)
        {
            EnsureWriteAccess("UpdateComicMetadata");
        }

        string patchJson = patch.GetRawText();
        var result = AgentOperations.UpdatePath(_editor, path, patchJson, dryRun, recursive);

        if (!result.IsDirectory)
        {
            object resObj = (result.Warnings != null && result.Warnings.Count > 0)
                ? (object)new { path = result.TargetPath, dryRun = result.DryRun, modifiedFields = result.Diffs?.Count ?? 0, diffs = result.Diffs, warnings = result.Warnings }
                : (object)new { path = result.TargetPath, dryRun = result.DryRun, modifiedFields = result.Diffs?.Count ?? 0, diffs = result.Diffs };
            return JsonSerializer.Serialize(resObj, new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            if (result.DryRun)
            {
                var fileDiffsForJson = result.FileDiffs?.Select(fd => new { path = fd.Path, diffs = fd.Diffs }).ToList();
                object resObj = (result.Warnings != null && result.Warnings.Count > 0)
                    ? (object)new { dryRun = true, files = fileDiffsForJson, warnings = result.Warnings }
                    : (object)new { dryRun = true, files = fileDiffsForJson };
                return JsonSerializer.Serialize(resObj, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                return JsonSerializer.Serialize(result.Report, new JsonSerializerOptions { WriteIndented = true });
            }
        }
    }

    [McpServerTool, Description("Extracts front cover art from a comic archive for multimodal vision inspection.")]
    public static object ExtractCoverImage(
        [Description("Path to comic archive (.cbz / .cbr)")] string path,
        [Description("Optional destination file path for image")] string? outputPath = null,
        [Description("If true, returns base64 encoded image bytes")] bool returnBase64 = false)
    {
        ValidatePathAccess(path);
        if (!string.IsNullOrEmpty(outputPath))
        {
            ValidatePathAccess(outputPath);
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_cover.jpg");
        }

        string? extracted = _editor.ExtractCoverImage(path, outputPath);
        if (extracted == null || !File.Exists(extracted))
        {
            throw new InvalidOperationException("Failed to extract cover image.");
        }

        if (returnBase64)
        {
            byte[] bytes = File.ReadAllBytes(extracted);
            string b64 = Convert.ToBase64String(bytes);
            string mime = extracted.EndsWith(".png") ? "image/png" : "image/jpeg";

            return new
            {
                content = new object[]
                {
                    new { type = "text", text = $"Cover extracted to {extracted}" },
                    new
                    {
                        type = "image",
                        data = b64,
                        mimeType = mime
                    }
                }
            };
        }

        return $"Cover image extracted to: {extracted}";
    }

    [McpServerTool, Description("Scans a directory for comic archives and checks for missing metadata fields or untagged comics.")]
    public static string ScanComics(
        [Description("Directory path to scan")] string directory,
        [Description("Fields to flag if null/empty (e.g. [\"Writer\", \"Series\"])")] string[]? missingFields = null,
        [Description("If true, scans subdirectories recursively.")] bool recursive = false,
        [Description("If true, filters and returns only untagged comics (missing ComicInfo.xml or empty Series/Title).")] bool onlyUntagged = false)
    {
        ValidatePathAccess(directory);
        var fieldsList = missingFields?.ToList() ?? new List<string>();
        var scanResult = AgentOperations.ScanDirectory(_editor, directory, fieldsList, recursive, onlyUntagged);

        var comicsForJson = scanResult.Items.Select(item => new
        {
            path = item.Path,
            title = item.Title,
            series = item.Series,
            number = item.Number,
            year = item.Year,
            hasEmbeddedXml = item.HasEmbeddedXml,
            isUntagged = item.IsUntagged,
            missing = item.MissingFields
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            directory = scanResult.Directory,
            totalFound = scanResult.TotalFound,
            untaggedCount = scanResult.UntaggedCount,
            onlyUntagged = scanResult.OnlyUntagged,
            comics = comicsForJson
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Returns the JSON Schema specification for valid ComicInfo metadata properties.")]
    public static string GetComicSchema()
    {
        return MetadataEditor.ExportJsonSchema();
    }

    [McpServerTool, Description("Searches ComicVine online database for matching comic issues.")]
    public static string SearchExternalMetadata(
        [Description("Series title (e.g. 'The Amazing Spider-Man')")] string series,
        [Description("Issue number (e.g. '121')")] string issueNumber = "",
        [Description("Optional release year (e.g. 1973)")] int? year = null,
        [Description("Optional ComicVine API key")] string? apiKey = null)
    {
        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        if (!string.IsNullOrEmpty(apiKey))
        {
            settingsService.Settings.ComicVineApiKey = apiKey;
        }

        var service = new InkTag.Core.Scrapers.MetadataScraperService(settingsService);
        var query = new InkTag.Core.Scrapers.ComicSearchQuery
        {
            Series = series,
            IssueNumber = issueNumber,
            Year = year
        };

        var results = service.SearchCandidatesAsync(query).GetAwaiter().GetResult();
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Scrapes and applies metadata from ComicVine to a local comic archive. Defaults to dryRun=true (preview only). Set dryRun=false to commit changes.")]
    public static string ScrapeComicMetadata(
        [Description("Path to comic archive (.cbz / .cbr)")] string path,
        [Description("Merge mode: 'fill-missing' (default) or 'overwrite'")] string mode = "fill-missing",
        [Description("If true (default), previews updates without writing to disk. Set dryRun=false to write changes.")] bool dryRun = true,
        [Description("Optional ComicVine API key")] string? apiKey = null)
    {
        ValidatePathAccess(path);
        if (!dryRun)
        {
            EnsureWriteAccess("ScrapeComicMetadata");
        }

        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        if (!string.IsNullOrEmpty(apiKey))
        {
            settingsService.Settings.ComicVineApiKey = apiKey;
        }

        var service = new InkTag.Core.Scrapers.MetadataScraperService(settingsService);
        var comic = _editor.ReadMetadata(path);
        ulong coverHash = _editor.GetCoverHash(path);
        var result = service.AutoScrapeComicAsync(comic, coverHash != 0 ? coverHash : null, path).GetAwaiter().GetResult();

        if (result.Success && !dryRun)
        {
            var mergeMode = string.Equals(mode, "overwrite", StringComparison.OrdinalIgnoreCase)
                ? InkTag.Core.Scrapers.ScrapeMergeMode.OverwriteAll
                : InkTag.Core.Scrapers.ScrapeMergeMode.FillMissingOnly;

            _editor.EditMetadata(path, existing => service.ApplyMetadata(existing, comic, mergeMode));
        }

        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            message = result.Message,
            confidence = result.SelectedCandidate?.MatchConfidence,
            visualSimilarity = result.SelectedCandidate?.VisualSimilarity,
            isVisualMatch = result.SelectedCandidate?.VisualSimilarity >= 0.90,
            dryRun,
            path,
            title = comic.Title,
            series = comic.Series,
            writer = comic.Writer
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Queues and executes a bulk scrape on a folder using smart series volume clustering and perceptual cover visual matching. Defaults to dryRun=true (preview only). Set dryRun=false to commit changes.")]
    public static string BulkScrapeDirectory(
        [Description("Directory path containing comic archives")] string directory,
        [Description("Merge mode: 'fill-missing' (default) or 'overwrite'")] string mode = "fill-missing",
        [Description("If true (default), previews updates without writing to archives on disk. Set dryRun=false to write changes.")] bool dryRun = true,
        [Description("If true, scans subdirectories recursively")] bool recursive = false,
        [Description("Optional ComicVine API key")] string? apiKey = null)
    {
        ValidatePathAccess(directory);
        if (!dryRun)
        {
            EnsureWriteAccess("BulkScrapeDirectory");
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        if (!string.IsNullOrEmpty(apiKey))
        {
            settingsService.Settings.ComicVineApiKey = apiKey;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, "*.*", searchOption)
            .Where(MetadataEditor.IsSupportedComicFile)
            .ToList();

        var scraperService = new InkTag.Core.Scrapers.MetadataScraperService(settingsService);
        var queueService = new InkTag.Core.Scrapers.BulkScrapeQueueService(scraperService, _editor, settingsService);
        var queue = queueService.CreateQueue(files);

        var mergeMode = string.Equals(mode, "overwrite", StringComparison.OrdinalIgnoreCase)
            ? InkTag.Core.Scrapers.ScrapeMergeMode.OverwriteAll
            : InkTag.Core.Scrapers.ScrapeMergeMode.FillMissingOnly;

        var options = new InkTag.Core.Scrapers.BulkScrapeOptions
        {
            MergeMode = mergeMode,
            ConfidenceThreshold = settingsService.Settings.AutoMatchConfidenceThreshold,
            EnableSmartSeriesGrouping = true
        };

        var summaryReport = queueService.ProcessQueueAsync(queue, options).GetAwaiter().GetResult();

        if (!dryRun)
        {
            queueService.ApplyMatchedMetadataAsync(queue, mergeMode).GetAwaiter().GetResult();
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            directory,
            dryRun,
            totalFiles = summaryReport.Total,
            matchedCount = summaryReport.Matched,
            reviewNeededCount = summaryReport.LowConfidence,
            unmatchedCount = summaryReport.Unmatched + summaryReport.Failed,
            items = summaryReport.Items.Select(i => new
            {
                file = i.FilePath,
                filename = i.Filename,
                status = i.Status.ToString(),
                matchedIssue = i.MatchedCandidate != null ? $"{i.MatchedCandidate.SeriesTitle} #{i.MatchedCandidate.IssueNumber}" : null,
                issueTitle = i.MatchedCandidate?.IssueTitle,
                visualSimilarity = i.MatchedCandidate?.VisualSimilarity,
                matchConfidence = i.MatchedCandidate?.MatchConfidence,
                message = i.StatusMessage
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Bulk renames comic archive files on disk based on embedded ComicInfo metadata and a customizable template pattern. Defaults to dryRun=true (preview only). Set dryRun=false to commit changes.")]
    public static string RenameComicFiles(
        [Description("Target file or directory path")] string path,
        [Description("Template pattern e.g. '{Series} #{Number:3} ({Year})' or '{Series} #{Number:3} - {Title} ({Year})'")] string template = "{Series} #{Number:3} ({Year})",
        [Description("If true, preserves scanner/release tags (e.g. (digital))")] bool preserveScanInfo = true,
        [Description("If true (default), previews rename operations without modifying files on disk. Set dryRun=false to commit renames.")] bool dryRun = true,
        [Description("If true, processes subdirectories recursively")] bool recursive = false)
    {
        ValidatePathAccess(path);
        if (!dryRun)
        {
            EnsureWriteAccess("RenameComicFiles");
        }

        var filesToProcess = new List<string>();
        if (File.Exists(path))
        {
            filesToProcess.Add(path);
        }
        else if (Directory.Exists(path))
        {
            var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".cbr", ".cb7", ".zip", ".rar" };
            filesToProcess.AddRange(Directory.EnumerateFiles(path, "*.*", searchOpt).Where(f => exts.Contains(Path.GetExtension(f))));
        }
        else
        {
            throw new FileNotFoundException($"Path not found: '{path}'");
        }

        var items = filesToProcess.Select(f =>
        {
            var comic = _editor.ReadMetadata(f);
            return (FilePath: f, Comic: comic);
        }).ToList();

        var previews = InkTag.Core.Renaming.ComicFileRenamer.PreviewBatchRename(items, template, preserveScanInfo);

        if (dryRun)
        {
            return JsonSerializer.Serialize(new
            {
                success = true,
                dryRun = true,
                total = previews.Count,
                items = previews.Select(p => new
                {
                    original = p.OriginalFilePath,
                    proposed = p.ProposedFilePath,
                    hasChange = p.HasChange,
                    hasCollision = p.HasCollision,
                    error = p.ErrorMessage
                })
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var result = InkTag.Core.Renaming.ComicFileRenamer.ExecuteBatchRename(previews);

        return JsonSerializer.Serialize(new
        {
            success = true,
            total = result.Total,
            renamed = result.Renamed,
            skipped = result.Skipped,
            failed = result.Failed,
            items = result.Items.Select(p => new
            {
                original = p.OriginalFilePath,
                proposed = p.ProposedFilePath,
                renamed = p.HasChange && string.IsNullOrEmpty(p.ErrorMessage),
                error = p.ErrorMessage
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Check Komga media server connection status, authentication, and list all library roots.")]
    public static async Task<string> CheckKomgaServer(
        [Description("Optional Komga server URL override (e.g. http://localhost:25600)")] string? serverUrl = null,
        [Description("Optional Komga API key override")] string? apiKey = null)
    {
        string url = !string.IsNullOrWhiteSpace(serverUrl) 
            ? serverUrl 
            : _settingsService.GetEffectiveKomgaServerUrl();
        string key = !string.IsNullOrWhiteSpace(apiKey) 
            ? apiKey 
            : _settingsService.GetEffectiveKomgaApiKey();

        if (string.IsNullOrWhiteSpace(url))
        {
            return JsonSerializer.Serialize(new
            {
                connected = false,
                error = "Komga server URL is not configured. Set KOMGA_SERVER_URL environment variable or configure in InkTag settings."
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        using var client = new InkTag.Core.Komga.KomgaClient(url, key, _settingsService.GetEffectiveKomgaUser(), _settingsService.GetEffectiveKomgaPassword());
        bool connected = await client.TestConnectionAsync();
        var libraries = connected ? await client.GetLibrariesAsync() : Array.Empty<InkTag.Core.Komga.KomgaLibraryDto>();

        return JsonSerializer.Serialize(new
        {
            connected,
            serverUrl = url,
            libraryCount = libraries.Count,
            libraries = libraries.Select(l => new
            {
                id = l.Id,
                name = l.Name,
                root = l.Root,
                scanInterval = l.ScanInterval
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Trigger targeted cache invalidation and analysis on Komga for a specific file or folder, with optional StoryArc collection sync.")]
    public static async Task<string> SyncKomgaBookOrSeries(
        [Description("Local file path or folder path to synchronize with Komga")] string path,
        [Description("Optional StoryArc name to sync into Komga Collections")] string? storyArc = null)
    {
        ValidatePathAccess(path);

        var syncService = new InkTag.Core.Komga.KomgaSyncService(_settingsService);
        if (!syncService.IsConfigured)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Komga server is not configured."
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        if (File.Exists(path))
        {
            var info = _editor.ReadMetadata(path);
            if (!string.IsNullOrWhiteSpace(storyArc))
            {
                info.StoryArc = storyArc;
            }

            var report = await syncService.SyncComicFileAsync(path, info);
            return JsonSerializer.Serialize(new
            {
                success = report.IsSuccess,
                booksAnalyzed = report.BooksAnalyzed,
                seriesAnalyzed = report.SeriesAnalyzed,
                collectionsSynced = report.CollectionsSynced,
                messages = report.SuccessMessages,
                failures = report.Failures.Select(f => new { path = f.Target, error = f.Error })
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        else if (Directory.Exists(path))
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".cbr", ".cb7", ".zip", ".rar" };
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .Select(f => (FilePath: f, Info: _editor.ReadMetadata(f)))
                .ToList();

            var report = await syncService.SyncMultipleComicsAsync(files);
            return JsonSerializer.Serialize(new
            {
                success = report.IsSuccess,
                totalFiles = files.Count,
                booksAnalyzed = report.BooksAnalyzed,
                seriesAnalyzed = report.SeriesAnalyzed,
                collectionsSynced = report.CollectionsSynced,
                messages = report.SuccessMessages,
                failures = report.Failures.Select(f => new { path = f.Target, error = f.Error })
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        throw new FileNotFoundException($"Path not found: '{path}'");
    }

    [McpServerTool, Description("Audit Komga library for unreadable, unsupported, or error books requiring repair or metadata tagging.")]
    public static async Task<string> AuditKomgaLibrary(
        [Description("Optional Komga library ID to filter audit")] string? libraryId = null)
    {
        using var client = new InkTag.Core.Komga.KomgaClient(_settingsService);
        if (!client.IsConfigured)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Komga server is not configured."
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var errorBooks = await client.GetUntaggedOrErrorBooksAsync(libraryId);
        return JsonSerializer.Serialize(new
        {
            success = true,
            errorCount = errorBooks.Count,
            books = errorBooks.Select(b => new
            {
                id = b.Id,
                name = b.Name,
                seriesId = b.SeriesId,
                seriesTitle = b.SeriesTitle,
                url = b.Url,
                mediaStatus = b.Media?.Status,
                mediaComment = b.Media?.Comment
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Lists automated pre-write metadata backup snapshots for comic archives.")]
    public static string ListMetadataBackups(
        [Description("Optional archive file path filter")] string? path = null,
        [Description("Maximum number of backup records to return (default: 50)")] int limit = 50)
    {
        if (!string.IsNullOrEmpty(path))
        {
            ValidatePathAccess(path);
        }

        var backupService = new InkTag.Core.Backup.MetadataBackupService();
        var backups = backupService.ListBackups(path, limit);

        return JsonSerializer.Serialize(new
        {
            backupCount = backups.Count,
            backups = backups.Select(b => new
            {
                id = b.Id,
                archivePath = b.ArchivePath,
                originalFileName = b.OriginalFileName,
                operationType = b.OperationType,
                timestamp = b.Timestamp
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Restores a comic archive's ComicInfo.xml metadata from a previous backup snapshot.")]
    public static string RestoreComicBackup(
        [Description("Path to comic archive (.cbz / .cbr)")] string path,
        [Description("Optional specific backup ID (defaults to the most recent backup for this archive)")] string? backupId = null)
    {
        ValidatePathAccess(path);
        EnsureWriteAccess("RestoreComicBackup");

        var backupService = new InkTag.Core.Backup.MetadataBackupService();
        bool restored = backupService.RestoreBackup(path, backupId);

        var metadata = _editor.ReadMetadata(path);

        return JsonSerializer.Serialize(new
        {
            success = restored,
            path,
            backupId,
            message = "Metadata successfully restored from backup snapshot.",
            currentMetadata = new
            {
                title = metadata.Title,
                series = metadata.Series,
                number = metadata.Number,
                year = metadata.Year,
                writer = metadata.Writer
            }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}

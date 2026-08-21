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

    [McpServerTool, Description("Reads XML metadata embedded in a CBZ or CBR archive and returns it as JSON.")]
    public static string ReadComicMetadata(
        [Description("Path to comic archive (.cbz / .cbr)")] string path)
    {
        var metadata = _editor.ReadMetadata(path);
        string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        return $"Metadata for {Path.GetFileName(path)}:\n{json}";
    }

    [McpServerTool, Description("Updates metadata properties in a comic archive or directory using a JSON patch.")]
    public static string UpdateComicMetadata(
        [Description("Target file or directory path")] string path,
        [Description("Key-value property updates (e.g. {\"Writer\": \"Stan Lee\"})")] JsonElement patch,
        [Description("If true, previews diffs without modifying files on disk.")] bool dryRun = false,
        [Description("If true, updates files in subdirectories recursively.")] bool recursive = false)
    {
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

    [McpServerTool, Description("Scrapes and applies metadata from ComicVine to a local comic archive.")]
    public static string ScrapeComicMetadata(
        [Description("Path to comic archive (.cbz / .cbr)")] string path,
        [Description("Merge mode: 'fill-missing' (default) or 'overwrite'")] string mode = "fill-missing",
        [Description("If true, previews updates without writing to disk")] bool dryRun = false,
        [Description("Optional ComicVine API key")] string? apiKey = null)
    {
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

    [McpServerTool, Description("Queues and executes a bulk scrape on a folder using smart series volume clustering and perceptual cover visual matching.")]
    public static string BulkScrapeDirectory(
        [Description("Directory path containing comic archives")] string directory,
        [Description("Merge mode: 'fill-missing' (default) or 'overwrite'")] string mode = "fill-missing",
        [Description("If true, previews updates without writing to archives on disk")] bool dryRun = false,
        [Description("If true, scans subdirectories recursively")] bool recursive = false,
        [Description("Optional ComicVine API key")] string? apiKey = null)
    {
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
            .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
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

    [McpServerTool, Description("Bulk renames comic archive files on disk based on embedded ComicInfo metadata and a customizable template pattern.")]
    public static string RenameComicFiles(
        [Description("Target file or directory path")] string path,
        [Description("Template pattern e.g. '{Series} #{Number:3} ({Year})' or '{Series} #{Number:3} - {Title} ({Year})'")] string template = "{Series} #{Number:3} ({Year})",
        [Description("If true, preserves scanner/release tags (e.g. (digital))")] bool preserveScanInfo = true,
        [Description("If true, previews rename operations without modifying files on disk")] bool dryRun = false,
        [Description("If true, processes subdirectories recursively")] bool recursive = false)
    {
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
}

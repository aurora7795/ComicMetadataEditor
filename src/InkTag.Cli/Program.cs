using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using InkTag.Core;

bool isJsonOutput = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
bool isDryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
bool isVerbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

var positionalArgs = args.Where(a => !a.StartsWith("--")).ToList();
string command = positionalArgs.Count > 0 ? positionalArgs[0].ToLowerInvariant() : "help";

var editor = new MetadataEditor();

try
{
    switch (command)
    {
        case "read":
            HandleReadCommand(positionalArgs, isJsonOutput, editor);
            break;
        case "update":
            HandleUpdateCommand(args, positionalArgs, isJsonOutput, isDryRun, editor);
            break;
        case "scan":
            HandleScanCommand(args, positionalArgs, isJsonOutput, editor);
            break;
        case "cover":
            HandleCoverCommand(args, positionalArgs, isJsonOutput, editor);
            break;
        case "schema":
            HandleSchemaCommand(isJsonOutput);
            break;
        case "scrape":
            HandleScrapeCommand(args, positionalArgs, isJsonOutput, isDryRun, editor);
            break;
        case "rename":
            HandleRenameCommand(args, positionalArgs, isJsonOutput, isDryRun, editor);
            break;
        case "help":
        case "--help":
        case "-h":
            PrintHelp(isJsonOutput);
            break;
        default:
            // Fallback for legacy usage: if target argument is a valid file/directory
            if (Directory.Exists(command) || File.Exists(command))
            {
                HandleLegacyOrFallback(command, isJsonOutput, isDryRun, editor);
            }
            else
            {
                PrintUnknownCommand(command, isJsonOutput);
            }
            break;
    }
}
catch (Exception ex)
{
    if (isJsonOutput)
    {
        object errObj = isVerbose
            ? new { success = false, error = ex.Message, stackTrace = ex.StackTrace }
            : (object)new { success = false, error = ex.Message };
        Console.WriteLine(JsonSerializer.Serialize(errObj, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
    }
    Environment.Exit(1);
}

#region Command Handlers

static void HandleReadCommand(List<string> positionalArgs, bool isJson, MetadataEditor editor)
{
    if (positionalArgs.Count < 2)
    {
        throw new ArgumentException("Usage: read <file-path>");
    }

    string filePath = positionalArgs[1];
    if (!File.Exists(filePath))
    {
        throw new FileNotFoundException($"Comic archive not found: {filePath}");
    }

    var metadata = editor.ReadMetadata(filePath);

    if (isJson)
    {
        var result = new { success = true, path = filePath, metadata = metadata };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"--- Metadata for {Path.GetFileName(filePath)} ---");
        Console.WriteLine(JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }
}

static void HandleUpdateCommand(string[] rawArgs, List<string> positionalArgs, bool isJson, bool isDryRun, MetadataEditor editor)
{
    if (positionalArgs.Count < 2)
    {
        throw new ArgumentException("Usage: update <file-or-directory-path> --patch '<json>' [--dry-run] [--recursive]");
    }

    string targetPath = positionalArgs[1];
    string? patchJson = GetOptionValue(rawArgs, "--patch");
    bool isRecursive = rawArgs.Any(a => a.Equals("--recursive", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(patchJson))
    {
        throw new ArgumentException("Missing required option --patch '<json>'");
    }

    var result = AgentOperations.UpdatePath(editor, targetPath, patchJson, isDryRun, isRecursive);

    if (!result.IsDirectory)
    {
        if (isJson)
        {
            var jsonRes = (result.Warnings != null && result.Warnings.Count > 0)
                ? (object)new { success = true, path = result.TargetPath, dryRun = result.DryRun, diffs = result.Diffs, warnings = result.Warnings }
                : (object)new { success = true, path = result.TargetPath, dryRun = result.DryRun, diffs = result.Diffs };
            Console.WriteLine(JsonSerializer.Serialize(jsonRes, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"{(isDryRun ? "[DRY RUN] " : "")}Updated {Path.GetFileName(result.TargetPath)} successfully.");
            Console.WriteLine($"Modifications ({result.Diffs?.Count ?? 0} fields):");
            if (result.Diffs != null)
            {
                foreach (var d in result.Diffs)
                {
                    Console.WriteLine($"  - {d.PropertyName}: '{d.OldValue}' => '{d.NewValue}'");
                }
            }
        }
    }
    else
    {
        if (isDryRun)
        {
            var fileDiffsForJson = result.FileDiffs?.Select(fd => new
            {
                path = fd.Path,
                diffs = fd.Diffs
            }).ToList();

            if (isJson)
            {
                var jsonRes = new { success = true, directory = result.TargetPath, dryRun = true, files = fileDiffsForJson };
                Console.WriteLine(JsonSerializer.Serialize(jsonRes, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"[DRY RUN] Would update {result.FileDiffs?.Count ?? 0} files in {result.TargetPath}.");
            }
        }
        else
        {
            if (isJson)
            {
                var jsonRes = new { success = result.Report?.Failures.Count == 0, report = result.Report };
                Console.WriteLine(JsonSerializer.Serialize(jsonRes, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"Bulk edit complete: {result.Report?.Successes.Count ?? 0} succeeded, {result.Report?.Failures.Count ?? 0} failed.");
            }
        }
    }
}

static void HandleScanCommand(string[] rawArgs, List<string> positionalArgs, bool isJson, MetadataEditor editor)
{
    string targetDir = positionalArgs.Count > 1 ? positionalArgs[1] : Directory.GetCurrentDirectory();
    string? missingFilterStr = GetOptionValue(rawArgs, "--missing");
    var missingFields = missingFilterStr?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
    bool isRecursive = rawArgs.Any(a => a.Equals("--recursive", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));

    var scanResult = AgentOperations.ScanDirectory(editor, targetDir, missingFields, isRecursive);

    if (isJson)
    {
        var itemsForJson = scanResult.Items.Select(item => new
        {
            path = item.Path,
            title = item.Title,
            series = item.Series,
            number = item.Number,
            year = item.Year,
            missingFields = item.MissingFields
        }).ToList();

        var payload = new { success = true, directory = scanResult.Directory, totalFound = scanResult.TotalFound, items = itemsForJson };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"Found {scanResult.TotalFound} comic archives in {scanResult.Directory}:");
        foreach (var item in scanResult.Items)
        {
            Console.WriteLine($"  [{item.Number ?? "?"}] {item.Series ?? "Unknown"} - {item.Title ?? Path.GetFileName(item.Path)}");
        }
    }
}

static void HandleCoverCommand(string[] rawArgs, List<string> positionalArgs, bool isJson, MetadataEditor editor)
{
    if (positionalArgs.Count < 2)
    {
        throw new ArgumentException("Usage: cover <comic-file-path> [--output <image-path>]");
    }

    string comicPath = positionalArgs[1];
    string? outputPath = GetOptionValue(rawArgs, "--output");

    if (string.IsNullOrEmpty(outputPath))
    {
        string dir = Path.GetDirectoryName(comicPath) ?? ".";
        string nameNoExt = Path.GetFileNameWithoutExtension(comicPath);
        outputPath = Path.Combine(dir, $"{nameNoExt}_cover.jpg");
    }

    string? extractedPath = editor.ExtractCoverImage(comicPath, outputPath);

    if (extractedPath != null && File.Exists(extractedPath))
    {
        if (isJson)
        {
            var res = new { success = true, comicPath = comicPath, coverPath = extractedPath };
            Console.WriteLine(JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Extracted cover image to: {extractedPath}");
        }
    }
    else
    {
        throw new InvalidOperationException($"No cover image could be extracted from: {comicPath}");
    }
}

static void HandleSchemaCommand(bool isJson)
{
    string schemaJson = MetadataEditor.ExportJsonSchema();
    if (isJson)
    {
        Console.WriteLine(schemaJson);
    }
    else
    {
        Console.WriteLine("--- ComicInfo Metadata JSON Schema ---");
        Console.WriteLine(schemaJson);
    }
}

static void HandleLegacyOrFallback(string targetDir, bool isJson, bool isDryRun, MetadataEditor editor)
{
    if (Directory.Exists(targetDir))
    {
        HandleScanCommand(Array.Empty<string>(), new List<string> { "scan", targetDir }, isJson, editor);
    }
    else if (File.Exists(targetDir))
    {
        HandleReadCommand(new List<string> { "read", targetDir }, isJson, editor);
    }
}

static void HandleScrapeCommand(string[] args, List<string> positionalArgs, bool isJson, bool isDryRun, MetadataEditor editor)
{
    if (positionalArgs.Count < 2)
    {
        throw new ArgumentException("Usage: scrape <file|directory> [--api-key KEY] [--mode fill-missing|overwrite] [--dry-run] [--json]");
    }

    string targetPath = positionalArgs[1];
    string? apiKey = GetOptionValue(args, "--api-key");
    string? modeStr = GetOptionValue(args, "--mode");

    InkTag.Core.Scrapers.ScrapeMergeMode mergeMode = InkTag.Core.Scrapers.ScrapeMergeMode.FillMissingOnly;
    if (string.Equals(modeStr, "overwrite", StringComparison.OrdinalIgnoreCase))
    {
        mergeMode = InkTag.Core.Scrapers.ScrapeMergeMode.OverwriteAll;
    }

    var settingsService = new InkTag.Core.Configuration.AppSettingsService();
    if (!string.IsNullOrEmpty(apiKey))
    {
        settingsService.Settings.ComicVineApiKey = apiKey;
    }

    var scraperService = new InkTag.Core.Scrapers.MetadataScraperService(settingsService);

    if (File.Exists(targetPath))
    {
        var comic = editor.ReadMetadata(targetPath);
        ulong coverHash = editor.GetCoverHash(targetPath);
        var result = scraperService.AutoScrapeComicAsync(comic, coverHash != 0 ? coverHash : null, targetPath).GetAwaiter().GetResult();

        if (result.Success && !isDryRun)
        {
            editor.EditMetadata(targetPath, existing =>
            {
                scraperService.ApplyMetadata(existing, comic, mergeMode);
            });
        }

        if (isJson)
        {
            var resObj = new
            {
                success = result.Success,
                message = result.Message,
                dryRun = isDryRun,
                file = targetPath,
                series = comic.Series,
                number = comic.Number,
                title = comic.Title,
                writer = comic.Writer
            };
            Console.WriteLine(JsonSerializer.Serialize(resObj, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine(result.Message);
        }
    }
    else if (Directory.Exists(targetPath))
    {
        bool recursive = args.Any(a => a.Equals("--recursive", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(targetPath, "*.*", searchOption)
                             .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
                             .ToList();

        var queueService = new InkTag.Core.Scrapers.BulkScrapeQueueService(scraperService, editor, settingsService);
        var queue = queueService.CreateQueue(files);
        var options = new InkTag.Core.Scrapers.BulkScrapeOptions
        {
            MergeMode = mergeMode,
            ConfidenceThreshold = settingsService.Settings.AutoMatchConfidenceThreshold,
            EnableSmartSeriesGrouping = true
        };

        var progress = new Progress<InkTag.Core.Scrapers.BulkScrapeProgressReport>(report =>
        {
            if (!isJson)
            {
                Console.WriteLine($"[{report.ProcessedItems}/{report.TotalItems}] {report.StatusMessage}");
            }
        });

        var summaryReport = queueService.ProcessQueueAsync(queue, options, progress).GetAwaiter().GetResult();

        if (!isDryRun)
        {
            queueService.ApplyMatchedMetadataAsync(queue, mergeMode).GetAwaiter().GetResult();
        }

        if (isJson)
        {
            var summary = new
            {
                success = true,
                dryRun = isDryRun,
                totalFiles = summaryReport.Total,
                scrapedCount = summaryReport.Matched,
                reviewNeeded = summaryReport.LowConfidence,
                failedCount = summaryReport.Unmatched + summaryReport.Failed,
                details = summaryReport.Items.Select(i => new
                {
                    file = i.FilePath,
                    status = i.Status.ToString(),
                    matchedIssue = i.MatchedCandidate != null ? $"{i.MatchedCandidate.SeriesTitle} #{i.MatchedCandidate.IssueNumber}" : null,
                    visualSimilarity = i.MatchedCandidate?.VisualSimilarity,
                    matchConfidence = i.MatchedCandidate?.MatchConfidence,
                    message = i.StatusMessage
                })
            };
            Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"\n--- Bulk Scrape Complete ---");
            Console.WriteLine($"Total: {summaryReport.Total} | Matched: {summaryReport.Matched} | Review Needed: {summaryReport.LowConfidence} | Unmatched: {summaryReport.Unmatched + summaryReport.Failed}");
            if (isDryRun)
            {
                Console.WriteLine("(Dry run: no changes written to files)");
            }
        }
    }
    else
    {
        throw new FileNotFoundException($"Target path not found: '{targetPath}'");
    }
}

static void HandleRenameCommand(string[] args, List<string> positionalArgs, bool isJson, bool isDryRun, MetadataEditor editor)
{
    if (positionalArgs.Count < 2)
    {
        throw new ArgumentException("Usage: rename <file|directory> [--template <pattern>] [--strip-scan-info] [--dry-run] [--json]");
    }

    string targetPath = positionalArgs[1];
    string template = GetOptionValue(args, "--template") ?? InkTag.Core.Renaming.ComicFileRenamer.DefaultTemplate;
    bool preserveScanInfo = !args.Any(a => a.Equals("--strip-scan-info", StringComparison.OrdinalIgnoreCase));

    var filesToProcess = new List<string>();
    if (File.Exists(targetPath))
    {
        filesToProcess.Add(targetPath);
    }
    else if (Directory.Exists(targetPath))
    {
        bool recursive = args.Any(a => a.Equals("--recursive", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));
        var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cbz", ".cbr", ".cb7", ".zip", ".rar" };
        filesToProcess.AddRange(Directory.EnumerateFiles(targetPath, "*.*", searchOpt).Where(f => exts.Contains(Path.GetExtension(f))));
    }
    else
    {
        throw new FileNotFoundException("Target file or directory not found.", targetPath);
    }

    var items = filesToProcess.Select(f =>
    {
        var comic = editor.ReadMetadata(f);
        return (FilePath: f, Comic: comic);
    }).ToList();

    var previews = InkTag.Core.Renaming.ComicFileRenamer.PreviewBatchRename(items, template, preserveScanInfo);

    if (isDryRun)
    {
        if (isJson)
        {
            var jsonOut = new
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
            };
            Console.WriteLine(JsonSerializer.Serialize(jsonOut, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"[Dry Run] Bulk Rename Preview ({previews.Count} items, template: '{template}'):");
            foreach (var p in previews)
            {
                string status = p.HasCollision ? "[COLLISION]" : (!p.HasChange ? "[UNCHANGED]" : "[READY]");
                Console.WriteLine($"  {status} {p.OriginalFilename} -> {p.ProposedFilename}");
            }
        }
        return;
    }

    var result = InkTag.Core.Renaming.ComicFileRenamer.ExecuteBatchRename(previews);

    if (isJson)
    {
        var jsonOut = new
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
        };
        Console.WriteLine(JsonSerializer.Serialize(jsonOut, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"Renamed {result.Renamed} of {result.Total} file(s) ({result.Skipped} unchanged, {result.Failed} failed).");
    }
}

static void PrintHelp(bool isJson)
{
    var helpObj = new
    {
        name = "InkTag.Cli",
        description = "AI-Agent friendly CLI for editing ComicInfo.xml metadata in CBZ/CBR archives.",
        subcommands = new[]
        {
            new { name = "read <file>", description = "Reads and displays ComicInfo metadata as JSON or text." },
            new { name = "update <file|dir> --patch '<json>' [--dry-run] [--recursive]", description = "Applies JSON property edits to one or all comic archives." },
            new { name = "scan <directory> [--missing Field1,Field2] [--recursive]", description = "Scans a directory for comic files and missing metadata." },
            new { name = "scrape <file|dir> [--api-key KEY] [--mode fill-missing|overwrite]", description = "Auto-scrapes metadata from ComicVine online database." },
            new { name = "rename <file|dir> [--template <pattern>] [--dry-run]", description = "Bulk renames files based on metadata template pattern." },
            new { name = "cover <file> [--output <image-path>]", description = "Extracts front cover image from archive." },
            new { name = "schema", description = "Prints JSON Schema for ComicInfo metadata objects." }
        },
        globalFlags = new[]
        {
            new { flag = "--json", description = "Output structured machine-parseable JSON." },
            new { flag = "--dry-run", description = "Preview changes without writing files to disk." },
            new { flag = "--recursive, -r", description = "Recursively search subdirectories when scanning or updating directories." },
            new { flag = "--verbose", description = "Include detailed stack traces in error outputs." }
        }
    };

    if (isJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(helpObj, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine("InkTag.Cli - AI-Agent Friendly CLI\n");
        Console.WriteLine("Usage: dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- <command> [options]\n");
        Console.WriteLine("Commands:");
        foreach (var cmd in helpObj.subcommands)
        {
            Console.WriteLine($"  {cmd.name,-45} {cmd.description}");
        }
        Console.WriteLine("\nGlobal Flags:");
        foreach (var flag in helpObj.globalFlags)
        {
            Console.WriteLine($"  {flag.flag,-45} {flag.description}");
        }
    }
}

static void PrintUnknownCommand(string cmd, bool isJson)
{
    if (isJson)
    {
        var err = new { success = false, error = $"Unknown command '{cmd}'", help = "Use --help for usage instructions." };
        Console.WriteLine(JsonSerializer.Serialize(err, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: Unknown command '{cmd}'. Use --help for usage.");
        Console.ResetColor();
    }
    Environment.Exit(1);
}

static string? GetOptionValue(string[] args, string optionName)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }
    return null;
}

#endregion

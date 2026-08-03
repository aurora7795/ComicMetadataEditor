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

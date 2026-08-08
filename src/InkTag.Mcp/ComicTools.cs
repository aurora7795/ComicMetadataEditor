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

    [McpServerTool, Description("Scans a directory for comic archives and checks for missing metadata fields.")]
    public static string ScanComics(
        [Description("Directory path to scan")] string directory,
        [Description("Fields to flag if null/empty (e.g. [\"Writer\", \"Series\"])")] string[]? missingFields = null,
        [Description("If true, scans subdirectories recursively.")] bool recursive = false)
    {
        var fieldsList = missingFields?.ToList() ?? new List<string>();
        var scanResult = AgentOperations.ScanDirectory(_editor, directory, fieldsList, recursive);

        var comicsForJson = scanResult.Items.Select(item => new
        {
            path = item.Path,
            title = item.Title,
            series = item.Series,
            number = item.Number,
            missing = item.MissingFields
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            directory = scanResult.Directory,
            totalFound = scanResult.TotalFound,
            comics = comicsForJson
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool, Description("Returns the JSON Schema specification for valid ComicInfo metadata properties.")]
    public static string GetComicSchema()
    {
        return MetadataEditor.ExportJsonSchema();
    }
}

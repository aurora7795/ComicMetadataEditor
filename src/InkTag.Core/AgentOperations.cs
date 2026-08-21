using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace InkTag.Core;

public class AgentScanItem
{
    public required string Path { get; set; }
    public string? Title { get; set; }
    public string? Series { get; set; }
    public string? Number { get; set; }
    public int? Year { get; set; }
    public bool HasEmbeddedXml { get; set; }
    public bool IsUntagged { get; set; }
    public List<string> MissingFields { get; set; } = new();
}

public class AgentScanResult
{
    public required string Directory { get; set; }
    public int TotalFound { get; set; }
    public int UntaggedCount { get; set; }
    public bool OnlyUntagged { get; set; }
    public List<AgentScanItem> Items { get; set; } = new();
}

public class FileUpdateDiffResult
{
    public required string Path { get; set; }
    public List<MetadataDiffItem> Diffs { get; set; } = new();
}

public class AgentUpdateResult
{
    public bool IsDirectory { get; set; }
    public required string TargetPath { get; set; }
    public bool DryRun { get; set; }
    public List<MetadataDiffItem>? Diffs { get; set; }
    public List<string>? Warnings { get; set; }
    public List<FileUpdateDiffResult>? FileDiffs { get; set; }
    public BulkEditReport? Report { get; set; }
}

public static class AgentOperations
{
    /// <summary>
    /// Scans a directory for comic archives (.cbz, .cbr) and checks for missing metadata fields or untagged comics.
    /// </summary>
    public static AgentScanResult ScanDirectory(
        MetadataEditor editor, 
        string directoryPath, 
        IEnumerable<string>? missingFields = null, 
        bool recursive = false,
        bool onlyUntagged = false)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(MetadataEditor.IsSupportedComicFile)
            .ToList();

        var missingFieldList = missingFields?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList() ?? new List<string>();

        var properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var items = new List<AgentScanItem>();
        int totalUntagged = 0;

        foreach (var file in files)
        {
            var info = editor.ReadMetadata(file, out bool hasEmbeddedXml);
            bool isUntagged = !hasEmbeddedXml || !info.HasEssentialMetadata;
            if (isUntagged)
            {
                totalUntagged++;
            }

            if (onlyUntagged && !isUntagged)
            {
                continue;
            }

            var missing = new List<string>();

            if (missingFieldList.Count > 0)
            {
                foreach (var req in missingFieldList)
                {
                    var p = properties.FirstOrDefault(pr => pr.Name.Equals(req, StringComparison.OrdinalIgnoreCase));
                    if (p != null)
                    {
                        var val = p.GetValue(info);
                        if (val == null || (val is string s && string.IsNullOrWhiteSpace(s)))
                        {
                            missing.Add(p.Name);
                        }
                    }
                }
            }

            items.Add(new AgentScanItem
            {
                Path = file,
                Title = info.Title,
                Series = info.Series,
                Number = info.Number,
                Year = info.Year,
                HasEmbeddedXml = hasEmbeddedXml,
                IsUntagged = isUntagged,
                MissingFields = missing
            });
        }

        return new AgentScanResult
        {
            Directory = directoryPath,
            TotalFound = files.Count,
            UntaggedCount = totalUntagged,
            OnlyUntagged = onlyUntagged,
            Items = items
        };
    }

    /// <summary>
    /// Updates metadata for a file or directory using a JSON patch object string.
    /// </summary>
    public static AgentUpdateResult UpdatePath(MetadataEditor editor, string targetPath, string jsonPatch, bool dryRun = false, bool recursive = false)
    {
        if (File.Exists(targetPath))
        {
            var diffs = editor.GetMetadataDiff(targetPath, jsonPatch);
            var warnings = MetadataEditor.ApplyJsonPatch(new ComicInfo(), jsonPatch);

            if (!dryRun)
            {
                editor.EditMetadataFromJson(targetPath, jsonPatch);
            }

            return new AgentUpdateResult
            {
                IsDirectory = false,
                TargetPath = targetPath,
                DryRun = dryRun,
                Diffs = diffs,
                Warnings = warnings
            };
        }
        else if (Directory.Exists(targetPath))
        {
            var warnings = MetadataEditor.ApplyJsonPatch(new ComicInfo(), jsonPatch);
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (dryRun)
            {
                var files = Directory.GetFiles(targetPath, "*.*", searchOption)
                    .Where(MetadataEditor.IsSupportedComicFile)
                    .ToList();

                var fileDiffs = files.Select(f => new FileUpdateDiffResult
                {
                    Path = f,
                    Diffs = editor.GetMetadataDiff(f, jsonPatch)
                }).ToList();

                return new AgentUpdateResult
                {
                    IsDirectory = true,
                    TargetPath = targetPath,
                    DryRun = true,
                    Warnings = warnings,
                    FileDiffs = fileDiffs
                };
            }
            else
            {
                var report = editor.BulkEditMetadataFromJson(targetPath, jsonPatch, recursive);
                return new AgentUpdateResult
                {
                    IsDirectory = true,
                    TargetPath = targetPath,
                    DryRun = false,
                    Warnings = warnings,
                    Report = report
                };
            }
        }
        else
        {
            throw new FileNotFoundException($"Target path not found: {targetPath}");
        }
    }
}

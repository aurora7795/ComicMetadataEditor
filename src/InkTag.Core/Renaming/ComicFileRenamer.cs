using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using InkTag.Core.Parsing;

namespace InkTag.Core.Renaming;

public class RenameItemPreview
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public string OriginalFilename => Path.GetFileName(OriginalFilePath);
    public string ProposedFilename { get; set; } = string.Empty;
    public string ProposedFilePath { get; set; } = string.Empty;
    public bool HasChange => !string.Equals(OriginalFilename, ProposedFilename, StringComparison.Ordinal);
    public bool HasCollision { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RenameBatchResult
{
    public int Total { get; set; }
    public int Renamed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<RenameItemPreview> Items { get; set; } = new();
}

public static class ComicFileRenamer
{
    public const string DefaultTemplate = "{Series} #{Number:3} ({Year})";
    public const string TemplateWithTitle = "{Series} #{Number:3} - {Title} ({Year})";
    public const string TemplateWithScanInfo = "{Series} #{Number:3} ({Year}) {ScanInfo}";
    public const string TemplateNumberless = "{Series} {Number:3} ({Year})";
    public const string TemplatePublisherVolume = "{Publisher} - {Series} v{Volume} #{Number:3} ({Year})";

    public static readonly IReadOnlyList<string> StandardTemplates = new[]
    {
        DefaultTemplate,
        TemplateWithTitle,
        TemplateWithScanInfo,
        TemplateNumberless,
        TemplatePublisherVolume
    };

    private static readonly Regex TokenRegex = new(@"\{(?<token>[A-Za-z]+)(?::(?<format>[^}]+))?\}", RegexOptions.Compiled);
    private static readonly Regex InvalidCharsRegex = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex EmptyParenthesesRegex = new(@"\(\s*\)|\[\s*\]|\{\s*\}", RegexOptions.Compiled);
    private static readonly Regex DanglingPunctuationRegex = new(@"\s*[-–—_:,]+\s*(?=\.[^.]+$|$)", RegexOptions.Compiled);
    private static readonly Regex LeadingPunctuationRegex = new(@"^[\s\-–—_:,]+", RegexOptions.Compiled);
    private static readonly Regex RedundantSeparatorsRegex = new(@"\s*[-–—_]\s*[-–—_]+\s*", RegexOptions.Compiled);

    /// <summary>
    /// Generates a standardized, filesystem-safe filename from comic metadata and a template pattern.
    /// </summary>
    public static string GenerateFilename(
        ComicInfo comic,
        string originalFilePath,
        string templatePattern = DefaultTemplate,
        bool preserveScanInfo = false)
    {
        if (string.IsNullOrWhiteSpace(templatePattern))
        {
            templatePattern = DefaultTemplate;
        }

        string originalExt = Path.GetExtension(originalFilePath);
        if (string.IsNullOrEmpty(originalExt))
        {
            originalExt = ".cbz";
        }

        // Extract scan/edition info if requested
        string scanInfo = comic.ScanInformation ?? string.Empty;
        if (string.IsNullOrWhiteSpace(scanInfo) && !string.IsNullOrWhiteSpace(originalFilePath))
        {
            var parsed = ComicFilenameParser.Parse(originalFilePath);
            scanInfo = parsed.ScanInformation;
        }

        // Fallback: If Series is empty, attempt to infer from original filename
        string series = comic.Series ?? string.Empty;
        string number = comic.Number ?? string.Empty;
        int? year = comic.Year;
        int? volume = comic.Volume;
        string title = comic.Title ?? string.Empty;
        string publisher = comic.Publisher ?? string.Empty;
        string format = comic.Format ?? string.Empty;

        if (string.IsNullOrWhiteSpace(series) && !string.IsNullOrWhiteSpace(originalFilePath))
        {
            var parsed = ComicFilenameParser.Parse(originalFilePath);
            series = parsed.Series;
            if (string.IsNullOrWhiteSpace(number)) number = parsed.IssueNumber;
            if (!year.HasValue) year = parsed.Year;
            if (!volume.HasValue) volume = parsed.Volume;
        }

        // If series is still empty, retain original file name without extension
        if (string.IsNullOrWhiteSpace(series))
        {
            return Path.GetFileName(originalFilePath);
        }

        // Token Replacement
        string result = TokenRegex.Replace(templatePattern, match =>
        {
            string token = match.Groups["token"].Value.ToLowerInvariant();
            string formatSpec = match.Groups["format"].Value;

            return token switch
            {
                "series" => series,
                "number" or "issue" => FormatIssueNumber(number, formatSpec),
                "year" => year.HasValue ? year.Value.ToString() : string.Empty,
                "month" => comic.Month.HasValue ? comic.Month.Value.ToString("D2") : string.Empty,
                "day" => comic.Day.HasValue ? comic.Day.Value.ToString("D2") : string.Empty,
                "volume" => volume.HasValue ? volume.Value.ToString() : string.Empty,
                "title" => title,
                "publisher" => publisher,
                "format" => format,
                "scaninfo" or "scan" => !string.IsNullOrWhiteSpace(scanInfo) ? scanInfo : string.Empty,
                _ => string.Empty
            };
        });

        // If template did not explicitly include {ScanInfo} and preserveScanInfo is true, append trailing tag
        if (preserveScanInfo && !templatePattern.Contains("{ScanInfo}", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(scanInfo))
        {
            // Avoid double wrapping if already in parens/brackets
            string formattedScan = scanInfo.StartsWith("(") || scanInfo.StartsWith("[")
                ? scanInfo
                : $"({scanInfo})";

            result = $"{result} {formattedScan}";
        }

        // Graceful collapse of empty tokens and brackets
        result = CleanAndCollapseFilename(result);

        // Sanitize illegal filesystem characters
        result = SanitizeFilename(result);

        // Append extension
        return $"{result}{originalExt}";
    }

    /// <summary>
    /// Generates batch rename previews with collision detection.
    /// </summary>
    public static List<RenameItemPreview> PreviewBatchRename(
        IEnumerable<(string FilePath, ComicInfo Comic)> items,
        string templatePattern = DefaultTemplate,
        bool preserveScanInfo = false)
    {
        var previews = new List<RenameItemPreview>();
        var seenPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (filePath, comic) in items)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) continue;

            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string proposedName = GenerateFilename(comic, filePath, templatePattern, preserveScanInfo);
            string proposedPath = Path.Combine(dir, proposedName);

            var preview = new RenameItemPreview
            {
                OriginalFilePath = filePath,
                ProposedFilename = proposedName,
                ProposedFilePath = proposedPath
            };

            // Detect collisions within the current batch
            if (seenPaths.TryGetValue(proposedPath, out int count))
            {
                preview.HasCollision = true;
                preview.ErrorMessage = $"Filename collision with another file in this batch.";
                seenPaths[proposedPath] = count + 1;
            }
            else
            {
                // Check if file already exists on disk (and is not itself)
                if (File.Exists(proposedPath) && !string.Equals(filePath, proposedPath, StringComparison.OrdinalIgnoreCase))
                {
                    preview.HasCollision = true;
                    preview.ErrorMessage = "A file with this name already exists on disk.";
                }

                seenPaths[proposedPath] = 1;
            }

            previews.Add(preview);
        }

        return previews;
    }

    /// <summary>
    /// Renames a single comic file on disk.
    /// </summary>
    public static string RenameFile(string originalFilePath, string newFilename, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(originalFilePath) || !File.Exists(originalFilePath))
        {
            throw new FileNotFoundException("Original comic file not found.", originalFilePath);
        }

        string dir = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
        string targetPath = Path.Combine(dir, newFilename);

        if (string.Equals(originalFilePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            // Case-only rename on case-insensitive filesystems (macOS / Windows)
            if (!string.Equals(originalFilePath, targetPath, StringComparison.Ordinal))
            {
                string tempPath = Path.Combine(dir, $"__inktag_rename_tmp_{Guid.NewGuid():N}{Path.GetExtension(originalFilePath)}");
                File.Move(originalFilePath, tempPath);
                File.Move(tempPath, targetPath);
                return targetPath;
            }
            return originalFilePath;
        }

        if (File.Exists(targetPath))
        {
            if (overwrite)
            {
                File.Delete(targetPath);
            }
            else
            {
                throw new IOException($"Target file '{targetPath}' already exists.");
            }
        }

        File.Move(originalFilePath, targetPath);
        return targetPath;
    }

    /// <summary>
    /// Executes a batch rename operation on a list of previewed items.
    /// </summary>
    public static RenameBatchResult ExecuteBatchRename(
        IEnumerable<RenameItemPreview> items,
        bool overwrite = false,
        IProgress<(int Processed, int Total, string Message)>? progress = null)
    {
        var list = items.ToList();
        var result = new RenameBatchResult
        {
            Total = list.Count,
            Items = list
        };

        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (!item.HasChange)
            {
                result.Skipped++;
                continue;
            }

            if (item.HasCollision && !overwrite)
            {
                result.Failed++;
                continue;
            }

            try
            {
                string newPath = RenameFile(item.OriginalFilePath, item.ProposedFilename, overwrite);
                item.ProposedFilePath = newPath;
                result.Renamed++;
            }
            catch (Exception ex)
            {
                item.ErrorMessage = ex.Message;
                result.Failed++;
            }

            progress?.Report((i + 1, list.Count, $"Renamed {i + 1}/{list.Count}: {item.ProposedFilename}"));
        }

        return result;
    }

    private static string FormatIssueNumber(string issueNumber, string formatSpec)
    {
        if (string.IsNullOrWhiteSpace(issueNumber)) return string.Empty;

        string clean = issueNumber.Trim().TrimStart('#');

        // Check if formatSpec specifies padding length e.g. "2", "3", "4", "000"
        int padLength = 0;
        if (int.TryParse(formatSpec, out int parsedPad))
        {
            padLength = parsedPad;
        }
        else if (!string.IsNullOrEmpty(formatSpec) && formatSpec.All(c => c == '0'))
        {
            padLength = formatSpec.Length;
        }

        if (padLength > 0)
        {
            // If whole number (e.g. "1", "13")
            if (int.TryParse(clean, out int intVal))
            {
                return intVal.ToString($"D{padLength}");
            }

            // If decimal/half issue (e.g. "1.5", "1½")
            var match = Regex.Match(clean, @"^(\d+)([\.½¼¾].*)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int wholePart))
            {
                return $"{wholePart.ToString($"D{padLength}")}{match.Groups[2].Value}";
            }
        }

        return clean;
    }

    public static string SanitizeFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return string.Empty;

        // Replace colons with space-dash-space
        string sanitized = filename.Replace(":", " - ");

        // Replace slashes with dashes
        sanitized = sanitized.Replace("/", "-").Replace("\\", "-");

        // Remove other invalid characters (* ? " < > |)
        sanitized = InvalidCharsRegex.Replace(sanitized, "");

        // Collapse multiple spaces
        sanitized = MultipleSpacesRegex.Replace(sanitized, " ").Trim();

        return sanitized;
    }

    private static string CleanAndCollapseFilename(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string cleaned = input;

        // Iteratively remove empty brackets e.g. (), [], {}
        int prevLen;
        do
        {
            prevLen = cleaned.Length;
            cleaned = EmptyParenthesesRegex.Replace(cleaned, "");
        } while (cleaned.Length != prevLen);

        // Clean redundant consecutive separators e.g. " - - " -> " - "
        cleaned = RedundantSeparatorsRegex.Replace(cleaned, " - ");

        // Remove dangling punctuation before end of string
        cleaned = DanglingPunctuationRegex.Replace(cleaned, "");

        // Remove leading punctuation
        cleaned = LeadingPunctuationRegex.Replace(cleaned, "");

        // Collapse whitespace
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();

        return cleaned;
    }
}

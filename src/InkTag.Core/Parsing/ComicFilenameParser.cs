using System;
using System.IO;
using System.Text.RegularExpressions;

namespace InkTag.Core.Parsing;

public record ParsedComicFilename
{
    public string Series { get; init; } = string.Empty;
    public string IssueNumber { get; init; } = string.Empty;
    public int? Year { get; init; }
    public int? Volume { get; init; }
    public string ScanInformation { get; init; } = string.Empty;
}

public static class ComicFilenameParser
{
    // Regex to match 4-digit years enclosed in parentheses, e.g. (2003), (1988)
    private static readonly Regex YearInParensRegex = new(@"\((19\d\d|20\d\d)\)", RegexOptions.Compiled);

    // Regex to match standalone 4-digit years with boundaries, e.g. " 2003 "
    private static readonly Regex StandaloneYearRegex = new(@"\b(19\d\d|20\d\d)\b", RegexOptions.Compiled);

    // Regex to match volume indicators, e.g. "v01", "v2", "vol. 1", "vol 2", "volume 3", "book 1"
    private static readonly Regex VolumeRegex = new(@"(?:^|[\s_\-\(\[])(?:v|vol\.?|volume|book)\s*0*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match explicit issue prefix: "#01", "#1.5", "issue 02", "no. 3", "c01"
    private static readonly Regex ExplicitIssueRegex = new(@"(?:#|issue\s*|no\.?\s*|c)(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match "X of Y" (e.g. "01 of 12", "01 (of 12)")
    private static readonly Regex OfCountRegex = new(@"(?:^|\s)0*(\d+(?:\.\d+)?)\s*(?:\(of\s*\d+\)|of\s*\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match scanner / release group / edition tags in parentheses or brackets, e.g. "(Miracle Man-LXC)", "(Digital)", "[c2c]"
    private static readonly Regex TagGroupRegex = new(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

    /// <summary>
    /// Parses series title, issue number, publication year, and volume from a comic filename or path.
    /// </summary>
    public static ParsedComicFilename Parse(string filenameOrPath)
    {
        if (string.IsNullOrWhiteSpace(filenameOrPath))
        {
            return new ParsedComicFilename();
        }

        // Get filename without extension
        string rawName = Path.GetFileNameWithoutExtension(filenameOrPath).Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return new ParsedComicFilename();
        }

        int? year = null;
        int? volume = null;
        string issueNumber = string.Empty;
        string scanInfo = string.Empty;

        // 1. Extract 4-digit Year
        var yearMatch = YearInParensRegex.Match(rawName);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int parsedYear))
        {
            year = parsedYear;
        }

        // 2. Extract Volume
        var volMatch = VolumeRegex.Match(rawName);
        if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out int parsedVol))
        {
            volume = parsedVol;
        }

        // 3. Extract Scan Information / Release Group
        // Find tags other than the year tag (e.g. (Miracle Man-LXC), (digital), (c2c))
        var tagMatches = TagGroupRegex.Matches(rawName);
        foreach (Match match in tagMatches)
        {
            string content = match.Value.Trim('(', ')', '[', ']').Trim();
            if (int.TryParse(content, out int y) && y >= 1900 && y <= 2100)
            {
                continue; // Skip year tag
            }

            if (content.StartsWith("of ", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Skip "(of 12)"
            }

            if (string.IsNullOrEmpty(scanInfo))
            {
                scanInfo = content;
            }
        }

        // 4. Clean tags from working name string to isolate Title and Issue #
        string workingName = TagGroupRegex.Replace(rawName, " ").Trim();
        // Replace underscores and multiple spaces
        workingName = Regex.Replace(workingName.Replace('_', ' '), @"\s+", " ").Trim();

        // 5. Extract Issue Number
        // Check "X of Y" first (e.g. "Watchmen 01 of 12")
        var ofMatch = OfCountRegex.Match(workingName);
        if (ofMatch.Success)
        {
            issueNumber = NormalizeIssue(ofMatch.Groups[1].Value);
            workingName = workingName.Substring(0, ofMatch.Index).Trim();
        }
        else
        {
            // Check explicit issue prefix "#01", "Issue 03"
            var explicitMatch = ExplicitIssueRegex.Match(workingName);
            if (explicitMatch.Success)
            {
                issueNumber = NormalizeIssue(explicitMatch.Groups[1].Value);
                workingName = workingName.Substring(0, explicitMatch.Index).Trim();
            }
            else
            {
                // Check trailing number (e.g. "Blankets 03", "The Amazing Spider-Man 300")
                var trailingNumMatch = Regex.Match(workingName, @"(?:\s+|^)0*(\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);
                if (trailingNumMatch.Success)
                {
                    issueNumber = NormalizeIssue(trailingNumMatch.Groups[1].Value);
                    workingName = workingName.Substring(0, trailingNumMatch.Index).Trim();
                }
            }
        }

        // 6. If year wasn't in parens, check for standalone year
        if (!year.HasValue)
        {
            var standaloneYearMatch = StandaloneYearRegex.Match(workingName);
            if (standaloneYearMatch.Success && int.TryParse(standaloneYearMatch.Groups[1].Value, out int sYear))
            {
                year = sYear;
                // Remove year from workingName
                workingName = workingName.Remove(standaloneYearMatch.Index, standaloneYearMatch.Length).Trim();
            }
        }

        // 7. Remove any volume prefix from series name if present (e.g. "Invincible v01" -> "Invincible")
        if (volMatch.Success)
        {
            workingName = VolumeRegex.Replace(workingName, "").Trim();
        }

        // 8. Clean up series title delimiters (trailing hyphens, colons)
        string series = Regex.Replace(workingName, @"[\s\-_:]+$", "").Trim();
        series = Regex.Replace(series, @"^[\s\-_:]+", "").Trim();

        return new ParsedComicFilename
        {
            Series = series,
            IssueNumber = issueNumber,
            Year = year,
            Volume = volume,
            ScanInformation = scanInfo
        };
    }

    private static string NormalizeIssue(string issueStr)
    {
        if (string.IsNullOrWhiteSpace(issueStr)) return string.Empty;
        if (double.TryParse(issueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            // If whole integer (e.g. "03" or "001"), convert to "3" or "1"
            if (val == Math.Floor(val))
            {
                return ((int)val).ToString();
            }
            // If decimal (e.g. "1.5"), preserve decimal format
            return val.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return issueStr.TrimStart('0');
    }
}

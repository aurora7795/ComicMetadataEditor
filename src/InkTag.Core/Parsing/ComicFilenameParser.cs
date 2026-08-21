using System;
using System.Collections.Generic;
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
    private static readonly Regex VolumeRegex = new(@"(?:^|[\s_\-\(\[])(?:v|vol\.?|volume|book|season|part)\s*0*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match explicit issue prefix: "#01", "#1.5", "issue 02", "no. 3", "c01"
    private static readonly Regex ExplicitIssueRegex = new(@"(?:#|issue\s*|no\.?\s*|c)(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match "X of Y" (e.g. "01 of 12", "01 (of 12)")
    private static readonly Regex OfCountRegex = new(@"(?:^|\s)0*(\d+(?:\.\d+)?)\s*(?:\(of\s*\d+\)|of\s*\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Regex to match scanner / release group / edition tags in parentheses or brackets, e.g. "(Miracle Man-LXC)", "(Digital)", "[c2c]"
    private static readonly Regex TagGroupRegex = new(@"[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

    // Generic library, category, and system directory names to ignore during parent folder traversal
    private static readonly HashSet<string> GenericDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Categories & mediums
        "Comics", "Comic", "Comic Books", "ComicBooks", "Western", "Manga", "Graphic Novels", "GraphicNovels",
        "Bande Dessinée", "Bande Dessinee", "BD", "Manhwa", "Manhua", "Webcomics", "Webtoons", "Anime",
        // Formats & packaging
        "Trades", "Trade Paperbacks", "TPB", "TPBs", "Omnibus", "Omnibuses", "Single Issues", "Singles",
        "Floppies", "One-Shots", "One Shots", "OneShot", "Digital", "Scans", "Complete", "Current", "Releases",
        "Ongoing", "Mini-Series", "Miniseries", "Annuals", "Specials",
        // Indexing & filesystem
        "0-9", "#", "A-Z", "Downloads", "Incoming", "Temp", "Tmp", "Desktop", "Documents", "Volumes",
        "Root", "Media", "Storage", "Share", "General", "Files", "Library", "Books", "eBooks"
    };

    /// <summary>
    /// Parses series title, issue number, publication year, and volume from a comic filename or path.
    /// If full path is provided and inspectParentHierarchy is true, interrogates parent folders to supplement missing information.
    /// </summary>
    public static ParsedComicFilename Parse(string filenameOrPath, bool inspectParentHierarchy = true)
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
                // Check trailing number (e.g. "Blankets 03", "The Amazing Spider-Man 300", "IM015", "IM_015", "IM-015", "ASM300", "015")
                var trailingNumMatch = Regex.Match(workingName, @"(?:[\s\-_#.]+|(?<=[A-Za-z])|^)0*(\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);
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

        // 9. Hierarchical Parent Directory Traversal
        // If series is missing/trivial/abbreviated, or year/volume is missing, interrogate parent and grandparent folders
        if (inspectParentHierarchy)
        {
            string? dirPath = Path.GetDirectoryName(filenameOrPath);
            if (!string.IsNullOrWhiteSpace(dirPath))
            {
                var (inferredSeries, inferredYear, inferredVolume) = InferFromDirectoryHierarchy(dirPath);

                if (!string.IsNullOrWhiteSpace(inferredSeries))
                {
                    if (string.IsNullOrWhiteSpace(series) || IsTrivialOrAbbreviatedSeriesName(series, inferredSeries))
                    {
                        series = inferredSeries;
                    }
                }

                if (!year.HasValue && inferredYear.HasValue)
                {
                    year = inferredYear;
                }

                if (!volume.HasValue && inferredVolume.HasValue)
                {
                    volume = inferredVolume;
                }
            }
        }

        return new ParsedComicFilename
        {
            Series = series,
            IssueNumber = issueNumber,
            Year = year,
            Volume = volume,
            ScanInformation = scanInfo
        };
    }

    /// <summary>
    /// Interrogates parent and grandparent directory names to extract series title, publication year, and volume.
    /// </summary>
    public static (string? Series, int? Year, int? Volume) InferFromDirectoryHierarchy(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return (null, null, null);
        }

        string? currentPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parentDirName = Path.GetFileName(currentPath);
        if (string.IsNullOrWhiteSpace(parentDirName))
        {
            return (null, null, null);
        }

        int? inferredVolume = null;
        int? inferredYear = null;
        string? inferredSeries = null;

        // Inspect immediate parent directory
        if (!IsGenericDirectoryName(parentDirName))
        {
            var (pSeries, pYear, pVol, isPureVol) = ParseDirectoryComponents(parentDirName);

            if (isPureVol)
            {
                inferredVolume = pVol;

                // Step up to Grandparent directory to get Series and Year
                string? grandparentPath = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrWhiteSpace(grandparentPath))
                {
                    string grandDirName = Path.GetFileName(grandparentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(grandDirName) && !IsGenericDirectoryName(grandDirName))
                    {
                        var (gSeries, gYear, gVol, _) = ParseDirectoryComponents(grandDirName);
                        if (!string.IsNullOrWhiteSpace(gSeries)) inferredSeries = gSeries;
                        if (gYear.HasValue) inferredYear = gYear;
                        if (!inferredVolume.HasValue && gVol.HasValue) inferredVolume = gVol;
                    }
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(pSeries)) inferredSeries = pSeries;
                if (pYear.HasValue) inferredYear = pYear;
                if (pVol.HasValue) inferredVolume = pVol;
            }
        }

        return (inferredSeries, inferredYear, inferredVolume);
    }

    /// <summary>
    /// Parses a single directory name for Series, Year, and Volume components.
    /// </summary>
    public static (string Series, int? Year, int? Volume, bool IsPureVolume) ParseDirectoryComponents(string dirName)
    {
        if (string.IsNullOrWhiteSpace(dirName))
        {
            return (string.Empty, null, null, false);
        }

        string cleanName = dirName.Trim();
        int? year = null;
        int? volume = null;

        // 1. Check for Year in parens, e.g. "The Avengers (1963)"
        var yearMatch = YearInParensRegex.Match(cleanName);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int pYear))
        {
            year = pYear;
        }

        // 2. Check for Volume, e.g. "Vol 1", "v02", "Volume 3", "Book 2"
        var volMatch = VolumeRegex.Match(cleanName);
        if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out int pVol))
        {
            volume = pVol;
        }

        // 3. Remove tags & years to get clean series title
        string workingName = TagGroupRegex.Replace(cleanName, " ").Trim();
        workingName = Regex.Replace(workingName.Replace('_', ' '), @"\s+", " ").Trim();

        if (volMatch.Success)
        {
            workingName = VolumeRegex.Replace(workingName, "").Trim();
        }

        // Clean up delimiters
        string series = Regex.Replace(workingName, @"[\s\-_:]+$", "").Trim();
        series = Regex.Replace(series, @"^[\s\-_:]+", "").Trim();

        if (!string.IsNullOrWhiteSpace(series))
        {
            // If the directory name was entirely lowercase (e.g. "iron man"), capitalize words ("Iron Man")
            if (series.Equals(series.ToLowerInvariant(), StringComparison.Ordinal))
            {
                series = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(series);
            }
        }

        // Check if this was purely a volume directory (e.g. "Vol 1", "v02", "Volume 1", "Book 1")
        bool isPureVolume = volMatch.Success && (string.IsNullOrWhiteSpace(series) || series.Length <= 1);

        return (series, year, volume, isPureVolume);
    }

    /// <summary>
    /// Determines whether a directory name is generic (categories, indexing, temp, decade, year-only) and should not be used as a Series title.
    /// </summary>
    public static bool IsGenericDirectoryName(string dirName)
    {
        if (string.IsNullOrWhiteSpace(dirName)) return true;

        string trimmed = dirName.Trim();

        // Single letter index folders (e.g. "A", "B", "C")
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0])) return true;

        // Check generic directory set
        if (GenericDirectoryNames.Contains(trimmed)) return true;

        // Standalone 4-digit years or decade folders (e.g. "2024", "1990s", "2010s")
        if (Regex.IsMatch(trimmed, @"^(19\d\d|20\d\d)(s)?$", RegexOptions.IgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// Checks if a series name parsed from a filename is uninformative, an issue prefix, or a short acronym/abbreviation
    /// of the parent directory series title.
    /// </summary>
    public static bool IsTrivialOrAbbreviatedSeriesName(string series, string? inferredSeries = null)
    {
        if (string.IsNullOrWhiteSpace(series)) return true;
        string trimmed = series.Trim();

        // 1. Purely digits (e.g. "048", "1")
        if (Regex.IsMatch(trimmed, @"^\d+$")) return true;

        // 2. Issue prefixes (e.g. "#01", "Issue 1", "No. 2", "c01", "Book 1", "Part 1")
        if (Regex.IsMatch(trimmed, @"^(#\s*\d+|issue\s*\d*|no\.?\s*\d*|c\d+|book\s*\d*|part\s*\d*)$", RegexOptions.IgnoreCase)) return true;

        // 3. If an inferred parent directory series is available:
        if (!string.IsNullOrWhiteSpace(inferredSeries))
        {
            // Short abbreviations / acronyms (<= 4 chars without spaces, e.g. "IM", "ASM", "UXM", "FF", "DD", "GL", "BM", "Cap")
            if (trimmed.Length <= 4 && !trimmed.Contains(' '))
            {
                return true;
            }

            // Check if series matches the initials of inferredSeries (e.g. "IM" for "Iron Man", "ASM" for "The Amazing Spider-Man")
            if (MatchesInitials(trimmed, inferredSeries))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesInitials(string acronym, string fullTitle)
    {
        if (string.IsNullOrWhiteSpace(acronym) || string.IsNullOrWhiteSpace(fullTitle)) return false;

        // Extract alphanumeric words from fullTitle
        var words = Regex.Matches(fullTitle, @"[A-Za-z0-9]+");
        if (words.Count == 0) return false;

        // 1. Full initials, e.g. "Iron Man" -> "IM", "The Amazing Spider-Man" -> "TASM"
        string fullInitials = string.Concat(System.Linq.Enumerable.Select(words.Cast<Match>(), w => w.Value[0]));
        if (acronym.Equals(fullInitials, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Initials ignoring leading articles ("The", "A", "An") -> "ASM"
        if (words.Count > 1 && (words[0].Value.Equals("The", StringComparison.OrdinalIgnoreCase) ||
                                words[0].Value.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                                words[0].Value.Equals("An", StringComparison.OrdinalIgnoreCase)))
        {
            string noArticleInitials = string.Concat(System.Linq.Enumerable.Select(words.Cast<Match>().Skip(1), w => w.Value[0]));
            if (acronym.Equals(noArticleInitials, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a series name parsed from a filename is uninformative or just an issue prefix (e.g. "#01", "048", "Issue 1").
    /// </summary>
    public static bool IsTrivialSeriesName(string series) => IsTrivialOrAbbreviatedSeriesName(series, null);

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


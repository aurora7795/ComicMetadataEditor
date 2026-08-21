using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace InkTag.Core.Parsing;

public static class ComicBookInfoParser
{
    /// <summary>
    /// Attempts to parse a ComicBookInfo JSON string (or ZIP archive comment) into a ComicInfo object.
    /// </summary>
    public static bool TryParse(string jsonString, out ComicInfo? comicInfo)
    {
        comicInfo = null;
        if (string.IsNullOrWhiteSpace(jsonString)) return false;

        try
        {
            var info = new ComicInfo();
            if (TryMergeFromLegacyJson(info, jsonString))
            {
                info.HasLegacyMetadata = true;
                comicInfo = info;
                return true;
            }
        }
        catch
        {
            // Invalid JSON or unrecognized format
        }

        return false;
    }

    /// <summary>
    /// Parses legacy ComicBookInfo JSON and merges non-empty fields into the target ComicInfo,
    /// filling missing values without overwriting existing data.
    /// </summary>
    public static bool TryMergeFromLegacyJson(ComicInfo target, string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString)) return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            JsonElement cbiElement = root;

            // Check for standard x-cbi wrapper: { "appID": "...", "x-cbi": { ... } }
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("x-cbi", out var xCbi) && xCbi.ValueKind == JsonValueKind.Object)
                {
                    cbiElement = xCbi;
                }
                else if (root.TryGetProperty("ComicBookInfo/1.0", out var cbiAlt) && cbiAlt.ValueKind == JsonValueKind.Object)
                {
                    cbiElement = cbiAlt;
                }
            }

            if (cbiElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            bool anyMerged = false;

            // Series
            string? series = GetString(cbiElement, "series");
            if (string.IsNullOrWhiteSpace(target.Series) && !string.IsNullOrWhiteSpace(series))
            {
                target.Series = series.Trim();
                anyMerged = true;
            }

            // Title
            string? title = GetString(cbiElement, "title");
            if (string.IsNullOrWhiteSpace(target.Title) && !string.IsNullOrWhiteSpace(title))
            {
                target.Title = title.Trim();
                anyMerged = true;
            }

            // Issue Number
            string? issue = GetString(cbiElement, "issue") ?? GetString(cbiElement, "issueNumber");
            if (string.IsNullOrWhiteSpace(target.Number) && !string.IsNullOrWhiteSpace(issue))
            {
                target.Number = issue.Trim();
                anyMerged = true;
            }

            // Volume
            int? volume = GetInt(cbiElement, "volume");
            if (!target.Volume.HasValue && volume.HasValue)
            {
                target.Volume = volume.Value;
                anyMerged = true;
            }

            // Count / Total Issues
            int? count = GetInt(cbiElement, "numberOfIssues") ?? GetInt(cbiElement, "numberOfVolumes") ?? GetInt(cbiElement, "count");
            if (!target.Count.HasValue && count.HasValue)
            {
                target.Count = count.Value;
                anyMerged = true;
            }

            // Publisher & Imprint
            string? publisher = GetString(cbiElement, "publisher");
            if (string.IsNullOrWhiteSpace(target.Publisher) && !string.IsNullOrWhiteSpace(publisher))
            {
                target.Publisher = publisher.Trim();
                anyMerged = true;
            }

            string? imprint = GetString(cbiElement, "imprint");
            if (string.IsNullOrWhiteSpace(target.Imprint) && !string.IsNullOrWhiteSpace(imprint))
            {
                target.Imprint = imprint.Trim();
                anyMerged = true;
            }

            // Publication Year / Month
            int? year = GetInt(cbiElement, "publicationYear") ?? GetInt(cbiElement, "year");
            if (!target.Year.HasValue && year.HasValue)
            {
                target.Year = year.Value;
                anyMerged = true;
            }

            int? month = GetInt(cbiElement, "publicationMonth") ?? GetInt(cbiElement, "month");
            if (!target.Month.HasValue && month.HasValue)
            {
                target.Month = month.Value;
                anyMerged = true;
            }

            // Genre
            string? genre = GetString(cbiElement, "genre");
            if (string.IsNullOrWhiteSpace(target.Genre) && !string.IsNullOrWhiteSpace(genre))
            {
                target.Genre = genre.Trim();
                anyMerged = true;
            }

            // Tags
            string? tags = GetStringOrArray(cbiElement, "tags");
            if (string.IsNullOrWhiteSpace(target.Tags) && !string.IsNullOrWhiteSpace(tags))
            {
                target.Tags = tags.Trim();
                anyMerged = true;
            }

            // Summary / Comments
            string? summary = GetString(cbiElement, "comments") ?? GetString(cbiElement, "summary");
            if (string.IsNullOrWhiteSpace(target.Summary) && !string.IsNullOrWhiteSpace(summary))
            {
                target.Summary = summary.Trim();
                anyMerged = true;
            }

            // Community Rating
            if (!target.CommunityRating.HasValue)
            {
                decimal? rating = GetDecimal(cbiElement, "rating");
                if (rating.HasValue)
                {
                    target.CommunityRating = rating.Value;
                    anyMerged = true;
                }
            }

            // Credits mapping (Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor)
            if (cbiElement.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                var writers = new List<string>();
                var pencillers = new List<string>();
                var inkers = new List<string>();
                var colorists = new List<string>();
                var letterers = new List<string>();
                var coverArtists = new List<string>();
                var editors = new List<string>();

                foreach (var credit in credits.EnumerateArray())
                {
                    if (credit.ValueKind != JsonValueKind.Object) continue;

                    string? person = GetString(credit, "person") ?? GetString(credit, "name");
                    string? role = GetString(credit, "role") ?? GetString(credit, "primary");

                    if (string.IsNullOrWhiteSpace(person) || string.IsNullOrWhiteSpace(role)) continue;
                    person = person.Trim();
                    role = role.Trim();

                    if (IsRole(role, "writer", "script", "author")) writers.Add(person);
                    else if (IsRole(role, "penciller", "pencils", "artist", "art", "drawings")) pencillers.Add(person);
                    else if (IsRole(role, "inker", "inks")) inkers.Add(person);
                    else if (IsRole(role, "colorist", "colors", "colourist", "colours")) colorists.Add(person);
                    else if (IsRole(role, "letterer", "letters")) letterers.Add(person);
                    else if (IsRole(role, "cover", "cover artist", "cover designer")) coverArtists.Add(person);
                    else if (IsRole(role, "editor", "editorial")) editors.Add(person);
                }

                if (string.IsNullOrWhiteSpace(target.Writer) && writers.Count > 0)
                {
                    target.Writer = string.Join(", ", writers.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.Penciller) && pencillers.Count > 0)
                {
                    target.Penciller = string.Join(", ", pencillers.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.Inker) && inkers.Count > 0)
                {
                    target.Inker = string.Join(", ", inkers.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.Colorist) && colorists.Count > 0)
                {
                    target.Colorist = string.Join(", ", colorists.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.Letterer) && letterers.Count > 0)
                {
                    target.Letterer = string.Join(", ", letterers.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.CoverArtist) && coverArtists.Count > 0)
                {
                    target.CoverArtist = string.Join(", ", coverArtists.Distinct());
                    anyMerged = true;
                }
                if (string.IsNullOrWhiteSpace(target.Editor) && editors.Count > 0)
                {
                    target.Editor = string.Join(", ", editors.Distinct());
                    anyMerged = true;
                }
            }

            return anyMerged;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRole(string role, params string[] matchRoles)
    {
        return matchRoles.Any(r => string.Equals(role, r, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetRawText();
            }
        }
        return null;
    }

    private static string? GetStringOrArray(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            if (prop.ValueKind == JsonValueKind.Array)
            {
                var items = prop.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (items.Count > 0)
                {
                    return string.Join(", ", items);
                }
            }
        }
        return null;
    }

    private static int? GetInt(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
            {
                return val;
            }
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static decimal? GetDecimal(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out decimal val))
            {
                return val;
            }
            if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
            {
                return parsed;
            }
        }
        return null;
    }
}

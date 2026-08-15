using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InkTag.Core.Scrapers;

public class ComicVineProvider : IMetadataScraperProvider
{
    private readonly RateLimitedHttpClient _httpClient;
    private readonly ScraperCacheService? _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromDays(7);

    public string ProviderName => "ComicVine";
    public bool RequiresApiKey => true;
    public bool SupportsSeriesSearch => true;

    public ComicVineProvider(RateLimitedHttpClient? httpClient = null, ScraperCacheService? cache = null)
    {
        _httpClient = httpClient ?? new RateLimitedHttpClient();
        _cache = cache;
    }

    public async Task<IEnumerable<ComicSearchResult>> SearchAsync(ComicSearchQuery query, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("ComicVine API key is required.");
        }

        string rawQuery = $"{query.Series} {query.IssueNumber}".Trim();
        string cacheKey = $"cv_search_{rawQuery}";

        string? json = _cache?.Get(cacheKey, _cacheDuration);
        if (string.IsNullOrEmpty(json))
        {
            string url = $"https://comicvine.gamespot.com/api/search/?api_key={Uri.EscapeDataString(apiKey)}&format=json&resources=issue&query={Uri.EscapeDataString(rawQuery)}";
            json = await _httpClient.GetStringAsync(url, ct);
            _cache?.Set(cacheKey, json);
        }

        return ParseSearchResults(json, query);
    }

    public async Task<ComicInfo> FetchComicMetadataAsync(string issueId, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("ComicVine API key is required.");
        }

        string normalizedId = issueId.StartsWith("4000-") ? issueId : $"4000-{issueId}";
        string cacheKey = $"cv_issue_{normalizedId}";

        string? json = _cache?.Get(cacheKey, _cacheDuration);
        if (string.IsNullOrEmpty(json))
        {
            string url = $"https://comicvine.gamespot.com/api/issue/{normalizedId}/?api_key={Uri.EscapeDataString(apiKey)}&format=json";
            json = await _httpClient.GetStringAsync(url, ct);
            _cache?.Set(cacheKey, json);
        }

        return ParseIssueDetails(json);
    }

    public async Task<IEnumerable<SeriesSearchResult>> SearchSeriesAsync(string seriesTitle, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("ComicVine API key is required.");
        }

        string rawQuery = seriesTitle.Trim();
        string cacheKey = $"cv_series_search_{rawQuery}";

        string? json = _cache?.Get(cacheKey, _cacheDuration);
        if (string.IsNullOrEmpty(json))
        {
            string url = $"https://comicvine.gamespot.com/api/search/?api_key={Uri.EscapeDataString(apiKey)}&format=json&resources=volume&query={Uri.EscapeDataString(rawQuery)}";
            json = await _httpClient.GetStringAsync(url, ct);
            _cache?.Set(cacheKey, json);
        }

        return ParseSeriesSearchResults(json);
    }

    public async Task<IEnumerable<ComicSearchResult>> FetchSeriesIssuesAsync(string volumeId, string apiKey, int page = 1, int pageSize = 50, ComicSearchQuery? query = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("ComicVine API key is required.");
        }

        string cleanVolumeId = volumeId.StartsWith("4050-") ? volumeId.Substring(5) : volumeId;
        int offset = Math.Max(0, (page - 1) * pageSize);
        string cacheKey = $"cv_series_issues_{cleanVolumeId}_p{page}_s{pageSize}";

        string? json = _cache?.Get(cacheKey, _cacheDuration);
        if (string.IsNullOrEmpty(json))
        {
            string url = $"https://comicvine.gamespot.com/api/issues/?api_key={Uri.EscapeDataString(apiKey)}&format=json&filter=volume:{cleanVolumeId}&limit={pageSize}&offset={offset}&sort=issue_number:asc";
            json = await _httpClient.GetStringAsync(url, ct);
            _cache?.Set(cacheKey, json);
        }

        return ParseSeriesIssuesResults(json, query);
    }

    private IEnumerable<SeriesSearchResult> ParseSeriesSearchResults(string json)
    {
        var results = new List<SeriesSearchResult>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status_code", out var status) || status.GetInt32() != 1)
        {
            return results;
        }

        if (!root.TryGetProperty("results", out var resultsArray) || resultsArray.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var elem in resultsArray.EnumerateArray())
        {
            string id = elem.TryGetProperty("id", out var idProp) ? idProp.ToString() : string.Empty;
            string name = elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() ?? "" : "";
            string siteUrl = elem.TryGetProperty("site_detail_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String ? urlProp.GetString() ?? "" : "";
            string desc = elem.TryGetProperty("deck", out var deckProp) && deckProp.ValueKind == JsonValueKind.String ? deckProp.GetString() ?? "" : "";

            int? startYear = null;
            if (elem.TryGetProperty("start_year", out var yrProp))
            {
                if (yrProp.ValueKind == JsonValueKind.Number) startYear = yrProp.GetInt32();
                else if (yrProp.ValueKind == JsonValueKind.String && int.TryParse(yrProp.GetString(), out int y)) startYear = y;
            }

            int? issueCount = null;
            if (elem.TryGetProperty("count_of_issues", out var cntProp) && cntProp.ValueKind == JsonValueKind.Number)
            {
                issueCount = cntProp.GetInt32();
            }

            string publisher = "";
            if (elem.TryGetProperty("publisher", out var pubObj) && pubObj.ValueKind == JsonValueKind.Object)
            {
                if (pubObj.TryGetProperty("name", out var pName)) publisher = pName.GetString() ?? "";
            }

            string mediumUrl = "";
            string smallUrl = "";
            if (elem.TryGetProperty("image", out var imgObj) && imgObj.ValueKind == JsonValueKind.Object)
            {
                if (imgObj.TryGetProperty("medium_url", out var mUrl)) mediumUrl = mUrl.GetString() ?? "";
                if (imgObj.TryGetProperty("small_url", out var sUrl)) smallUrl = sUrl.GetString() ?? "";
            }

            results.Add(new SeriesSearchResult
            {
                VolumeId = id,
                SeriesTitle = name,
                Publisher = publisher,
                StartYear = startYear,
                CountOfIssues = issueCount,
                CoverUrl = mediumUrl,
                SmallCoverUrl = smallUrl,
                SiteDetailUrl = siteUrl,
                Description = desc
            });
        }

        return results;
    }

    private IEnumerable<ComicSearchResult> ParseSeriesIssuesResults(string json, ComicSearchQuery? query = null)
    {
        var results = new List<ComicSearchResult>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status_code", out var status) || status.GetInt32() != 1)
        {
            return results;
        }

        if (!root.TryGetProperty("results", out var resultsArray) || resultsArray.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var elem in resultsArray.EnumerateArray())
        {
            string id = elem.TryGetProperty("id", out var idProp) ? idProp.ToString() : string.Empty;
            string name = elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() ?? "" : "";
            string issueNum = elem.TryGetProperty("issue_number", out var numProp) ? numProp.ToString() : "";
            string coverDate = elem.TryGetProperty("cover_date", out var dateProp) && dateProp.ValueKind == JsonValueKind.String ? dateProp.GetString() ?? "" : "";
            string siteUrl = elem.TryGetProperty("site_detail_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String ? urlProp.GetString() ?? "" : "";

            string volumeName = "";
            string volumeId = "";
            if (elem.TryGetProperty("volume", out var volObj) && volObj.ValueKind == JsonValueKind.Object)
            {
                if (volObj.TryGetProperty("name", out var vName)) volumeName = vName.GetString() ?? "";
                if (volObj.TryGetProperty("id", out var vId)) volumeId = vId.ToString();
            }

            string mediumUrl = "";
            string smallUrl = "";
            if (elem.TryGetProperty("image", out var imgObj) && imgObj.ValueKind == JsonValueKind.Object)
            {
                if (imgObj.TryGetProperty("medium_url", out var mUrl)) mediumUrl = mUrl.GetString() ?? "";
                if (imgObj.TryGetProperty("small_url", out var sUrl)) smallUrl = sUrl.GetString() ?? "";
            }

            var item = new ComicSearchResult
            {
                IssueId = id,
                VolumeId = volumeId,
                SeriesTitle = volumeName,
                IssueNumber = issueNum,
                IssueTitle = name,
                CoverDate = coverDate,
                CoverUrl = mediumUrl,
                SmallCoverUrl = smallUrl,
                SiteDetailUrl = siteUrl
            };

            if (query != null)
            {
                item.MatchConfidence = CalculateConfidence(item, query);
            }

            results.Add(item);
        }

        return results.OrderBy(ParseIssueNumberForSort).ThenBy(r => r.IssueNumber);
    }

    private static double ParseIssueNumberForSort(ComicSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.IssueNumber)) return double.MaxValue;
        var match = Regex.Match(result.IssueNumber, @"\d+(\.\d+)?");
        if (match.Success && double.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }
        return double.MaxValue;
    }


    private IEnumerable<ComicSearchResult> ParseSearchResults(string json, ComicSearchQuery query)
    {
        var results = new List<ComicSearchResult>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status_code", out var status) || status.GetInt32() != 1)
        {
            return results;
        }

        if (!root.TryGetProperty("results", out var resultsArray) || resultsArray.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var elem in resultsArray.EnumerateArray())
        {
            string id = elem.TryGetProperty("id", out var idProp) ? idProp.ToString() : string.Empty;
            string name = elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() ?? "" : "";
            string issueNum = elem.TryGetProperty("issue_number", out var numProp) ? numProp.ToString() : "";
            string coverDate = elem.TryGetProperty("cover_date", out var dateProp) && dateProp.ValueKind == JsonValueKind.String ? dateProp.GetString() ?? "" : "";
            string siteUrl = elem.TryGetProperty("site_detail_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String ? urlProp.GetString() ?? "" : "";

            string volumeName = "";
            string volumeId = "";
            if (elem.TryGetProperty("volume", out var volObj) && volObj.ValueKind == JsonValueKind.Object)
            {
                if (volObj.TryGetProperty("name", out var vName)) volumeName = vName.GetString() ?? "";
                if (volObj.TryGetProperty("id", out var vId)) volumeId = vId.ToString();
            }

            string mediumUrl = "";
            string smallUrl = "";
            if (elem.TryGetProperty("image", out var imgObj) && imgObj.ValueKind == JsonValueKind.Object)
            {
                if (imgObj.TryGetProperty("medium_url", out var mUrl)) mediumUrl = mUrl.GetString() ?? "";
                if (imgObj.TryGetProperty("small_url", out var sUrl)) smallUrl = sUrl.GetString() ?? "";
            }

            var item = new ComicSearchResult
            {
                IssueId = id,
                VolumeId = volumeId,
                SeriesTitle = volumeName,
                IssueNumber = issueNum,
                IssueTitle = name,
                CoverDate = coverDate,
                CoverUrl = mediumUrl,
                SmallCoverUrl = smallUrl,
                SiteDetailUrl = siteUrl
            };

            item.MatchConfidence = CalculateConfidence(item, query);
            results.Add(item);
        }

        return results.OrderByDescending(r => r.MatchConfidence);
    }

    public static double CalculateConfidence(ComicSearchResult result, ComicSearchQuery query, ulong? localCoverHash = null)
    {
        double textScore = 0.0;

        // Series title similarity
        if (!string.IsNullOrEmpty(query.Series) && !string.IsNullOrEmpty(result.SeriesTitle))
        {
            string cleanSearchSeries = CleanString(query.Series);
            string cleanResultSeries = CleanString(result.SeriesTitle);

            if (cleanSearchSeries.Equals(cleanResultSeries, StringComparison.OrdinalIgnoreCase))
            {
                textScore += 0.5;
            }
            else if (cleanResultSeries.Contains(cleanSearchSeries, StringComparison.OrdinalIgnoreCase) ||
                     cleanSearchSeries.Contains(cleanResultSeries, StringComparison.OrdinalIgnoreCase))
            {
                textScore += 0.3;
            }
        }

        // Issue number similarity
        if (!string.IsNullOrEmpty(query.IssueNumber) && !string.IsNullOrEmpty(result.IssueNumber))
        {
            if (NormalizeIssueNumber(query.IssueNumber) == NormalizeIssueNumber(result.IssueNumber))
            {
                textScore += 0.35;
            }
        }

        int? candidateYear = null;
        if (!string.IsNullOrEmpty(result.CoverDate) && DateTime.TryParse(result.CoverDate, out var date))
        {
            candidateYear = date.Year;
        }

        bool hasSevereYearMismatch = false;

        // Year similarity & mismatch penalty
        if (query.Year.HasValue && candidateYear.HasValue)
        {
            int yearDiff = Math.Abs(query.Year.Value - candidateYear.Value);
            if (yearDiff == 0)
            {
                textScore += 0.25; // Exact year match
            }
            else if (yearDiff == 1)
            {
                textScore += 0.15; // Adjacent year (publication/cover date difference)
            }
            else
            {
                hasSevereYearMismatch = true;
                textScore -= 0.40; // Severe mismatch penalty for different comic runs/decades
            }
        }

        textScore = Math.Clamp(textScore, 0.0, 1.0);

        // Visual Cover Similarity (if both local and online hashes are present)
        if (localCoverHash.HasValue && localCoverHash.Value != 0 && result.CoverHash.HasValue && result.CoverHash.Value != 0)
        {
            double visualSimilarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(localCoverHash.Value, result.CoverHash.Value);
            result.VisualSimilarity = visualSimilarity;

            // If there is a severe year mismatch, prevent visual override from promoting the wrong volume
            if (hasSevereYearMismatch)
            {
                return Math.Min(0.40, (textScore * 0.5) + (visualSimilarity * 0.5));
            }

            // Visual Override Strategy:
            // If visual match is extremely high (>= 90%) and year is not contradictory, treat cover as primary confirmation (95%+ confidence)
            if (visualSimilarity >= 0.90)
            {
                return Math.Max(0.95, (textScore * 0.2) + (visualSimilarity * 0.8));
            }
            if (visualSimilarity >= 0.75)
            {
                return (textScore * 0.5) + (visualSimilarity * 0.5);
            }
            return (textScore * 0.7) + (visualSimilarity * 0.3);
        }

        return textScore;
    }

    private ComicInfo ParseIssueDetails(string json)
    {
        var comic = new ComicInfo();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status_code", out var status) || status.GetInt32() != 1)
        {
            return comic;
        }

        if (!root.TryGetProperty("results", out var res) || res.ValueKind != JsonValueKind.Object)
        {
            return comic;
        }

        if (res.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            comic.Title = nameProp.GetString();
        }

        if (res.TryGetProperty("issue_number", out var numProp))
        {
            comic.Number = numProp.ToString();
        }

        if (res.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
        {
            string rawHtml = descProp.GetString() ?? "";
            comic.Summary = StripHtml(rawHtml);
        }
        else if (res.TryGetProperty("deck", out var deckProp) && deckProp.ValueKind == JsonValueKind.String)
        {
            comic.Summary = deckProp.GetString();
        }

        if (res.TryGetProperty("cover_date", out var dateProp) && dateProp.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(dateProp.GetString(), out var coverDate))
        {
            comic.Year = coverDate.Year;
            comic.Month = coverDate.Month;
            comic.Day = coverDate.Day;
        }

        if (res.TryGetProperty("volume", out var volObj) && volObj.ValueKind == JsonValueKind.Object)
        {
            if (volObj.TryGetProperty("name", out var vName))
            {
                comic.Series = vName.GetString();
            }
        }

        if (res.TryGetProperty("site_detail_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
        {
            comic.Web = urlProp.GetString();
        }

        // Parse Credits
        if (res.TryGetProperty("person_credits", out var persons) && persons.ValueKind == JsonValueKind.Array)
        {
            var writers = new List<string>();
            var pencillers = new List<string>();
            var inkers = new List<string>();
            var colorists = new List<string>();
            var letterers = new List<string>();
            var coverArtists = new List<string>();
            var editors = new List<string>();

            foreach (var person in persons.EnumerateArray())
            {
                string pName = person.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string pRole = person.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(pName)) continue;
                string roleLower = pRole.ToLowerInvariant();

                if (roleLower.Contains("writer") || roleLower.Contains("script")) writers.Add(pName);
                if (roleLower.Contains("penciller") || roleLower.Contains("artist") || roleLower.Contains("breakdowns")) pencillers.Add(pName);
                if (roleLower.Contains("inker") || roleLower.Contains("finishes")) inkers.Add(pName);
                if (roleLower.Contains("colorist") || roleLower.Contains("colors")) colorists.Add(pName);
                if (roleLower.Contains("letterer") || roleLower.Contains("letters")) letterers.Add(pName);
                if (roleLower.Contains("cover")) coverArtists.Add(pName);
                if (roleLower.Contains("editor")) editors.Add(pName);
            }

            if (writers.Any()) comic.Writer = string.Join(", ", writers.Distinct());
            if (pencillers.Any()) comic.Penciller = string.Join(", ", pencillers.Distinct());
            if (inkers.Any()) comic.Inker = string.Join(", ", inkers.Distinct());
            if (colorists.Any()) comic.Colorist = string.Join(", ", colorists.Distinct());
            if (letterers.Any()) comic.Letterer = string.Join(", ", letterers.Distinct());
            if (coverArtists.Any()) comic.CoverArtist = string.Join(", ", coverArtists.Distinct());
            if (editors.Any()) comic.Editor = string.Join(", ", editors.Distinct());
        }

        // Characters, Teams, Locations
        if (res.TryGetProperty("character_credits", out var chars) && chars.ValueKind == JsonValueKind.Array)
        {
            var charList = chars.EnumerateArray()
                .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n));
            comic.Characters = string.Join(", ", charList!);
        }

        if (res.TryGetProperty("team_credits", out var teams) && teams.ValueKind == JsonValueKind.Array)
        {
            var teamList = teams.EnumerateArray()
                .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n));
            comic.Teams = string.Join(", ", teamList!);
        }

        if (res.TryGetProperty("location_credits", out var locs) && locs.ValueKind == JsonValueKind.Array)
        {
            var locList = locs.EnumerateArray()
                .Select(l => l.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n));
            comic.Locations = string.Join(", ", locList!);
        }

        return comic;
    }

    private static string CleanString(string input) => Regex.Replace(input, @"[^\w\s]", "").Trim();
    private static string NormalizeIssueNumber(string input) => Regex.Replace(input, @"^[^\d]+", "").TrimStart('0');

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        string clean = Regex.Replace(html, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(clean).Trim();
    }
}

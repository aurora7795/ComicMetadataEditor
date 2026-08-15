using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Configuration;

namespace InkTag.Core.Scrapers;

public class ScrapeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ComicInfo TargetComic { get; set; } = new();
    public ComicSearchResult? SelectedCandidate { get; set; }
    public IEnumerable<ComicSearchResult> Candidates { get; set; } = Array.Empty<ComicSearchResult>();
    public bool RequiredUserSelection { get; set; }
}

public class MetadataScraperService
{
    private readonly AppSettingsService _settingsService;
    private readonly IMetadataScraperProvider _provider;

    public MetadataScraperService(AppSettingsService settingsService, IMetadataScraperProvider? provider = null)
    {
        _settingsService = settingsService;
        _provider = provider ?? new ComicVineProvider(null, new ScraperCacheService());
    }

    public async Task<IEnumerable<ComicSearchResult>> SearchCandidatesAsync(ComicSearchQuery query, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable.");
        }

        var globalResults = (await _provider.SearchAsync(query, apiKey, ct)).ToList();

        // Volume-First Resolution: If series title and year are present, find the matching volume
        // and include its issues so the correct publication run is guaranteed to be in the candidate list
        if (_provider.SupportsSeriesSearch && !string.IsNullOrWhiteSpace(query.Series) && query.Year.HasValue)
        {
            try
            {
                var volumes = await _provider.SearchSeriesAsync(query.Series, apiKey, ct);
                var matchingVolume = volumes.FirstOrDefault(v => v.StartYear.HasValue && Math.Abs(v.StartYear.Value - query.Year.Value) <= 1);
                
                if (matchingVolume != null)
                {
                    var volumeIssues = await _provider.FetchSeriesIssuesAsync(matchingVolume.VolumeId, apiKey, 1, 50, query, ct);
                    var seenIds = new HashSet<string>(globalResults.Select(r => r.IssueId));
                    foreach (var vIssue in volumeIssues)
                    {
                        if (seenIds.Add(vIssue.IssueId))
                        {
                            globalResults.Add(vIssue);
                        }
                    }
                }
            }
            catch
            {
                // Fallback to standard global results on any lookup error
            }
        }

        return globalResults.OrderByDescending(r => r.MatchConfidence);
    }

    public async Task<ComicInfo> FetchMetadataAsync(string issueId, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured.");
        }

        return await _provider.FetchComicMetadataAsync(issueId, apiKey, ct);
    }

    public bool SupportsSeriesSearch => _provider.SupportsSeriesSearch;

    public async Task<IEnumerable<SeriesSearchResult>> SearchSeriesAsync(string seriesTitle, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable.");
        }

        return await _provider.SearchSeriesAsync(seriesTitle, apiKey, ct);
    }

    public async Task<IEnumerable<ComicSearchResult>> FetchSeriesIssuesAsync(string volumeId, int page = 1, int pageSize = 50, ComicSearchQuery? query = null, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable.");
        }

        return await _provider.FetchSeriesIssuesAsync(volumeId, apiKey, page, pageSize, query, ct);
    }

    public async Task<ScrapeResult> AutoScrapeComicAsync(ComicInfo existingComic, ulong? localCoverHash = null, CancellationToken ct = default)
    {
        var query = ExtractQueryFromComicInfo(existingComic);
        var candidates = (await SearchCandidatesAsync(query, ct)).ToList();

        if (!candidates.Any())
        {
            return new ScrapeResult
            {
                Success = false,
                Message = $"No matching results found for '{query}' on {_provider.ProviderName}.",
                TargetComic = existingComic
            };
        }

        // If local cover hash is available and auto-visual match is enabled, evaluate candidate cover hashes
        if (localCoverHash.HasValue && localCoverHash.Value != 0 && _settingsService.Settings.AutoApplyOnVisualMatch)
        {
            foreach (var candidate in candidates.Take(5))
            {
                string coverUrl = !string.IsNullOrEmpty(candidate.SmallCoverUrl) ? candidate.SmallCoverUrl : candidate.CoverUrl;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    try
                    {
                        using var client = new System.Net.Http.HttpClient();
                        client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ComicMetadataEditor)");
                        byte[] bytes = await client.GetByteArrayAsync(coverUrl, ct);
                        ulong onlineHash = InkTag.Core.Images.PerceptualHashService.ComputeDHash(bytes);
                        candidate.CoverHash = onlineHash;
                        candidate.MatchConfidence = ComicVineProvider.CalculateConfidence(candidate, query, localCoverHash);
                    }
                    catch
                    {
                        // Ignore individual thumbnail download errors
                    }
                }
            }

            // Re-order candidates by updated confidence
            candidates = candidates.OrderByDescending(c => c.MatchConfidence).ToList();
        }

        var topMatch = candidates.First();
        double threshold = _settingsService.Settings.AutoMatchConfidenceThreshold;

        if (topMatch.MatchConfidence >= threshold)
        {
            var fetchedMetadata = await FetchMetadataAsync(topMatch.IssueId, ct);
            ApplyMetadata(existingComic, fetchedMetadata, _settingsService.Settings.DefaultMergeMode);

            string visualNote = topMatch.VisualSimilarity.HasValue && topMatch.VisualSimilarity.Value >= 0.70 
                ? $" [Cover Match: {topMatch.VisualSimilarity.Value:P0}]" 
                : "";

            return new ScrapeResult
            {
                Success = true,
                Message = $"Successfully scraped metadata from '{topMatch.SeriesTitle} #{topMatch.IssueNumber}'{visualNote} (Confidence: {topMatch.MatchConfidence:P0}).",
                TargetComic = existingComic,
                SelectedCandidate = topMatch,
                Candidates = candidates
            };
        }

        return new ScrapeResult
        {
            Success = false,
            Message = $"Low confidence match ({topMatch.MatchConfidence:P0} < threshold {threshold:P0}). Manual candidate selection required.",
            TargetComic = existingComic,
            Candidates = candidates,
            RequiredUserSelection = true
        };
    }

    public void ApplyMetadata(ComicInfo target, ComicInfo fetched, ScrapeMergeMode mode, HashSet<string>? allowedFields = null)
    {
        PropertyInfo[] properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name == nameof(ComicInfo.PageCount) || prop.Name == nameof(ComicInfo.Pages)) continue;

            if (allowedFields != null && !allowedFields.Contains(prop.Name))
            {
                continue;
            }

            object? fetchedVal = prop.GetValue(fetched);
            if (fetchedVal == null) continue;

            object? targetVal = prop.GetValue(target);

            if (mode == ScrapeMergeMode.OverwriteAll || allowedFields != null)
            {
                prop.SetValue(target, fetchedVal);
            }
            else if (mode == ScrapeMergeMode.FillMissingOnly)
            {
                if (IsMissingValue(targetVal))
                {
                    prop.SetValue(target, fetchedVal);
                }
            }
        }
    }

    public static ComicSearchQuery ExtractQueryFromComicInfo(ComicInfo comic)
    {
        return new ComicSearchQuery
        {
            Series = comic.Series ?? string.Empty,
            IssueNumber = comic.Number ?? string.Empty,
            Year = comic.Year
        };
    }

    private static bool IsMissingValue(object? value)
    {
        if (value == null) return true;
        if (value is string s) return string.IsNullOrWhiteSpace(s);
        if (value is int i) return i == 0;
        return false;
    }
}

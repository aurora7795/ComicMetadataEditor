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
    public bool DetectedIntroPage { get; set; }
    public int TrueCoverPageIndex { get; set; }
    public string? IntroPageKey { get; set; }
}

public class MetadataScraperService : IDisposable
{
    private readonly AppSettingsService _settingsService;
    private readonly IMetadataScraperProvider _provider;

    public MetadataScraperService(AppSettingsService settingsService, IMetadataScraperProvider? provider = null)
    {
        _settingsService = settingsService;
        _provider = provider ?? new ComicVineProvider(null, new ScraperCacheService());
    }

    public void FlushCache() => (_provider as ComicVineProvider)?.FlushCache();

    /// <summary>
    /// Flushes the provider's scraper cache to disk. Essential for one-shot processes (CLI / MCP
    /// tool invocations) that exit before the cache's debounced background write can fire.
    /// </summary>
    public void Dispose()
    {
        (_provider as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<IEnumerable<ComicSearchResult>> SearchCandidatesAsync(ComicSearchQuery query, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable. Acquire a free API key at https://comicvine.gamespot.com/api/");
        }

        var globalResults = (await _provider.SearchAsync(query, apiKey, ct)).ToList();

        // Volume-First Resolution: If series title and year are present, find the matching volume
        // and include its issues so the correct publication run is guaranteed to be in the candidate list
        if (_provider.SupportsSeriesSearch && !string.IsNullOrWhiteSpace(query.Series) && query.Year.HasValue)
        {
            try
            {
                var searchTitles = new List<string> { query.Series };
                if (query.Series.StartsWith("Buffy:", StringComparison.OrdinalIgnoreCase))
                {
                    searchTitles.Add("Buffy the Vampire Slayer: " + query.Series.Substring(6).Trim());
                    searchTitles.Add(query.Series.Substring(6).Trim());
                }
                else if (query.Series.StartsWith("Buffy, Season", StringComparison.OrdinalIgnoreCase))
                {
                    searchTitles.Add(query.Series.Replace("Buffy, Season", "Buffy the Vampire Slayer Season", StringComparison.OrdinalIgnoreCase));
                }
                else if (query.Series.Contains(':'))
                {
                    var arcPart = query.Series.Split(':')[1].Trim();
                    if (!string.IsNullOrWhiteSpace(arcPart)) searchTitles.Add(arcPart);
                }

                var volumes = new List<SeriesSearchResult>();
                foreach (var title in searchTitles)
                {
                    var res = (await _provider.SearchSeriesAsync(title, apiKey, ct)).ToList();
                    if (res.Count > 0)
                    {
                        volumes.AddRange(res);
                        break;
                    }
                }

                var matchingVolume = volumes
                    .Where(v => v.StartYear.HasValue && v.StartYear.Value <= query.Year.Value)
                    .OrderByDescending(v => v.StartYear ?? 0)
                    .FirstOrDefault()
                    ?? volumes.FirstOrDefault(v => v.StartYear.HasValue && Math.Abs(v.StartYear.Value - query.Year.Value) <= 1)
                    ?? volumes.FirstOrDefault();
                
                if (matchingVolume != null)
                {
                    var volumeIssues = (await _provider.FetchSeriesIssuesAsync(matchingVolume.VolumeId, apiKey, 1, 50, query, ct)).ToList();
                    var seenIds = new HashSet<string>(globalResults.Select(r => r.IssueId));
                    foreach (var vIssue in volumeIssues)
                    {
                        vIssue.VolumeStartYear = matchingVolume.StartYear;
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
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable. Acquire a free API key at https://comicvine.gamespot.com/api/");
        }

        return await _provider.FetchComicMetadataAsync(issueId, apiKey, ct);
    }

    public bool SupportsSeriesSearch => _provider.SupportsSeriesSearch;

    public async Task<IEnumerable<SeriesSearchResult>> SearchSeriesAsync(string seriesTitle, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable. Acquire a free API key at https://comicvine.gamespot.com/api/");
        }

        return await _provider.SearchSeriesAsync(seriesTitle, apiKey, ct);
    }

    public async Task<IEnumerable<ComicSearchResult>> FetchSeriesIssuesAsync(string volumeId, int page = 1, int pageSize = 50, ComicSearchQuery? query = null, CancellationToken ct = default)
    {
        string apiKey = _settingsService.GetEffectiveComicVineApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ComicVine API key is not configured. Please set your API key in Settings or COMICVINE_API_KEY environment variable. Acquire a free API key at https://comicvine.gamespot.com/api/");
        }

        return await _provider.FetchSeriesIssuesAsync(volumeId, apiKey, page, pageSize, query, ct);
    }

    public async Task<ScrapeResult> AutoScrapeComicAsync(
        ComicInfo existingComic,
        ulong? localCoverHash = null,
        string? filePath = null,
        bool enableIntroPageFallback = true,
        int? targetCoverPageIndex = null,
        CancellationToken ct = default)
    {
        var editor = new MetadataEditor();
        if (targetCoverPageIndex.HasValue && !string.IsNullOrEmpty(filePath))
        {
            localCoverHash = editor.GetCoverHash(filePath, targetCoverPageIndex.Value);
        }
        else if (!localCoverHash.HasValue && !string.IsNullOrEmpty(filePath))
        {
            localCoverHash = editor.GetCoverHash(filePath, 0);
        }

        var query = ExtractQueryFromComicInfo(existingComic, filePath);
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

        bool detectedIntroPage = false;
        int trueCoverPageIndex = targetCoverPageIndex ?? 0;

        // If local cover hash is available and auto-visual match is enabled, evaluate candidate cover hashes
        if (localCoverHash.HasValue && localCoverHash.Value != 0 && _settingsService.Settings.AutoApplyOnVisualMatch)
        {
            var tasks = candidates.Take(10).Select(async candidate =>
            {
                string coverUrl = !string.IsNullOrEmpty(candidate.SmallCoverUrl) ? candidate.SmallCoverUrl : candidate.CoverUrl;
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    try
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
                        byte[] bytes = await InkTag.Core.Net.SharedHttpClient.Instance.GetByteArrayAsync(coverUrl, timeoutCts.Token);
                        ulong onlineHash = InkTag.Core.Images.PerceptualHashService.ComputeDHash(bytes);
                        candidate.CoverHash = onlineHash;
                        candidate.VisualSimilarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(localCoverHash.Value, onlineHash);
                        candidate.MatchConfidence = ComicVineProvider.CalculateConfidence(candidate, query, localCoverHash);
                    }
                    catch
                    {
                        // Ignore individual thumbnail download errors
                    }
                }
            });

            await Task.WhenAll(tasks);

            // Re-order candidates primarily by overall MatchConfidence, then by visual match
            candidates = candidates
                .OrderByDescending(c => c.MatchConfidence)
                .ThenByDescending(c => c.VisualSimilarity ?? 0.0)
                .ToList();

            var initialTop = candidates.First();
            double threshold = _settingsService.Settings.AutoMatchConfidenceThreshold;

            // Smart Intro Page Fallback: If Page 0 visual match is weak (< 70%) or below threshold, test Page 1
            if (enableIntroPageFallback && !targetCoverPageIndex.HasValue && !string.IsNullOrEmpty(filePath) &&
                ((initialTop.VisualSimilarity ?? 0.0) < 0.70 || initialTop.MatchConfidence < threshold))
            {
                ulong page1Hash = editor.GetCoverHash(filePath, pageIndex: 1);
                if (page1Hash != 0)
                {
                    var page1VisualScores = new Dictionary<string, (double Sim, double Conf)>();
                    foreach (var c in candidates.Where(c => c.CoverHash.HasValue && c.CoverHash.Value != 0))
                    {
                        double sim = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(page1Hash, c.CoverHash!.Value);
                        double conf = ComicVineProvider.CalculateConfidence(c, query, page1Hash);
                        page1VisualScores[c.IssueId] = (sim, conf);
                    }

                    if (page1VisualScores.Count > 0)
                    {
                        var bestPage1 = page1VisualScores.OrderByDescending(kv => kv.Value.Sim).First();
                        double topP0Sim = initialTop.VisualSimilarity ?? 0.0;

                        if (bestPage1.Value.Sim >= 0.85 || (bestPage1.Value.Sim >= 0.70 && bestPage1.Value.Sim > topP0Sim + 0.30))
                        {
                            detectedIntroPage = true;
                            trueCoverPageIndex = 1;

                            foreach (var c in candidates)
                            {
                                if (page1VisualScores.TryGetValue(c.IssueId, out var scores))
                                {
                                    c.VisualSimilarity = scores.Sim;
                                    c.MatchConfidence = scores.Conf;
                                }
                            }

                            candidates = candidates
                                .OrderByDescending(c => c.MatchConfidence)
                                .ThenByDescending(c => c.VisualSimilarity ?? 0.0)
                                .ToList();
                        }
                    }
                }
            }
        }

        var topMatch = candidates.First();
        double autoThreshold = _settingsService.Settings.AutoMatchConfidenceThreshold;

        if (topMatch.MatchConfidence >= autoThreshold)
        {
            var fetchedMetadata = await FetchMetadataAsync(topMatch.IssueId, ct);
            ApplyMetadata(existingComic, fetchedMetadata, _settingsService.Settings.DefaultMergeMode);

            string introNote = detectedIntroPage ? " [Intro Page Detected; Matched on Page 2 Cover]" : "";
            string visualNote = topMatch.VisualSimilarity.HasValue && topMatch.VisualSimilarity.Value >= 0.70 
                ? $" [Cover Match: {(int)Math.Round(topMatch.VisualSimilarity.Value * 100)}%]" 
                : "";

            return new ScrapeResult
            {
                Success = true,
                Message = $"Successfully scraped metadata from '{topMatch.SeriesTitle} #{topMatch.IssueNumber}'{introNote}{visualNote} (Confidence: {(int)Math.Round(topMatch.MatchConfidence * 100)}%).",
                TargetComic = existingComic,
                SelectedCandidate = topMatch,
                Candidates = candidates,
                DetectedIntroPage = detectedIntroPage,
                TrueCoverPageIndex = trueCoverPageIndex
            };
        }

        return new ScrapeResult
        {
            Success = false,
            Message = $"Low confidence match ({(int)Math.Round(topMatch.MatchConfidence * 100)}% < threshold {(int)Math.Round(autoThreshold * 100)}%). Manual candidate selection required.",
            TargetComic = existingComic,
            Candidates = candidates,
            RequiredUserSelection = true,
            DetectedIntroPage = detectedIntroPage,
            TrueCoverPageIndex = trueCoverPageIndex
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

            if (prop.Name == nameof(ComicInfo.Notes))
            {
                if (_settingsService.Settings.WriteTaggingAttributionToNotes && !string.IsNullOrWhiteSpace(fetched.Notes))
                {
                    prop.SetValue(target, MergeNotes(target.Notes, fetched.Notes));
                }
                else if (mode == ScrapeMergeMode.OverwriteAll)
                {
                    prop.SetValue(target, fetchedVal);
                }
                continue;
            }

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

    public static string MergeNotes(string? existingNotes, string newAttributionNote)
    {
        if (string.IsNullOrWhiteSpace(existingNotes))
        {
            return newAttributionNote;
        }

        var lines = existingNotes.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        bool replaced = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("Tagged with ", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = newAttributionNote;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            lines.Add(newAttributionNote);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static ComicSearchQuery ExtractQueryFromComicInfo(ComicInfo comic, string? filePath = null)
    {
        string series = comic.Series ?? string.Empty;
        string issue = comic.Number ?? string.Empty;
        int? year = comic.Year;

        // If Series, Issue, or Year is missing or trivial/abbreviation and filePath is provided, infer from filename and parent directory hierarchy
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var parsed = InkTag.Core.Parsing.ComicFilenameParser.Parse(filePath, inspectParentHierarchy: true);
            if ((string.IsNullOrWhiteSpace(series) || InkTag.Core.Parsing.ComicFilenameParser.IsTrivialOrAbbreviatedSeriesName(series, parsed.Series)) && !string.IsNullOrWhiteSpace(parsed.Series))
            {
                series = parsed.Series;
            }
            if (string.IsNullOrWhiteSpace(issue) && !string.IsNullOrWhiteSpace(parsed.IssueNumber))
            {
                issue = parsed.IssueNumber;
            }
            if (!year.HasValue && parsed.Year.HasValue)
            {
                year = parsed.Year;
            }
        }

        return new ComicSearchQuery
        {
            Series = series,
            IssueNumber = issue,
            Year = year
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

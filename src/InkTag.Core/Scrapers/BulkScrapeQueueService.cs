using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Configuration;
using InkTag.Core.Images;
using InkTag.Core.Logging;
using InkTag.Core.Parsing;

namespace InkTag.Core.Scrapers;

public enum BulkScrapeItemStatus
{
    Queued,
    ExtractingCover,
    SearchingComicVine,
    ComparingVisuals,
    Matched,
    LowConfidence,
    Unmatched,
    Error,
    Saved
}

public class BulkScrapeQueueItem
{
    public string FilePath { get; set; } = string.Empty;
    public string Filename => Path.GetFileName(FilePath);
    public ComicInfo ExistingComic { get; set; } = new();
    public ComicSearchQuery ParsedQuery { get; set; } = new();
    public byte[]? LocalCoverBytes { get; set; }
    public ulong LocalCoverHash { get; set; }
    
    public BulkScrapeItemStatus Status { get; set; } = BulkScrapeItemStatus.Queued;
    public string StatusMessage { get; set; } = "Queued";
    public string? ErrorMessage { get; set; }
    
    public ComicSearchResult? MatchedCandidate { get; set; }
    public List<ComicSearchResult> Candidates { get; set; } = new();
    public ComicInfo? FetchedMetadata { get; set; }
    public bool IsSelected { get; set; } = true;

    public double VisualSimilarity => MatchedCandidate?.VisualSimilarity ?? 0.0;
    public double MatchConfidence => MatchedCandidate?.MatchConfidence ?? 0.0;
}

public class BulkScrapeOptions
{
    public ScrapeMergeMode MergeMode { get; set; } = ScrapeMergeMode.FillMissingOnly;
    public double ConfidenceThreshold { get; set; } = 0.70;
    public double VisualSimilarityThreshold { get; set; } = 0.85;
    public bool EnableSmartSeriesGrouping { get; set; } = true;
    public bool AutoFetchFullMetadataOnMatch { get; set; } = true;
}

public class BulkScrapeProgressReport
{
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int MatchedItems { get; set; }
    public int LowConfidenceItems { get; set; }
    public int UnmatchedItems { get; set; }
    public int FailedItems { get; set; }
    public BulkScrapeQueueItem? CurrentItem { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public double PercentComplete => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100.0 : 0;
}

public class BulkScrapeSummaryReport
{
    public int Total { get; set; }
    public int Matched { get; set; }
    public int LowConfidence { get; set; }
    public int Unmatched { get; set; }
    public int Failed { get; set; }
    public int Saved { get; set; }
    public List<BulkScrapeQueueItem> Items { get; set; } = new();
}

public class BulkScrapeQueueService
{
    private readonly MetadataScraperService _scraperService;
    private readonly MetadataEditor _metadataEditor;
    private readonly AppSettingsService _settingsService;
    private readonly HttpClient _thumbnailHttpClient;

    public BulkScrapeQueueService(
        MetadataScraperService? scraperService = null,
        MetadataEditor? metadataEditor = null,
        AppSettingsService? settingsService = null,
        HttpClient? httpClient = null)
    {
        _settingsService = settingsService ?? new AppSettingsService();
        _scraperService = scraperService ?? new MetadataScraperService(_settingsService);
        _metadataEditor = metadataEditor ?? new MetadataEditor();
        _thumbnailHttpClient = httpClient ?? new HttpClient();
        _thumbnailHttpClient.Timeout = TimeSpan.FromSeconds(6);
        if (!_thumbnailHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _thumbnailHttpClient.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (BulkScraperQueue)");
        }
    }

    /// <summary>
    /// Builds a queue of items from a list of file paths or an entire directory.
    /// </summary>
    public List<BulkScrapeQueueItem> CreateQueue(IEnumerable<string> filePaths)
    {
        var items = new List<BulkScrapeQueueItem>();
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".cbz" && ext != ".cbr") continue;

            ComicInfo existingInfo;
            try
            {
                existingInfo = _metadataEditor.ReadMetadata(path);
            }
            catch
            {
                existingInfo = new ComicInfo();
            }

            var query = MetadataScraperService.ExtractQueryFromComicInfo(existingInfo, path);

            items.Add(new BulkScrapeQueueItem
            {
                FilePath = path,
                ExistingComic = existingInfo,
                ParsedQuery = query,
                Status = BulkScrapeItemStatus.Queued,
                StatusMessage = "Ready"
            });
        }
        return items;
    }

    /// <summary>
    /// Processes all items in the queue using Smart Series Grouping and Perceptual Cover Visual Hashing.
    /// </summary>
    public async Task<BulkScrapeSummaryReport> ProcessQueueAsync(
        IList<BulkScrapeQueueItem> queue,
        BulkScrapeOptions? options = null,
        IProgress<BulkScrapeProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new BulkScrapeOptions
        {
            MergeMode = _settingsService.Settings.DefaultMergeMode,
            ConfidenceThreshold = _settingsService.Settings.AutoMatchConfidenceThreshold
        };

        var report = new BulkScrapeSummaryReport
        {
            Total = queue.Count,
            Items = queue.ToList()
        };

        if (queue.Count == 0)
        {
            return report;
        }

        // Phase 1: Extract covers and compute local dHash for all items
        for (int i = 0; i < queue.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = queue[i];
            item.Status = BulkScrapeItemStatus.ExtractingCover;
            item.StatusMessage = "Extracting cover...";
            
            ReportProgress(progress, queue, i, item, $"Extracting cover for {item.Filename}...");

            try
            {
                item.LocalCoverBytes = _metadataEditor.ExtractCoverImageBytes(item.FilePath);
                if (item.LocalCoverBytes != null && item.LocalCoverBytes.Length > 0)
                {
                    item.LocalCoverHash = PerceptualHashService.ComputeDHash(item.LocalCoverBytes);
                }
            }
            catch (Exception ex)
            {
                item.ErrorMessage = $"Cover extraction error: {ex.Message}";
            }
        }

        // Phase 2: Group by Series if Smart Series Grouping is enabled
        var processedSet = new HashSet<BulkScrapeQueueItem>();

        if (options.EnableSmartSeriesGrouping && _scraperService.SupportsSeriesSearch)
        {
            // Cluster by parsed series title (case-insensitive)
            var seriesGroups = queue
                .Where(item => !string.IsNullOrWhiteSpace(item.ParsedQuery.Series))
                .GroupBy(item => item.ParsedQuery.Series.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2) // Runs of 2 or more issues
                .ToList();

            foreach (var group in seriesGroups)
            {
                ct.ThrowIfCancellationRequested();
                string seriesName = group.Key;
                int? sampleYear = group.FirstOrDefault(g => g.ParsedQuery.Year.HasValue)?.ParsedQuery.Year;

                ReportProgress(progress, queue, processedSet.Count, null, $"Querying ComicVine series volume for '{seriesName}'...");

                try
                {
                    var seriesResults = (await _scraperService.SearchSeriesAsync(seriesName, ct)).ToList();
                    SeriesSearchResult? matchingVolume = null;

                    if (sampleYear.HasValue)
                    {
                        matchingVolume = seriesResults.FirstOrDefault(v => v.StartYear.HasValue && Math.Abs(v.StartYear.Value - sampleYear.Value) <= 1)
                                      ?? seriesResults.FirstOrDefault();
                    }
                    else
                    {
                        matchingVolume = seriesResults.FirstOrDefault();
                    }

                    if (matchingVolume != null)
                    {
                        // Fetch all issues for this volume
                        var volumeIssues = (await _scraperService.FetchSeriesIssuesAsync(matchingVolume.VolumeId, 1, 100, null, ct)).ToList();

                        // Fetch / compute online cover hashes for volume issues
                        await PopulateCoverHashesForCandidatesAsync(volumeIssues.Take(50), ct);

                        // Match each item in group against the volume issues
                        foreach (var item in group)
                        {
                            ct.ThrowIfCancellationRequested();
                            item.Status = BulkScrapeItemStatus.ComparingVisuals;
                            item.StatusMessage = $"Comparing with {matchingVolume.SeriesTitle}...";

                            var ranked = RankCandidatesAgainstLocalItem(item, volumeIssues, options);
                            item.Candidates = ranked;

                            if (ranked.Count > 0)
                            {
                                var top = ranked[0];
                                item.MatchedCandidate = top;

                                if (top.VisualSimilarity >= options.VisualSimilarityThreshold || top.MatchConfidence >= options.ConfidenceThreshold)
                                {
                                    item.Status = BulkScrapeItemStatus.Matched;
                                    item.StatusMessage = $"Matched: {top.SeriesTitle} #{top.IssueNumber} (Visual: {top.VisualSimilarity:P0})";
                                }
                                else
                                {
                                    item.Status = BulkScrapeItemStatus.LowConfidence;
                                    item.StatusMessage = $"Review needed (Confidence: {top.MatchConfidence:P0}, Visual: {top.VisualSimilarity:P0})";
                                }
                            }
                            else
                            {
                                item.Status = BulkScrapeItemStatus.Unmatched;
                                item.StatusMessage = "No matching issue in volume";
                            }

                            processedSet.Add(item);
                            ReportProgress(progress, queue, processedSet.Count, item, item.StatusMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Smart series grouping error for '{seriesName}': {ex.Message}");
                    // Items will fall back to Phase 3
                }
            }
        }

        // Phase 3: Process remaining items via individual search
        var remainingItems = queue.Where(item => !processedSet.Contains(item)).ToList();
        for (int i = 0; i < remainingItems.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = remainingItems[i];
            item.Status = BulkScrapeItemStatus.SearchingComicVine;
            item.StatusMessage = $"Searching '{item.ParsedQuery.Series} #{item.ParsedQuery.IssueNumber}'...";

            ReportProgress(progress, queue, processedSet.Count, item, item.StatusMessage);

            try
            {
                var candidates = (await _scraperService.SearchCandidatesAsync(item.ParsedQuery, ct)).ToList();

                if (candidates.Count == 0)
                {
                    item.Status = BulkScrapeItemStatus.Unmatched;
                    item.StatusMessage = "No results found";
                }
                else
                {
                    if (item.LocalCoverHash != 0)
                    {
                        await PopulateCoverHashesForCandidatesAsync(candidates.Take(10), ct);
                    }

                    var ranked = RankCandidatesAgainstLocalItem(item, candidates, options);
                    item.Candidates = ranked;
                    var top = ranked[0];
                    item.MatchedCandidate = top;

                    if (top.VisualSimilarity >= options.VisualSimilarityThreshold || top.MatchConfidence >= options.ConfidenceThreshold)
                    {
                        item.Status = BulkScrapeItemStatus.Matched;
                        item.StatusMessage = $"Matched: {top.SeriesTitle} #{top.IssueNumber} (Visual: {top.VisualSimilarity:P0})";
                    }
                    else
                    {
                        item.Status = BulkScrapeItemStatus.LowConfidence;
                        item.StatusMessage = $"Review needed (Confidence: {top.MatchConfidence:P0}, Visual: {top.VisualSimilarity:P0})";
                    }
                }
            }
            catch (Exception ex)
            {
                item.Status = BulkScrapeItemStatus.Error;
                item.StatusMessage = $"Error: {ex.Message}";
                item.ErrorMessage = ex.ToString();
            }

            processedSet.Add(item);
            ReportProgress(progress, queue, processedSet.Count, item, item.StatusMessage);
        }

        // Populate summary metrics
        report.Matched = queue.Count(x => x.Status == BulkScrapeItemStatus.Matched);
        report.LowConfidence = queue.Count(x => x.Status == BulkScrapeItemStatus.LowConfidence);
        report.Unmatched = queue.Count(x => x.Status == BulkScrapeItemStatus.Unmatched);
        report.Failed = queue.Count(x => x.Status == BulkScrapeItemStatus.Error);

        ReportProgress(progress, queue, queue.Count, null, $"Bulk scrape complete. {report.Matched} matched, {report.LowConfidence} need review.");
        return report;
    }

    /// <summary>
    /// Applies matched metadata to selected comic files and saves back to archives.
    /// </summary>
    public async Task<int> ApplyMatchedMetadataAsync(
        IEnumerable<BulkScrapeQueueItem> items,
        ScrapeMergeMode mergeMode,
        IProgress<BulkScrapeProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var targetList = items.Where(i => i.IsSelected && i.MatchedCandidate != null).ToList();
        int savedCount = 0;

        for (int i = 0; i < targetList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = targetList[i];
            item.StatusMessage = "Saving metadata to archive...";

            try
            {
                var fetched = item.FetchedMetadata ?? await _scraperService.FetchMetadataAsync(item.MatchedCandidate!.IssueId, ct);
                item.FetchedMetadata = fetched;

                _metadataEditor.EditMetadata(item.FilePath, comic =>
                {
                    _scraperService.ApplyMetadata(comic, fetched, mergeMode);
                });

                item.Status = BulkScrapeItemStatus.Saved;
                item.StatusMessage = "Saved successfully";
                savedCount++;
            }
            catch (Exception ex)
            {
                item.Status = BulkScrapeItemStatus.Error;
                item.StatusMessage = $"Save failed: {ex.Message}";
                item.ErrorMessage = ex.ToString();
            }

            if (progress != null)
            {
                progress.Report(new BulkScrapeProgressReport
                {
                    TotalItems = targetList.Count,
                    ProcessedItems = i + 1,
                    MatchedItems = savedCount,
                    CurrentItem = item,
                    StatusMessage = $"Saved {i + 1}/{targetList.Count}: {item.Filename}"
                });
            }
        }

        return savedCount;
    }

    private async Task PopulateCoverHashesForCandidatesAsync(IEnumerable<ComicSearchResult> candidates, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(4, 4);
        var tasks = candidates.Select(async candidate =>
        {
            if (candidate.CoverHash.HasValue && candidate.CoverHash.Value != 0) return;

            string url = !string.IsNullOrEmpty(candidate.SmallCoverUrl) ? candidate.SmallCoverUrl : candidate.CoverUrl;
            if (string.IsNullOrEmpty(url)) return;

            await throttle.WaitAsync(ct);
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                byte[] bytes = await _thumbnailHttpClient.GetByteArrayAsync(url, timeoutCts.Token);
                candidate.CoverHash = PerceptualHashService.ComputeDHash(bytes);
            }
            catch
            {
                // Ignore individual thumbnail fetch errors or timeouts
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static List<ComicSearchResult> RankCandidatesAgainstLocalItem(
        BulkScrapeQueueItem item,
        IEnumerable<ComicSearchResult> candidates,
        BulkScrapeOptions options)
    {
        var list = candidates.ToList();

        foreach (var cand in list)
        {
            if (item.LocalCoverHash != 0 && cand.CoverHash.HasValue && cand.CoverHash.Value != 0)
            {
                cand.VisualSimilarity = PerceptualHashService.CalculateSimilarity(item.LocalCoverHash, cand.CoverHash.Value);
            }
            cand.MatchConfidence = ComicVineProvider.CalculateConfidence(cand, item.ParsedQuery, item.LocalCoverHash != 0 ? item.LocalCoverHash : null);
        }

        // Rank primarily by visual similarity if strong match, otherwise by match confidence
        return list
            .OrderByDescending(c => c.VisualSimilarity ?? 0.0)
            .ThenByDescending(c => c.MatchConfidence)
            .ToList();
    }

    private static void ReportProgress(
        IProgress<BulkScrapeProgressReport>? progress,
        IList<BulkScrapeQueueItem> queue,
        int processedCount,
        BulkScrapeQueueItem? currentItem,
        string message)
    {
        if (progress == null) return;

        progress.Report(new BulkScrapeProgressReport
        {
            TotalItems = queue.Count,
            ProcessedItems = processedCount,
            MatchedItems = queue.Count(x => x.Status == BulkScrapeItemStatus.Matched || x.Status == BulkScrapeItemStatus.Saved),
            LowConfidenceItems = queue.Count(x => x.Status == BulkScrapeItemStatus.LowConfidence),
            UnmatchedItems = queue.Count(x => x.Status == BulkScrapeItemStatus.Unmatched),
            FailedItems = queue.Count(x => x.Status == BulkScrapeItemStatus.Error),
            CurrentItem = currentItem,
            StatusMessage = message
        });
    }
}

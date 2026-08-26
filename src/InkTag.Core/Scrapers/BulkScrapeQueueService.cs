using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InkTag.Core.Configuration;
using InkTag.Core.Images;
using InkTag.Core.Logging;
using InkTag.Core.Parsing;
using InkTag.Core.Renaming;

namespace InkTag.Core.Scrapers;

public enum BulkScrapeItemStatus
{
    Ready,
    Excluded,
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
    public bool DetectedIntroPage { get; set; }
    public int TrueCoverPageIndex { get; set; }
    public byte[]? TrueCoverBytes { get; set; }
    
    public BulkScrapeItemStatus Status { get; set; } = BulkScrapeItemStatus.Ready;
    public string StatusMessage { get; set; } = "Ready";
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
    public bool EnableIntroPageFallback { get; set; } = true;
    public bool StripDetectedIntroPages { get; set; } = false;
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
                Status = BulkScrapeItemStatus.Ready,
                StatusMessage = "Ready"
            });
        }
        return items;
    }

    /// <summary>
    /// Processes all items in the queue using pipelined parallel cover extraction and real-time visual cover matching.
    /// Covers are extracted across parallel background workers, feeding matched candidates into the ComicVine resolution engine concurrently.
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

        int processedCount = 0;
        var volumeCache = new ConcurrentDictionary<string, Task<List<ComicSearchResult>>>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateBounded<BulkScrapeQueueItem>(new BoundedChannelOptions(Math.Max(16, queue.Count))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        // 1. Producer: Parallel Cover Extractor (4-6 workers)
        int extractionConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 6);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = extractionConcurrency,
            CancellationToken = ct
        };

        var producerTask = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(queue, parallelOptions, async (item, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    item.Status = BulkScrapeItemStatus.ExtractingCover;
                    item.StatusMessage = "Extracting cover...";
                    ReportProgress(progress, queue, processedCount, item, $"Extracting cover for {item.Filename}...");

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
                        AppLogger.LogWarning($"Bulk scrape cover extraction failed for '{item.FilePath}': {ex.Message}");
                    }

                    await channel.Writer.WriteAsync(item, token);
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.LogWarning($"Producer extraction error: {ex.Message}");
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        // 2. Consumer: Concurrent Matcher (processes items as soon as their covers are extracted)
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                item.Status = BulkScrapeItemStatus.SearchingComicVine;
                item.StatusMessage = $"Matching '{item.ParsedQuery.Series} #{item.ParsedQuery.IssueNumber}'...";
                ReportProgress(progress, queue, processedCount, item, item.StatusMessage);

                try
                {
                    bool matchedViaVolume = false;

                    // Try smart series volume clustering if enabled
                    if (options.EnableSmartSeriesGrouping && _scraperService.SupportsSeriesSearch && !string.IsNullOrWhiteSpace(item.ParsedQuery.Series))
                    {
                        string seriesKey = item.ParsedQuery.Series.Trim();
                        var volumeIssuesTask = volumeCache.GetOrAdd(seriesKey, s => FetchVolumeIssuesForSeriesAsync(s, item.ParsedQuery.Year, ct));
                        var volumeIssues = await volumeIssuesTask;

                        if (volumeIssues.Count > 0)
                        {
                            var candidatePool = volumeIssues.ToList();
                            if (!string.IsNullOrWhiteSpace(item.ParsedQuery.IssueNumber))
                            {
                                string cleanTargetNum = ComicVineProvider.NormalizeIssueNumber(item.ParsedQuery.IssueNumber);
                                if (!candidatePool.Any(i => ComicVineProvider.NormalizeIssueNumber(i.IssueNumber) == cleanTargetNum))
                                {
                                    try
                                    {
                                        string volumeId = volumeIssues.First().VolumeId;
                                        var specific = (await _scraperService.FetchSeriesIssuesAsync(volumeId, 1, 10, item.ParsedQuery, ct)).ToList();
                                        await PopulateCoverHashesForCandidatesAsync(specific, ct);
                                        candidatePool.AddRange(specific);
                                    }
                                    catch
                                    {
                                        // Fallback to standard volume issues
                                    }
                                }
                            }

                            item.Status = BulkScrapeItemStatus.ComparingVisuals;
                            item.StatusMessage = "Comparing cover with series volume...";

                            // On-demand cover hashing: Ensure candidates matching this specific issue have cover hashes computed
                            if (item.LocalCoverHash != 0 && !string.IsNullOrWhiteSpace(item.ParsedQuery.IssueNumber))
                            {
                                string cleanTargetNum = ComicVineProvider.NormalizeIssueNumber(item.ParsedQuery.IssueNumber);
                                var unhashedMatches = candidatePool
                                    .Where(c => ComicVineProvider.NormalizeIssueNumber(c.IssueNumber) == cleanTargetNum && (!c.CoverHash.HasValue || c.CoverHash.Value == 0))
                                    .Take(5)
                                    .ToList();

                                if (unhashedMatches.Count > 0)
                                {
                                    await PopulateCoverHashesForCandidatesAsync(unhashedMatches, ct);
                                }
                            }

                            var ranked = RankCandidatesAgainstLocalItem(item, candidatePool, options);
                            if (ranked.Count > 0)
                            {
                                var top = ranked[0];

                                if (options.EnableIntroPageFallback && (top.VisualSimilarity < options.VisualSimilarityThreshold || top.MatchConfidence < options.ConfidenceThreshold))
                                {
                                    var page1Bytes = _metadataEditor.ExtractCoverImageBytes(item.FilePath, pageIndex: 1);
                                    if (page1Bytes != null && page1Bytes.Length > 0)
                                    {
                                        ulong page1Hash = PerceptualHashService.ComputeDHash(page1Bytes);
                                        if (page1Hash != 0)
                                        {
                                            var p1Ranked = RankCandidatesWithHash(item, candidatePool, page1Hash, options);
                                            if (p1Ranked.Count > 0)
                                            {
                                                var topP1 = p1Ranked[0];
                                                double p0Sim = top.VisualSimilarity ?? 0.0;
                                                if (topP1.VisualSimilarity >= options.VisualSimilarityThreshold || (topP1.VisualSimilarity >= 0.70 && topP1.VisualSimilarity > p0Sim + 0.30))
                                                {
                                                    item.DetectedIntroPage = true;
                                                    item.TrueCoverPageIndex = 1;
                                                    item.TrueCoverBytes = page1Bytes;
                                                    item.LocalCoverBytes = page1Bytes;
                                                    item.LocalCoverHash = page1Hash;
                                                    ranked = p1Ranked;
                                                    top = topP1;
                                                }
                                            }
                                        }
                                    }
                                }

                                item.Candidates = ranked;
                                item.MatchedCandidate = top;

                                string introLabel = item.DetectedIntroPage ? " (Page 2 Cover)" : "";
                                if (top.VisualSimilarity >= options.VisualSimilarityThreshold || top.MatchConfidence >= options.ConfidenceThreshold)
                                {
                                    item.Status = BulkScrapeItemStatus.Matched;
                                    item.StatusMessage = $"Matched{introLabel}: {top.SeriesTitle} #{top.IssueNumber} (Visual: {(int)Math.Round((top.VisualSimilarity ?? 0) * 100)}%)";
                                    item.IsSelected = true;
                                    matchedViaVolume = true;
                                }
                                else
                                {
                                    item.Status = BulkScrapeItemStatus.LowConfidence;
                                    item.StatusMessage = $"Review needed{introLabel} (Confidence: {(int)Math.Round(top.MatchConfidence * 100)}%, Visual: {(int)Math.Round((top.VisualSimilarity ?? 0) * 100)}%)";
                                    item.IsSelected = false; // Auto-uncheck review needed items
                                }
                            }
                        }
                    }

                    // Fallback to individual candidate search if volume matching did not yield a strong match
                    if (!matchedViaVolume)
                    {
                        var candidates = (await _scraperService.SearchCandidatesAsync(item.ParsedQuery, ct)).ToList();
                        if (candidates.Count == 0)
                        {
                            item.Status = BulkScrapeItemStatus.Unmatched;
                            item.StatusMessage = "No results found";
                            item.IsSelected = false;
                        }
                        else
                        {
                            if (item.LocalCoverHash != 0)
                            {
                                await PopulateCoverHashesForCandidatesAsync(candidates.Take(10), ct);
                            }

                            var ranked = RankCandidatesAgainstLocalItem(item, candidates, options);
                            if (ranked.Count > 0)
                            {
                                var top = ranked[0];

                                if (options.EnableIntroPageFallback && (top.VisualSimilarity < options.VisualSimilarityThreshold || top.MatchConfidence < options.ConfidenceThreshold))
                                {
                                    var page1Bytes = _metadataEditor.ExtractCoverImageBytes(item.FilePath, pageIndex: 1);
                                    if (page1Bytes != null && page1Bytes.Length > 0)
                                    {
                                        ulong page1Hash = PerceptualHashService.ComputeDHash(page1Bytes);
                                        if (page1Hash != 0)
                                        {
                                            var p1Ranked = RankCandidatesWithHash(item, candidates, page1Hash, options);
                                            if (p1Ranked.Count > 0)
                                            {
                                                var topP1 = p1Ranked[0];
                                                double p0Sim = top.VisualSimilarity ?? 0.0;
                                                if (topP1.VisualSimilarity >= options.VisualSimilarityThreshold || (topP1.VisualSimilarity >= 0.70 && topP1.VisualSimilarity > p0Sim + 0.30))
                                                {
                                                    item.DetectedIntroPage = true;
                                                    item.TrueCoverPageIndex = 1;
                                                    item.TrueCoverBytes = page1Bytes;
                                                    item.LocalCoverBytes = page1Bytes;
                                                    item.LocalCoverHash = page1Hash;
                                                    ranked = p1Ranked;
                                                    top = topP1;
                                                }
                                            }
                                        }
                                    }
                                }

                                item.Candidates = ranked;
                                item.MatchedCandidate = top;

                                string introLabel = item.DetectedIntroPage ? " (Page 2 Cover)" : "";
                                if (top.VisualSimilarity >= options.VisualSimilarityThreshold || top.MatchConfidence >= options.ConfidenceThreshold)
                                {
                                    item.Status = BulkScrapeItemStatus.Matched;
                                    item.StatusMessage = $"Matched{introLabel}: {top.SeriesTitle} #{top.IssueNumber} (Visual: {(int)Math.Round((top.VisualSimilarity ?? 0) * 100)}%)";
                                    item.IsSelected = true;
                                }
                                else
                                {
                                    item.Status = BulkScrapeItemStatus.LowConfidence;
                                    item.StatusMessage = $"Review needed{introLabel} (Confidence: {(int)Math.Round(top.MatchConfidence * 100)}%, Visual: {(int)Math.Round((top.VisualSimilarity ?? 0) * 100)}%)";
                                    item.IsSelected = false; // Auto-uncheck review needed items
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    item.Status = BulkScrapeItemStatus.Error;
                    item.StatusMessage = $"Error: {ex.Message}";
                    item.ErrorMessage = ex.ToString();
                    item.IsSelected = false;
                    AppLogger.LogWarning($"Bulk scrape item error for '{item.FilePath}': {ex.Message}");
                }

                Interlocked.Increment(ref processedCount);
                ReportProgress(progress, queue, processedCount, item, item.StatusMessage);
            }
        }, ct);

        await Task.WhenAll(producerTask, consumerTask);

        // Flush scraper cache to disk
        _scraperService.FlushCache();

        // Populate summary metrics
        report.Matched = queue.Count(x => x.Status == BulkScrapeItemStatus.Matched);
        report.LowConfidence = queue.Count(x => x.Status == BulkScrapeItemStatus.LowConfidence);
        report.Unmatched = queue.Count(x => x.Status == BulkScrapeItemStatus.Unmatched);
        report.Failed = queue.Count(x => x.Status == BulkScrapeItemStatus.Error);

        ReportProgress(progress, queue, queue.Count, null, $"Bulk scrape complete. {report.Matched} matched, {report.LowConfidence} need review.");
        return report;
    }

    private async Task<List<ComicSearchResult>> FetchVolumeIssuesForSeriesAsync(string seriesName, int? sampleYear, CancellationToken ct)
    {
        try
        {
            var seriesResults = (await _scraperService.SearchSeriesAsync(seriesName, ct)).ToList();
            SeriesSearchResult? matchingVolume = null;

            if (sampleYear.HasValue)
            {
                matchingVolume = seriesResults
                    .Where(v => v.StartYear.HasValue && v.StartYear.Value <= sampleYear.Value)
                    .OrderByDescending(v => v.StartYear ?? 0)
                    .FirstOrDefault()
                    ?? seriesResults.FirstOrDefault(v => v.StartYear.HasValue && Math.Abs(v.StartYear.Value - sampleYear.Value) <= 1)
                    ?? seriesResults.FirstOrDefault();
            }
            else
            {
                matchingVolume = seriesResults.FirstOrDefault();
            }

            if (matchingVolume != null)
            {
                var volumeIssues = (await _scraperService.FetchSeriesIssuesAsync(matchingVolume.VolumeId, 1, 100, null, ct)).ToList();
                foreach (var vIssue in volumeIssues)
                {
                    vIssue.VolumeStartYear = matchingVolume.StartYear;
                }
                await PopulateCoverHashesForCandidatesAsync(volumeIssues.Take(50), ct);
                return volumeIssues;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Error fetching volume issues for series '{seriesName}': {ex.Message}");
        }

        return new List<ComicSearchResult>();
    }

    /// <summary>
    /// Applies matched metadata to selected comic files and saves back to archives, optionally auto-renaming files and stripping detected intro pages.
    /// </summary>
    public async Task<int> ApplyMatchedMetadataAsync(
        IEnumerable<BulkScrapeQueueItem> items,
        ScrapeMergeMode mergeMode,
        bool renameFiles = false,
        string renameTemplate = ComicFileRenamer.DefaultTemplate,
        bool stripDetectedIntroPages = false,
        IProgress<BulkScrapeProgressReport>? progress = null,
        CancellationToken ct = default,
        string? batchJobId = null)
    {
        var targetList = items.Where(i => i.IsSelected && i.MatchedCandidate != null).ToList();
        int savedCount = 0;
        string batchId = batchJobId ?? ("batch_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);

        for (int i = 0; i < targetList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = targetList[i];
            item.StatusMessage = "Saving metadata to archive...";

            try
            {
                if (stripDetectedIntroPages && item.DetectedIntroPage)
                {
                    try
                    {
                        item.StatusMessage = "Stripping provider intro page...";
                        var stripRes = _metadataEditor.StripFirstPage(item.FilePath);
                        if (stripRes.Success)
                        {
                            item.FilePath = stripRes.FilePath;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"Failed to strip intro page from '{item.FilePath}': {ex.Message}");
                    }
                }

                var fetched = item.FetchedMetadata ?? await _scraperService.FetchMetadataAsync(item.MatchedCandidate!.IssueId, ct);
                item.FetchedMetadata = fetched;

                _metadataEditor.EditMetadata(
                    item.FilePath,
                    comic =>
                    {
                        _scraperService.ApplyMetadata(comic, fetched, mergeMode);
                    },
                    batchJobId: batchId,
                    changeReason: $"Bulk Auto-Tag ComicVine ({item.MatchedCandidate?.SeriesTitle} #{item.MatchedCandidate?.IssueNumber})",
                    coverDHash: item.LocalCoverHash != 0 ? item.LocalCoverHash.ToString("X16") : null,
                    matchedThumbnailUrl: !string.IsNullOrEmpty(item.MatchedCandidate?.SmallCoverUrl) ? item.MatchedCandidate.SmallCoverUrl : item.MatchedCandidate?.CoverUrl,
                    matchConfidence: item.MatchedCandidate?.MatchConfidence,
                    visualSimilarity: item.MatchedCandidate?.VisualSimilarity);

                // Update item path if a CBR was converted to CBZ during repackaging
                if (Path.GetExtension(item.FilePath).Equals(".cbr", StringComparison.OrdinalIgnoreCase))
                {
                    string targetCbz = Path.ChangeExtension(item.FilePath, ".cbz");
                    if (File.Exists(targetCbz))
                    {
                        item.FilePath = targetCbz;
                    }
                }

                if (renameFiles)
                {
                    try
                    {
                        var updatedComic = _metadataEditor.ReadMetadata(item.FilePath);
                        string newFilename = ComicFileRenamer.GenerateFilename(updatedComic, item.FilePath, renameTemplate, preserveScanInfo: false);
                        if (!string.Equals(item.Filename, newFilename, StringComparison.Ordinal))
                        {
                            string newPath = ComicFileRenamer.RenameFile(item.FilePath, newFilename, overwrite: false);
                            item.FilePath = newPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"Auto-rename skipped for '{item.FilePath}': {ex.Message}");
                    }
                }

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
        return RankCandidatesWithHash(item, candidates, item.LocalCoverHash, options);
    }

    private static List<ComicSearchResult> RankCandidatesWithHash(
        BulkScrapeQueueItem item,
        IEnumerable<ComicSearchResult> candidates,
        ulong coverHash,
        BulkScrapeOptions options)
    {
        var list = candidates.ToList();

        foreach (var cand in list)
        {
            if (coverHash != 0 && cand.CoverHash.HasValue && cand.CoverHash.Value != 0)
            {
                cand.VisualSimilarity = PerceptualHashService.CalculateSimilarity(coverHash, cand.CoverHash.Value);
            }
            cand.MatchConfidence = ComicVineProvider.CalculateConfidence(cand, item.ParsedQuery, coverHash != 0 ? coverHash : null);
        }

        // Rank primarily by combined MatchConfidence, then by visual similarity
        return list
            .OrderByDescending(c => c.MatchConfidence)
            .ThenByDescending(c => c.VisualSimilarity ?? 0.0)
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

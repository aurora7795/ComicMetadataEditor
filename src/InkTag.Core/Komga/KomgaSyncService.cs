using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Configuration;
using InkTag.Core.Logging;

namespace InkTag.Core.Komga;

public class KomgaSyncReport
{
    public int BooksAnalyzed { get; set; }
    public int SeriesAnalyzed { get; set; }
    public int CollectionsSynced { get; set; }
    public List<string> SuccessMessages { get; set; } = new();
    public List<(string Target, string Error)> Failures { get; set; } = new();

    public bool IsSuccess => Failures.Count == 0;
}

public class KomgaSyncService
{
    private readonly KomgaClient _client;
    private readonly AppSettingsService _settingsService;

    public KomgaSyncService(AppSettingsService? settingsService = null, KomgaClient? client = null)
    {
        _settingsService = settingsService ?? new AppSettingsService();
        _client = client ?? new KomgaClient(_settingsService);
    }

    public bool IsConfigured => _client.IsConfigured;

    public async Task<KomgaSyncReport> SyncComicFileAsync(
        string filePath,
        ComicInfo comicInfo,
        CancellationToken ct = default)
    {
        var report = new KomgaSyncReport();
        if (!IsConfigured || string.IsNullOrWhiteSpace(filePath))
        {
            return report;
        }

        try
        {
            var mappings = _settingsService.Settings.KomgaPathMappings;
            var book = await _client.FindBookByFilePathAsync(filePath, mappings, ct);

            if (book != null)
            {
                bool analyzed = await _client.AnalyzeBookAsync(book.Id, ct);
                if (analyzed)
                {
                    report.BooksAnalyzed++;
                    report.SuccessMessages.Add($"Refreshed book '{book.Name}' (ID: {book.Id}) on Komga.");
                }

                // StoryArc collection sync
                if (_settingsService.Settings.KomgaSyncStoryArcsToCollections &&
                    !string.IsNullOrWhiteSpace(comicInfo.StoryArc) &&
                    !string.IsNullOrWhiteSpace(book.SeriesId))
                {
                    foreach (var arc in comicInfo.StoryArc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        bool arcSynced = await _client.SyncStoryArcCollectionAsync(book.SeriesId, arc, ct);
                        if (arcSynced)
                        {
                            report.CollectionsSynced++;
                            report.SuccessMessages.Add($"Added series to collection '{arc}'.");
                        }
                    }
                }
            }
            else
            {
                // Fallback: search by series folder and trigger series analysis
                string? seriesDir = Path.GetDirectoryName(filePath);
                string seriesName = !string.IsNullOrWhiteSpace(comicInfo.Series) 
                    ? comicInfo.Series 
                    : Parsing.ComicFilenameParser.Parse(filePath, inspectParentHierarchy: true).Series;

                if (string.IsNullOrWhiteSpace(seriesName))
                {
                    seriesName = Path.GetFileName(seriesDir ?? string.Empty);
                }

                var series = await _client.FindSeriesByPathOrNameAsync(seriesDir ?? string.Empty, seriesName, mappings, ct);
                if (series != null)
                {
                    bool analyzed = await _client.AnalyzeSeriesAsync(series.Id, ct);
                    if (analyzed)
                    {
                        report.SeriesAnalyzed++;
                        report.SuccessMessages.Add($"Refreshed series '{series.Name}' (ID: {series.Id}) on Komga.");
                    }
                }
                else
                {
                    report.Failures.Add((filePath, $"Book '{Path.GetFileName(filePath)}' or Series '{seriesName}' not found in Komga. Verify your Komga library roots or Path Mappings."));
                }
            }
        }
        catch (Exception ex)
        {
            report.Failures.Add((filePath, ex.Message));
            AppLogger.LogWarning($"[KomgaSyncService] Sync failed for '{filePath}': {ex.Message}");
        }

        return report;
    }

    public async Task<KomgaSyncReport> SyncMultipleComicsAsync(
        IReadOnlyList<(string FilePath, ComicInfo Info)> items,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var totalReport = new KomgaSyncReport();
        if (!IsConfigured || items.Count == 0) return totalReport;

        int processed = 0;
        foreach (var (filePath, info) in items)
        {
            ct.ThrowIfCancellationRequested();
            var itemReport = await SyncComicFileAsync(filePath, info, ct);

            totalReport.BooksAnalyzed += itemReport.BooksAnalyzed;
            totalReport.SeriesAnalyzed += itemReport.SeriesAnalyzed;
            totalReport.CollectionsSynced += itemReport.CollectionsSynced;
            totalReport.SuccessMessages.AddRange(itemReport.SuccessMessages);
            totalReport.Failures.AddRange(itemReport.Failures);

            processed++;
            progress?.Report((double)processed / items.Count);
        }

        return totalReport;
    }
}

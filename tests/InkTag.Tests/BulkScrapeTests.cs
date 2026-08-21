using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Images;
using InkTag.Core.Scrapers;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace InkTag.Tests;

public class BulkScrapeTests
{
    private class MockScraperProvider : IMetadataScraperProvider
    {
        public string ProviderName => "MockProvider";
        public bool RequiresApiKey => false;
        public bool SupportsSeriesSearch => true;

        public int SearchSeriesCalls { get; private set; }
        public int FetchSeriesIssuesCalls { get; private set; }
        public int SearchAsyncCalls { get; private set; }
        public int FetchComicMetadataCalls { get; private set; }

        public List<SeriesSearchResult> SeriesResults { get; set; } = new();
        public List<ComicSearchResult> VolumeIssues { get; set; } = new();
        public List<ComicSearchResult> SearchResults { get; set; } = new();
        public ComicInfo FetchedMetadata { get; set; } = new() { Title = "Mock Fetched Title", Writer = "Mock Writer" };

        public Task<IEnumerable<ComicSearchResult>> SearchAsync(ComicSearchQuery query, string apiKey, CancellationToken ct = default)
        {
            SearchAsyncCalls++;
            return Task.FromResult<IEnumerable<ComicSearchResult>>(SearchResults);
        }

        public Task<ComicInfo> FetchComicMetadataAsync(string issueId, string apiKey, CancellationToken ct = default)
        {
            FetchComicMetadataCalls++;
            return Task.FromResult(FetchedMetadata);
        }

        public Task<IEnumerable<SeriesSearchResult>> SearchSeriesAsync(string seriesTitle, string apiKey, CancellationToken ct = default)
        {
            SearchSeriesCalls++;
            return Task.FromResult<IEnumerable<SeriesSearchResult>>(SeriesResults);
        }

        public Task<IEnumerable<ComicSearchResult>> FetchSeriesIssuesAsync(string volumeId, string apiKey, int page = 1, int pageSize = 50, ComicSearchQuery? query = null, CancellationToken ct = default)
        {
            FetchSeriesIssuesCalls++;
            return Task.FromResult<IEnumerable<ComicSearchResult>>(VolumeIssues);
        }
    }

    private string CreateTestCbzWithCover(string filename, string? title = null, string? series = null, string? number = null)
    {
        string cbzPath = Path.Combine(Path.GetTempPath(), filename);
        string imgPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

        try
        {
            // Create a small valid test JPEG image
            using (var image = new Image<Rgb24>(64, 96))
            {
                image.SaveAsJpeg(imgPath);
            }

            using (var fs = File.OpenWrite(cbzPath))
            using (var writer = new ZipWriter(fs, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("001_cover.jpg", imgPath);

                if (title != null || series != null || number != null)
                {
                    var comicInfo = new ComicInfo { Title = title, Series = series, Number = number };
                    string xmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
                    using (var xmlFs = File.OpenWrite(xmlPath))
                    {
                        new System.Xml.Serialization.XmlSerializer(typeof(ComicInfo)).Serialize(xmlFs, comicInfo);
                    }
                    writer.Write("ComicInfo.xml", xmlPath);
                    File.Delete(xmlPath);
                }
            }

            return cbzPath;
        }
        finally
        {
            if (File.Exists(imgPath)) File.Delete(imgPath);
        }
    }

    [Fact]
    public void CreateQueue_ExtractsComicInfoAndParsesQueryCorrectly()
    {
        string cbz1 = CreateTestCbzWithCover("Saga #01 (2012).cbz");
        string cbz2 = CreateTestCbzWithCover("Paper Girls 002.cbz", series: "Paper Girls", number: "2");

        try
        {
            var service = new BulkScrapeQueueService();
            var queue = service.CreateQueue(new[] { cbz1, cbz2 });

            Assert.Equal(2, queue.Count);

            Assert.Equal("Saga", queue[0].ParsedQuery.Series);
            Assert.Equal("1", queue[0].ParsedQuery.IssueNumber);
            Assert.Equal(2012, queue[0].ParsedQuery.Year);
            Assert.Equal(BulkScrapeItemStatus.Ready, queue[0].Status);

            Assert.Equal("Paper Girls", queue[1].ParsedQuery.Series);
            Assert.Equal("2", queue[1].ParsedQuery.IssueNumber);
        }
        finally
        {
            if (File.Exists(cbz1)) File.Delete(cbz1);
            if (File.Exists(cbz2)) File.Delete(cbz2);
        }
    }

    [Fact]
    public async Task ProcessQueueAsync_WithSmartSeriesGrouping_FetchesVolumeOnceAndMatchesCovers()
    {
        string cbz1 = CreateTestCbzWithCover("Saga #01 (2012).cbz");
        string cbz2 = CreateTestCbzWithCover("Saga #02 (2012).cbz");

        try
        {
            var mockProvider = new MockScraperProvider
            {
                SeriesResults = new List<SeriesSearchResult>
                {
                    new SeriesSearchResult { VolumeId = "4050-48229", SeriesTitle = "Saga", StartYear = 2012 }
                },
                VolumeIssues = new List<ComicSearchResult>
                {
                    new ComicSearchResult { IssueId = "4000-101", SeriesTitle = "Saga", IssueNumber = "01", IssueTitle = "Chapter 1" },
                    new ComicSearchResult { IssueId = "4000-102", SeriesTitle = "Saga", IssueNumber = "02", IssueTitle = "Chapter 2" }
                }
            };

            var settingsService = new AppSettingsService();
            settingsService.Settings.ComicVineApiKey = "mock_key";
            var scraperService = new MetadataScraperService(settingsService, mockProvider);
            var queueService = new BulkScrapeQueueService(scraperService, null, settingsService);

            var queue = queueService.CreateQueue(new[] { cbz1, cbz2 });
            var options = new BulkScrapeOptions
            {
                EnableSmartSeriesGrouping = true,
                ConfidenceThreshold = 0.50
            };

            var report = await queueService.ProcessQueueAsync(queue, options);

            // Series volume should be queried once, not per-item
            Assert.Equal(1, mockProvider.SearchSeriesCalls);
            Assert.Equal(1, mockProvider.FetchSeriesIssuesCalls);
            Assert.Equal(0, mockProvider.SearchAsyncCalls); // Avoided standalone search!

            Assert.Equal(2, report.Total);
            Assert.Equal(2, report.Matched);
            Assert.Equal(BulkScrapeItemStatus.Matched, queue[0].Status);
            Assert.Equal("Chapter 1", queue[0].MatchedCandidate?.IssueTitle);
            Assert.Equal("Chapter 2", queue[1].MatchedCandidate?.IssueTitle);
        }
        finally
        {
            if (File.Exists(cbz1)) File.Delete(cbz1);
            if (File.Exists(cbz2)) File.Delete(cbz2);
        }
    }

    [Fact]
    public async Task ProcessQueueAsync_WithMixedFolder_FallsBackToIndividualSearch()
    {
        string cbz1 = CreateTestCbzWithCover("Batman 001.cbz");
        string cbz2 = CreateTestCbzWithCover("Spider-Man 001.cbz");

        try
        {
            var mockProvider = new MockScraperProvider
            {
                SearchResults = new List<ComicSearchResult>
                {
                    new ComicSearchResult { IssueId = "4000-999", SeriesTitle = "Batman", IssueNumber = "001", MatchConfidence = 0.95 }
                }
            };

            var settingsService = new AppSettingsService();
            settingsService.Settings.ComicVineApiKey = "mock_key";
            var scraperService = new MetadataScraperService(settingsService, mockProvider);
            var queueService = new BulkScrapeQueueService(scraperService, null, settingsService);

            var queue = queueService.CreateQueue(new[] { cbz1, cbz2 });
            var options = new BulkScrapeOptions
            {
                EnableSmartSeriesGrouping = true,
                ConfidenceThreshold = 0.70
            };

            var report = await queueService.ProcessQueueAsync(queue, options);

            // Each unique series triggers individual search
            Assert.True(mockProvider.SearchAsyncCalls >= 1);
            Assert.Equal(2, report.Total);
        }
        finally
        {
            if (File.Exists(cbz1)) File.Delete(cbz1);
            if (File.Exists(cbz2)) File.Delete(cbz2);
        }
    }

    [Fact]
    public async Task ProcessQueueAsync_HonorsCancellationToken()
    {
        string cbz1 = CreateTestCbzWithCover("Saga #01.cbz");
        string cbz2 = CreateTestCbzWithCover("Saga #02.cbz");

        try
        {
            var queueService = new BulkScrapeQueueService();
            var queue = queueService.CreateQueue(new[] { cbz1, cbz2 });

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                queueService.ProcessQueueAsync(queue, null, null, cts.Token));
        }
        finally
        {
            if (File.Exists(cbz1)) File.Delete(cbz1);
            if (File.Exists(cbz2)) File.Delete(cbz2);
        }
    }

    [Fact]
    public async Task ApplyMatchedMetadataAsync_WritesMetadataToCbzArchive()
    {
        string cbz = CreateTestCbzWithCover("Saga #01.cbz", title: "Original Title");

        try
        {
            var mockProvider = new MockScraperProvider
            {
                FetchedMetadata = new ComicInfo
                {
                    Title = "Chapter One",
                    Series = "Saga",
                    Number = "1",
                    Writer = "Brian K. Vaughan",
                    Penciller = "Fiona Staples",
                    Year = 2012
                }
            };

            var settingsService = new AppSettingsService();
            settingsService.Settings.ComicVineApiKey = "mock_key";
            var scraperService = new MetadataScraperService(settingsService, mockProvider);
            var editor = new MetadataEditor();
            var queueService = new BulkScrapeQueueService(scraperService, editor, settingsService);

            var queue = queueService.CreateQueue(new[] { cbz });
            queue[0].MatchedCandidate = new ComicSearchResult { IssueId = "4000-101", SeriesTitle = "Saga", IssueNumber = "1" };
            queue[0].IsSelected = true;

            int saved = await queueService.ApplyMatchedMetadataAsync(queue, ScrapeMergeMode.OverwriteAll);
            Assert.Equal(1, saved);
            Assert.Equal(BulkScrapeItemStatus.Saved, queue[0].Status);

            // Verify disk archive was updated
            var updated = editor.ReadMetadata(cbz);
            Assert.Equal("Chapter One", updated.Title);
            Assert.Equal("Brian K. Vaughan", updated.Writer);
            Assert.Equal("Fiona Staples", updated.Penciller);
            Assert.Equal(2012, updated.Year);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using SixLabors.ImageSharp;
using Xunit;

namespace InkTag.Tests;

public class ScraperTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseContent { get; set; } = "{}";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseContent)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void AppSettingsService_FallbackToEnvironmentVariable()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var service = new AppSettingsService(tempFile);
            Environment.SetEnvironmentVariable("COMICVINE_API_KEY", "test_env_key_12345");

            string key = service.GetEffectiveComicVineApiKey();
            Assert.Equal("test_env_key_12345", key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMICVINE_API_KEY", null);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ScraperCacheService_StoresAndRetrievesData()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var cache = new ScraperCacheService(tempFile);
            cache.Set("test_key", "{\"result\":\"ok\"}");

            string? retrieved = cache.Get("test_key", TimeSpan.FromMinutes(5));
            Assert.Equal("{\"result\":\"ok\"}", retrieved);

            string? expired = cache.Get("test_key", TimeSpan.FromMilliseconds(-1));
            Assert.Null(expired);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComicVineProvider_ParsesSearchResultsAndCalculatesConfidence()
    {
        string json = @"{
            ""status_code"": 1,
            ""results"": [
                {
                    ""id"": 12345,
                    ""name"": ""The Night Gwen Stacy Died"",
                    ""issue_number"": ""121"",
                    ""cover_date"": ""1973-06-01"",
                    ""site_detail_url"": ""https://comicvine.gamespot.com/issue/4000-12345/"",
                    ""volume"": { ""id"": 999, ""name"": ""The Amazing Spider-Man"" },
                    ""image"": { ""medium_url"": ""https://example.com/cover.jpg"", ""small_url"": ""https://example.com/small.jpg"" }
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler { ResponseContent = json };
        var rateClient = new RateLimitedHttpClient(new HttpClient(mockHandler));
        var provider = new ComicVineProvider(rateClient);

        var query = new ComicSearchQuery
        {
            Series = "The Amazing Spider-Man",
            IssueNumber = "121",
            Year = 1973
        };

        var results = (await provider.SearchAsync(query, "valid_key")).ToList();

        Assert.Single(results);
        var first = results.First();
        Assert.Equal("12345", first.IssueId);
        Assert.Equal("The Amazing Spider-Man", first.SeriesTitle);
        Assert.Equal("121", first.IssueNumber);
        Assert.True(first.MatchConfidence >= 0.85, $"Expected confidence >= 0.85, got {first.MatchConfidence}");
    }

    [Fact]
    public async Task ComicVineProvider_ParsesIssueDetailsAndCredits()
    {
        string json = @"{
            ""status_code"": 1,
            ""results"": {
                ""name"": ""The Night Gwen Stacy Died"",
                ""issue_number"": ""121"",
                ""description"": ""<p>A tragic turning point in Marvel history.</p>"",
                ""cover_date"": ""1973-06-01"",
                ""volume"": { ""name"": ""The Amazing Spider-Man"" },
                ""person_credits"": [
                    { ""name"": ""Gerry Conway"", ""role"": ""writer"" },
                    { ""name"": ""Gil Kane"", ""role"": ""penciller, cover artist"" },
                    { ""name"": ""John Romita"", ""role"": ""inker"" }
                ],
                ""character_credits"": [ { ""name"": ""Spider-Man"" }, { ""name"": ""Green Goblin"" } ]
            }
        }";

        var mockHandler = new MockHttpMessageHandler { ResponseContent = json };
        var rateClient = new RateLimitedHttpClient(new HttpClient(mockHandler));
        var provider = new ComicVineProvider(rateClient);

        var comic = await provider.FetchComicMetadataAsync("12345", "valid_key");

        Assert.Equal("The Night Gwen Stacy Died", comic.Title);
        Assert.Equal("121", comic.Number);
        Assert.Equal("The Amazing Spider-Man", comic.Series);
        Assert.Equal("Gerry Conway", comic.Writer);
        Assert.Equal("Gil Kane", comic.Penciller);
        Assert.Equal("John Romita", comic.Inker);
        Assert.Equal("Gil Kane", comic.CoverArtist);
        Assert.Equal("A tragic turning point in Marvel history.", comic.Summary);
        Assert.Contains("Spider-Man", comic.Characters);
    }

    [Fact]
    public void MetadataScraperService_ApplyMetadata_FillMissingOnly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempFile);
            var service = new MetadataScraperService(settingsService);

            var existing = new ComicInfo
            {
                Series = "Original Series",
                Writer = "Existing Writer"
                // Title and Summary are missing (null)
            };

            var fetched = new ComicInfo
            {
                Series = "New Series",
                Writer = "New Writer",
                Title = "Fetched Title",
                Summary = "Fetched Summary"
            };

            service.ApplyMetadata(existing, fetched, ScrapeMergeMode.FillMissingOnly);

            Assert.Equal("Original Series", existing.Series); // Preserved
            Assert.Equal("Existing Writer", existing.Writer); // Preserved
            Assert.Equal("Fetched Title", existing.Title);     // Populated
            Assert.Equal("Fetched Summary", existing.Summary); // Populated
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MetadataScraperService_ApplyMetadata_OverwriteAll()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempFile);
            var service = new MetadataScraperService(settingsService);

            var existing = new ComicInfo
            {
                Series = "Original Series",
                Writer = "Existing Writer"
            };

            var fetched = new ComicInfo
            {
                Series = "New Series",
                Writer = "New Writer"
            };

            service.ApplyMetadata(existing, fetched, ScrapeMergeMode.OverwriteAll);

            Assert.Equal("New Series", existing.Series);
            Assert.Equal("New Writer", existing.Writer);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MetadataScraperService_ApplyMetadata_SelectiveFields()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempFile);
            var service = new MetadataScraperService(settingsService);

            var existing = new ComicInfo
            {
                Series = "Original Series",
                Writer = "Original Writer"
            };

            var fetched = new ComicInfo
            {
                Series = "New Series",
                Writer = "New Writer"
            };

            var allowedFields = new HashSet<string> { nameof(ComicInfo.Writer) };
            service.ApplyMetadata(existing, fetched, ScrapeMergeMode.SelectiveFields, allowedFields);

            Assert.Equal("Original Series", existing.Series); // Not selected -> preserved
            Assert.Equal("New Writer", existing.Writer);       // Selected -> updated
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MetadataScraperService_ExtractQueryFromComicInfo_InfersFromParentDirectoryHierarchy()
    {
        // 1. Untagged comic with attached acronym inside series folder
        var emptyComic = new ComicInfo();
        var query1 = MetadataScraperService.ExtractQueryFromComicInfo(emptyComic, "/Volumes/Comics/Iron Man/IM015.cbz");
        Assert.Equal("Iron Man", query1.Series);
        Assert.Equal("15", query1.IssueNumber);

        // 2. Comic with only issue number in filename inside series folder with year
        var query2 = MetadataScraperService.ExtractQueryFromComicInfo(emptyComic, "/media/comics/Batman (2016)/001.cbz");
        Assert.Equal("Batman", query2.Series);
        Assert.Equal("1", query2.IssueNumber);
        Assert.Equal(2016, query2.Year);

        // 3. Comic with trivial acronym in metadata but full folder name available
        var abbrevComic = new ComicInfo { Series = "IM", Number = "" };
        var query3 = MetadataScraperService.ExtractQueryFromComicInfo(abbrevComic, "/Comics/Iron Man/015.cbz");
        Assert.Equal("Iron Man", query3.Series);
        Assert.Equal("15", query3.IssueNumber);
    }

    [Fact]
    public async Task ComicVineProvider_ParsesSeriesSearchResults()
    {
        string json = @"{
            ""status_code"": 1,
            ""results"": [
                {
                    ""id"": ""4050-18234"",
                    ""name"": ""The Amazing Spider-Man"",
                    ""start_year"": ""1963"",
                    ""count_of_issues"": 700,
                    ""publisher"": { ""name"": ""Marvel"" },
                    ""site_detail_url"": ""https://comicvine.gamespot.com/the-amazing-spider-man/4050-18234/"",
                    ""deck"": ""The flagship Spider-Man comic series."",
                    ""image"": { ""medium_url"": ""https://example.com/spidey.jpg"", ""small_url"": ""https://example.com/spidey_small.jpg"" }
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler { ResponseContent = json };
        var rateClient = new RateLimitedHttpClient(new HttpClient(mockHandler));
        var provider = new ComicVineProvider(rateClient);

        var results = (await provider.SearchSeriesAsync("Amazing Spider-Man", "valid_key")).ToList();

        Assert.Single(results);
        var series = results.First();
        Assert.Equal("4050-18234", series.VolumeId);
        Assert.Equal("The Amazing Spider-Man", series.SeriesTitle);
        Assert.Equal("Marvel", series.Publisher);
        Assert.Equal(1963, series.StartYear);
        Assert.Equal(700, series.CountOfIssues);
        Assert.Equal("The flagship Spider-Man comic series.", series.Description);
    }

    [Fact]
    public async Task ComicVineProvider_ParsesSeriesSearchResults_WithDescriptionFallbackAndAliases()
    {
        string json = @"{
            ""status_code"": 1,
            ""results"": [
                {
                    ""id"": ""4050-1001"",
                    ""name"": ""Blankets"",
                    ""start_year"": ""2003"",
                    ""count_of_issues"": 1,
                    ""publisher"": { ""name"": ""Top Shelf"" },
                    ""deck"": null,
                    ""description"": ""<p>An autobiographical graphic novel by <b>Craig Thompson</b> &amp; published by Top Shelf.</p>"",
                    ""aliases"": ""Blankets US\nBlankets HC"",
                    ""image"": { ""medium_url"": ""https://example.com/cover1.jpg"", ""small_url"": ""https://example.com/small1.jpg"" }
                },
                {
                    ""id"": ""4050-1002"",
                    ""name"": ""Blankets"",
                    ""start_year"": ""2010"",
                    ""count_of_issues"": 1,
                    ""publisher"": { ""name"": ""Rizzoli Lizard"" },
                    ""deck"": ""Italian translation published by Rizzoli."",
                    ""description"": null,
                    ""aliases"": ""Blankets (Italy); Blankets Edizione Italiana"",
                    ""image"": { ""medium_url"": ""https://example.com/cover2.jpg"", ""small_url"": ""https://example.com/small2.jpg"" }
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler { ResponseContent = json };
        var rateClient = new RateLimitedHttpClient(new HttpClient(mockHandler));
        var provider = new ComicVineProvider(rateClient);

        var results = (await provider.SearchSeriesAsync("Blankets", "valid_key")).ToList();

        Assert.Equal(2, results.Count);

        var item1 = results[0];
        Assert.Equal("Top Shelf", item1.Publisher);
        Assert.Equal("An autobiographical graphic novel by Craig Thompson & published by Top Shelf.", item1.Description);
        Assert.Equal("Blankets US, Blankets HC", item1.Aliases);

        var item2 = results[1];
        Assert.Equal("Rizzoli Lizard", item2.Publisher);
        Assert.Equal("Italian translation published by Rizzoli.", item2.Description);
        Assert.Equal("Blankets (Italy), Blankets Edizione Italiana", item2.Aliases);
    }

    [Fact]
    public void SeriesItemViewModel_OnlyProvidesToolTip_WhenDescriptionIsTruncated()
    {
        var shortResult = new SeriesSearchResult
        {
            SeriesTitle = "Short Series",
            Description = "Four issue mini-series. Collected in Eden."
        };
        var shortVm = new InkTag.Gui.ViewModels.SeriesItemViewModel(shortResult);
        Assert.False(shortVm.IsDescriptionTruncated);
        Assert.Null(shortVm.DescriptionToolTip);

        var longResult = new SeriesSearchResult
        {
            SeriesTitle = "Long Series",
            Description = "5 issue digital comic series. When a heist to steal an expensive piece of scientific technology goes wrong, Henry Quan, a selfish career criminal, is unmoored in both space and time. Thrown in and out of parallel lives across the multiverse, he struggles to find his way back home."
        };
        var longVm = new InkTag.Gui.ViewModels.SeriesItemViewModel(longResult);
        Assert.True(longVm.IsDescriptionTruncated);
        Assert.NotNull(longVm.DescriptionToolTip);
        Assert.Equal(longResult.Description, longVm.DescriptionToolTip);
    }

    [Fact]
    public async Task ComicVineProvider_ParsesSeriesIssuesResults()
    {
        string json = @"{
            ""status_code"": 1,
            ""results"": [
                {
                    ""id"": ""4000-12345"",
                    ""name"": ""Spider-Man No More!"",
                    ""issue_number"": ""50"",
                    ""cover_date"": ""1967-07-01"",
                    ""site_detail_url"": ""https://comicvine.gamespot.com/issue/4000-12345/"",
                    ""volume"": { ""id"": 18234, ""name"": ""The Amazing Spider-Man"" },
                    ""image"": { ""medium_url"": ""https://example.com/cover50.jpg"", ""small_url"": ""https://example.com/small50.jpg"" }
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler { ResponseContent = json };
        var rateClient = new RateLimitedHttpClient(new HttpClient(mockHandler));
        var provider = new ComicVineProvider(rateClient);

        var results = (await provider.FetchSeriesIssuesAsync("4050-18234", "valid_key", page: 1, pageSize: 50)).ToList();

        Assert.Single(results);
        var issue = results.First();
        Assert.Equal("4000-12345", issue.IssueId);
        Assert.Equal("50", issue.IssueNumber);
        Assert.Equal("Spider-Man No More!", issue.IssueTitle);
        Assert.Equal("The Amazing Spider-Man", issue.SeriesTitle);
    }

    [Fact]
    public void PerceptualHashService_ComputesIdenticalHashForIdenticalImages()
    {
        // Generate a 100x100 checkerboard pattern image
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(100, 100);
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                byte val = (byte)(((x / 10) % 2 == (y / 10) % 2) ? 240 : 20);
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(val, val, val);
            }
        }

        using var ms1 = new MemoryStream();
        image.SaveAsPng(ms1);
        ms1.Position = 0;

        using var ms2 = new MemoryStream();
        image.SaveAsJpeg(ms2);
        ms2.Position = 0;

        ulong hashPng = InkTag.Core.Images.PerceptualHashService.ComputeDHash(ms1);
        ulong hashJpeg = InkTag.Core.Images.PerceptualHashService.ComputeDHash(ms2);

        Assert.NotEqual(0UL, hashPng);
        Assert.NotEqual(0UL, hashJpeg);

        // PNG vs JPEG of same image should have minimal or 0 Hamming distance
        int distance = InkTag.Core.Images.PerceptualHashService.ComputeHammingDistance(hashPng, hashJpeg);
        double similarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(hashPng, hashJpeg);

        Assert.True(distance <= 4, $"Expected distance <= 4, got {distance}");
        Assert.True(similarity >= 0.90, $"Expected similarity >= 0.90, got {similarity}");
        Assert.True(InkTag.Core.Images.PerceptualHashService.IsVisualMatch(hashPng, hashJpeg));
    }

    [Fact]
    public void ComicVineProvider_AppliesVisualOverrideWhenCoverMatches()
    {
        var result = new ComicSearchResult
        {
            SeriesTitle = "Unorganized Comic",
            IssueNumber = "Unknown",
            CoverHash = 0b1111000011110000UL
        };

        var query = new ComicSearchQuery
        {
            Series = "Unknown Title",
            IssueNumber = "99"
        };

        // Text score alone is 0
        double textOnlyConfidence = ComicVineProvider.CalculateConfidence(result, query, null);
        Assert.Equal(0.0, textOnlyConfidence);

        // With identical cover hash, visual override triggers and yields >= 95% confidence
        ulong localCoverHash = 0b1111000011110000UL;
        double visualConfidence = ComicVineProvider.CalculateConfidence(result, query, localCoverHash);

        Assert.True(visualConfidence >= 0.95, $"Expected visual confidence >= 0.95, got {visualConfidence}");
        Assert.Equal(1.0, result.VisualSimilarity);
    }

    [Fact]
    public void ComicVineProvider_PenalizesSevereYearMismatch()
    {
        var candidateFrom1998 = new ComicSearchResult
        {
            SeriesTitle = "Eden: It's an Endless World!",
            IssueNumber = "2",
            CoverDate = "1998-05-01",
            CoverHash = 0b1111000011110000UL
        };

        var target2006Query = new ComicSearchQuery
        {
            Series = "Eden: It's an Endless World!",
            IssueNumber = "2",
            Year = 2006
        };

        // Even with matching title and issue #, 8-year difference applies severe penalty
        double confidence = ComicVineProvider.CalculateConfidence(candidateFrom1998, target2006Query, null);
        Assert.True(confidence <= 0.50, $"Expected confidence <= 0.50 due to 8-year mismatch penalty, got {confidence}");

        // Severe year mismatch also prevents visual override from over-promoting the wrong decade run
        ulong localCoverHash = 0b1111000011110000UL;
        double visualConfidence = ComicVineProvider.CalculateConfidence(candidateFrom1998, target2006Query, localCoverHash);
        Assert.True(visualConfidence <= 0.40, $"Expected visual confidence <= 0.40 due to volume mismatch, got {visualConfidence}");
    }

    [Fact]
    public void ComicVineProvider_RewardsExactYearMatch()
    {
        var candidate2006 = new ComicSearchResult
        {
            SeriesTitle = "Eden: It's an Endless World!",
            IssueNumber = "2",
            CoverDate = "2006-08-01"
        };

        var target2006Query = new ComicSearchQuery
        {
            Series = "Eden: It's an Endless World!",
            IssueNumber = "2",
            Year = 2006
        };

        double confidence = ComicVineProvider.CalculateConfidence(candidate2006, target2006Query, null);
        Assert.Equal(1.0, confidence); // 0.50 (title) + 0.35 (issue) + 0.25 (year) = 1.0 (100%)
    }

    [Fact]
    public async Task MetadataScraperService_ThrowsWhenApiKeyMissing_IncludesAcquisitionUrl()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempFile);
            settingsService.Settings.ComicVineApiKey = "";
            var scraperService = new MetadataScraperService(settingsService);

            var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scraperService.SearchCandidatesAsync(new ComicSearchQuery { Series = "Spider-Man" }));
            Assert.Contains("https://comicvine.gamespot.com/api/", ex1.Message);

            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scraperService.FetchMetadataAsync("4000-12345"));
            Assert.Contains("https://comicvine.gamespot.com/api/", ex2.Message);

            var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scraperService.SearchSeriesAsync("Batman"));
            Assert.Contains("https://comicvine.gamespot.com/api/", ex3.Message);

            var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scraperService.FetchSeriesIssuesAsync("4050-12345"));
            Assert.Contains("https://comicvine.gamespot.com/api/", ex4.Message);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CandidateOrdering_HighestVisualMatchIsRankedTop()
    {
        ulong localCoverHash = 0b1111000011110000UL;

        var candidateLowVisual = new ComicSearchResult
        {
            SeriesTitle = "Batman",
            IssueNumber = "1",
            CoverHash = 0b0000111100001111UL, // Completely different
            MatchConfidence = 0.85
        };
        candidateLowVisual.VisualSimilarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(localCoverHash, candidateLowVisual.CoverHash.Value);

        var candidateHighVisual = new ComicSearchResult
        {
            SeriesTitle = "Batman",
            IssueNumber = "1",
            CoverHash = 0b1111000011110000UL, // Exact match
            MatchConfidence = 0.80
        };
        candidateHighVisual.VisualSimilarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(localCoverHash, candidateHighVisual.CoverHash.Value);

        var candidateMediumVisual = new ComicSearchResult
        {
            SeriesTitle = "Batman",
            IssueNumber = "1",
            CoverHash = 0b1111000011110001UL, // 1 bit diff (~98% similarity)
            MatchConfidence = 0.90
        };
        candidateMediumVisual.VisualSimilarity = InkTag.Core.Images.PerceptualHashService.CalculateSimilarity(localCoverHash, candidateMediumVisual.CoverHash.Value);

        var candidates = new List<ComicSearchResult> { candidateLowVisual, candidateMediumVisual, candidateHighVisual };

        var sorted = candidates
            .OrderByDescending(c => c.VisualSimilarity ?? 0.0)
            .ThenByDescending(c => c.MatchConfidence)
            .ToList();

        Assert.Same(candidateHighVisual, sorted[0]); // 100% visual match at top
        Assert.Same(candidateMediumVisual, sorted[1]); // ~98% visual match second
        Assert.Same(candidateLowVisual, sorted[2]); // low visual match last
    }

    [Fact]
    public async Task RateLimitedHttpClient_RetriesOnHttp420_AndSucceeds()
    {
        int callCount = 0;
        var handler = new CustomMockHandler((req, ct) =>
        {
            callCount++;
            if (callCount < 3)
            {
                return new HttpResponseMessage((HttpStatusCode)420)
                {
                    Content = new StringContent("Enhance Your Calm")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}")
            };
        });

        var client = new HttpClient(handler);
        var rateLimited = new RateLimitedHttpClient(client)
        {
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxRetries = 3
        };

        string result = await rateLimited.GetStringAsync("https://comicvine.gamespot.com/api/test");

        Assert.Equal(3, callCount);
        Assert.Contains("status", result);
    }

    [Fact]
    public async Task RateLimitedHttpClient_ThrowsAfterMaxRetriesOnHttp420()
    {
        var handler = new CustomMockHandler((req, ct) =>
        {
            return new HttpResponseMessage((HttpStatusCode)420)
            {
                Content = new StringContent("Enhance Your Calm")
            };
        });

        var client = new HttpClient(handler);
        var rateLimited = new RateLimitedHttpClient(client)
        {
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxRetries = 2
        };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => rateLimited.GetStringAsync("https://comicvine.gamespot.com/api/test"));
        Assert.Contains("ComicVine API rate limit reached", ex.Message);
    }

    [Fact]
    public void ScraperCacheService_PersistsAndLoadsFromDisk()
    {
        string tempCacheFile = Path.Combine(Path.GetTempPath(), $"scraper_cache_{Guid.NewGuid():N}.json");

        try
        {
            // 1. Write entries and flush
            using (var cache = new ScraperCacheService(tempCacheFile))
            {
                cache.Set("test-key-1", "{\"title\":\"Batman #1\"}");
                cache.Set("test-key-2", "{\"title\":\"Iron Man #1\"}");
                cache.Flush();
            }

            Assert.True(File.Exists(tempCacheFile));

            // 2. Open new instance and verify persisted data
            using (var loadedCache = new ScraperCacheService(tempCacheFile))
            {
                string? data1 = loadedCache.Get("test-key-1", TimeSpan.FromHours(1));
                string? data2 = loadedCache.Get("test-key-2", TimeSpan.FromHours(1));
                string? expired = loadedCache.Get("test-key-1", TimeSpan.FromMilliseconds(0)); // expired
                string? missing = loadedCache.Get("non-existent", TimeSpan.FromHours(1));

                Assert.Equal("{\"title\":\"Batman #1\"}", data1);
                Assert.Equal("{\"title\":\"Iron Man #1\"}", data2);
                Assert.Null(expired);
                Assert.Null(missing);
            }
        }
        finally
        {
            if (File.Exists(tempCacheFile)) File.Delete(tempCacheFile);
        }
    }

    [Fact]
    public void SharedHttpClient_IsSingletonWithUserAgent()
    {
        var client = InkTag.Core.Net.SharedHttpClient.Instance;

        Assert.NotNull(client);
        Assert.Same(client, InkTag.Core.Net.SharedHttpClient.Instance);
        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
    }

    [Fact]
    public async Task DisposingScraperService_FlushesCacheForOneShotProcesses()
    {
        string tempCache = Path.Combine(Path.GetTempPath(), $"inktag_cv_dispose_{Guid.NewGuid():N}.json");
        string tempSettings = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        string searchJson = @"{""status_code"":1,""results"":[{""id"":1,""name"":""X"",""issue_number"":""1"",""volume"":{""id"":9,""name"":""Batman""}}]}";

        try
        {
            var settings = new AppSettingsService(tempSettings);
            settings.Settings.ComicVineApiKey = "test-key";
            var rateClient = new RateLimitedHttpClient(new HttpClient(new MockHttpMessageHandler { ResponseContent = searchJson }));
            var provider = new ComicVineProvider(rateClient, new ScraperCacheService(tempCache));

            using (var service = new MetadataScraperService(settings, provider))
            {
                await service.SearchCandidatesAsync(new ComicSearchQuery { Series = "Batman", IssueNumber = "1" });

                // The cache write is debounced (2s); a one-shot process would exit before it fires.
                Assert.False(File.Exists(tempCache));
            } // MetadataScraperService.Dispose() -> ComicVineProvider.Dispose() -> synchronous cache flush

            Assert.True(File.Exists(tempCache));

            using var reloaded = new ScraperCacheService(tempCache);
            Assert.NotNull(reloaded.Get("cv_search_Batman 1", TimeSpan.FromHours(1)));
        }
        finally
        {
            if (File.Exists(tempCache)) File.Delete(tempCache);
            if (File.Exists(tempSettings)) File.Delete(tempSettings);
        }
    }

    [Fact]
    public void ComicVineProvider_AllowsVolumeLifespanYearWithoutPenalty()
    {
        var candidateFromVolume1963 = new ComicSearchResult
        {
            SeriesTitle = "The Avengers",
            IssueNumber = "63",
            VolumeStartYear = 1963
        };

        var target1969Query = new ComicSearchQuery
        {
            Series = "The Avengers",
            IssueNumber = "63",
            Year = 1969
        };

        double confidence = ComicVineProvider.CalculateConfidence(candidateFromVolume1963, target1969Query, null);
        Assert.True(confidence >= 0.95, $"Expected confidence >= 0.95 for issue 1969 in volume 1963, got {confidence}");
    }

    private class CustomMockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;
        public CustomMockHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}


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
}

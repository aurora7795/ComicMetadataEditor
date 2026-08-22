using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Komga;
using Xunit;

namespace InkTag.Tests;

public class KomgaClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    [Fact]
    public void KomgaClient_TranslatePath_AppliesPrefixMapping()
    {
        var mappings = new List<KomgaPathMapping>
        {
            new KomgaPathMapping
            {
                LocalPrefix = "/Volumes/ComicsShare",
                ServerPrefix = "/data/comics"
            }
        };

        string localFile = "/Volumes/ComicsShare/Batman (2016)/001.cbz";
        string translated = KomgaClient.TranslatePath(localFile, mappings);

        Assert.Equal("/data/comics/Batman (2016)/001.cbz", translated);
    }

    [Fact]
    public void KomgaClient_TranslatePath_HandlesWindowsBackslashes()
    {
        var mappings = new List<KomgaPathMapping>
        {
            new KomgaPathMapping
            {
                LocalPrefix = @"Z:\Comics",
                ServerPrefix = "/data/comics"
            }
        };

        string localFile = @"Z:\Comics\Spider-Man\001.cbz";
        string translated = KomgaClient.TranslatePath(localFile, mappings);

        Assert.Equal("/data/comics/Spider-Man/001.cbz", translated);
    }

    [Fact]
    public async Task KomgaClient_TestConnectionAsync_ReturnsTrueOnSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                Assert.Contains("/api/v2/users/me", req.RequestUri?.ToString() ?? "");
                Assert.True(req.Headers.Contains("X-API-Key") || req.Headers.Contains("X-Auth-Token"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\": \"user-1\", \"email\": \"admin@komga.org\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        bool success = await client.TestConnectionAsync();
        Assert.True(success);
    }

    [Fact]
    public async Task KomgaClient_FindBookAndAnalyze_TriggersTargetedEndpoint()
    {
        bool analyzeCalled = false;
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("/api/v1/books?search="))
                {
                    var page = new KomgaPageWrapper<KomgaBookDto>
                    {
                        Content = new List<KomgaBookDto>
                        {
                            new KomgaBookDto
                            {
                                Id = "book-123",
                                SeriesId = "series-456",
                                Name = "001.cbz",
                                Url = "/data/comics/Daredevil/001.cbz"
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page))
                    };
                }
                if (url.EndsWith("/api/v1/books/book-123/analyze"))
                {
                    Assert.Equal(HttpMethod.Post, req.Method);
                    analyzeCalled = true;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-key", httpClient: httpClient);

        var book = await client.FindBookByFilePathAsync("/data/comics/Daredevil/001.cbz");
        Assert.NotNull(book);
        Assert.Equal("book-123", book!.Id);

        bool analyzed = await client.AnalyzeBookAsync(book.Id);
        Assert.True(analyzed);
        Assert.True(analyzeCalled);
    }

    [Fact]
    public async Task KomgaSyncService_SyncsStoryArcCollection()
    {
        bool collectionCreated = false;
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("/api/v1/books?search="))
                {
                    var page = new KomgaPageWrapper<KomgaBookDto>
                    {
                        Content = new List<KomgaBookDto>
                        {
                            new KomgaBookDto
                            {
                                Id = "book-999",
                                SeriesId = "series-777",
                                Name = "001.cbz",
                                Url = "/comics/Avengers/001.cbz"
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page))
                    };
                }
                if (url.EndsWith("/api/v1/books/book-999/analyze"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (url.Contains("/api/v1/collections?search="))
                {
                    // Return empty collections search
                    var emptyPage = new KomgaPageWrapper<KomgaCollectionDto> { Content = new() };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(emptyPage))
                    };
                }
                if (url.EndsWith("/api/v1/collections") && req.Method == HttpMethod.Post)
                {
                    collectionCreated = true;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"id\":\"col-1\",\"name\":\"Infinity Gauntlet\"}")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        var settingsService = new AppSettingsService();
        settingsService.Settings.KomgaServerUrl = "http://localhost:25600";
        settingsService.Settings.KomgaApiKey = "token";
        settingsService.Settings.KomgaSyncStoryArcsToCollections = true;

        using var client = new KomgaClient(settingsService, httpClient);
        var syncService = new KomgaSyncService(settingsService, client);

        var comicInfo = new ComicInfo
        {
            Series = "Avengers",
            Number = "1",
            StoryArc = "Infinity Gauntlet"
        };

        var report = await syncService.SyncComicFileAsync("/comics/Avengers/001.cbz", comicInfo);

        Assert.True(report.IsSuccess);
        Assert.Equal(1, report.BooksAnalyzed);
        Assert.Equal(1, report.CollectionsSynced);
        Assert.True(collectionCreated);
    }
}

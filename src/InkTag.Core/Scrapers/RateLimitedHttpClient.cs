using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace InkTag.Core.Scrapers;

public class RateLimitedHttpClient
{
    private static readonly SemaphoreSlim RateLimitLock = new(1, 1);
    private static DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1050); // Just over 1 sec for API compliance

    private readonly HttpClient _client;

    public RateLimitedHttpClient(HttpClient? httpClient = null)
    {
        _client = httpClient ?? new HttpClient();
        if (!_client.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ComicMetadataEditor)");
        }
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        await RateLimitLock.WaitAsync(ct);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - _lastRequestTime;
            if (elapsed < MinimumInterval)
            {
                TimeSpan delay = MinimumInterval - elapsed;
                await Task.Delay(delay, ct);
            }

            _lastRequestTime = DateTimeOffset.UtcNow;
            HttpResponseMessage response = await _client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
        finally
        {
            RateLimitLock.Release();
        }
    }
}

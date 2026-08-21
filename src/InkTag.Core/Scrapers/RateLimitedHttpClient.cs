using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Logging;

namespace InkTag.Core.Scrapers;

public class RateLimitedHttpClient
{
    private static readonly SemaphoreSlim RateLimitLock = new(1, 1);
    private static DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1050); // Just over 1 sec for API compliance

    private readonly HttpClient _client;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(5);

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
        int attempt = 0;
        TimeSpan currentBackoff = InitialBackoff;

        while (true)
        {
            await RateLimitLock.WaitAsync(ct);
            bool lockReleasedInLoop = false;
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

                int statusCode = (int)response.StatusCode;
                // HTTP 420 (ComicVine "Enhance Your Calm") or HTTP 429 ("Too Many Requests")
                if (statusCode == 420 || statusCode == 429 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    attempt++;
                    if (attempt > MaxRetries)
                    {
                        throw new HttpRequestException(
                            $"ComicVine API rate limit reached (HTTP {statusCode}). The hourly request limit has been exceeded. Please wait a few minutes before continuing bulk operations.",
                            null,
                            response.StatusCode);
                    }

                    TimeSpan retryDelay = currentBackoff;
                    if (response.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        retryDelay = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter?.Date.HasValue == true)
                    {
                        var waitTime = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        if (waitTime > TimeSpan.Zero)
                        {
                            retryDelay = waitTime;
                        }
                    }

                    AppLogger.LogWarning($"[RateLimit] ComicVine rate limit encountered (HTTP {statusCode}). Backing off for {retryDelay.TotalSeconds:F0}s (Attempt {attempt}/{MaxRetries})...");

                    currentBackoff = TimeSpan.FromSeconds(currentBackoff.TotalSeconds * 2);

                    response.Dispose();
                    RateLimitLock.Release();
                    lockReleasedInLoop = true;

                    await Task.Delay(retryDelay, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            finally
            {
                if (!lockReleasedInLoop)
                {
                    RateLimitLock.Release();
                }
            }
        }
    }
}

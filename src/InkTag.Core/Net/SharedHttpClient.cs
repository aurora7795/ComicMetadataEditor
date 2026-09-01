using System;
using System.Net;
using System.Net.Http;

namespace InkTag.Core.Net;

/// <summary>
/// Process-wide <see cref="HttpClient"/> for outbound scraper and cover-thumbnail traffic.
/// A single pooled <see cref="SocketsHttpHandler"/> avoids the socket / ephemeral-port
/// exhaustion that results from allocating a new <see cref="HttpClient"/> per request or
/// per short-lived service instance. Per-request time limits are enforced by the callers
/// with linked <see cref="System.Threading.CancellationTokenSource"/>s, so the instance
/// itself keeps the default (long) timeout.
/// </summary>
public static class SharedHttpClient
{
    /// <summary>The shared client. Never dispose this instance.</summary>
    public static HttpClient Instance { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ComicMetadataEditor)");
        return client;
    }
}

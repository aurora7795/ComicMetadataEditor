using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Configuration;
using InkTag.Core.Logging;

namespace InkTag.Core.Komga;

public class KomgaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _serverUrl;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KomgaClient(AppSettingsService settingsService, HttpClient? httpClient = null)
        : this(
            settingsService.GetEffectiveKomgaServerUrl(),
            settingsService.GetEffectiveKomgaApiKey(),
            settingsService.GetEffectiveKomgaUser(),
            settingsService.GetEffectiveKomgaPassword(),
            httpClient)
    {
    }

    public KomgaClient(
        string serverUrl,
        string? apiKey = null,
        string? user = null,
        string? password = null,
        HttpClient? httpClient = null)
    {
        _serverUrl = CleanServerUrl(serverUrl);
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();

        ConfigureAuthentication(apiKey, user, password);
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string CleanServerUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;
        rawUrl = rawUrl.Trim();

        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = "http://" + rawUrl;
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            string path = uri.AbsolutePath.TrimEnd('/');
            // Strip common web UI suffixes if pasted directly from browser
            while (path.EndsWith("/login", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/dashboard", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/opds", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                int idx = path.LastIndexOf('/');
                path = idx > 0 ? path.Substring(0, idx) : "";
            }

            return $"{uri.Scheme}://{uri.Authority}{path}".TrimEnd('/');
        }

        return rawUrl.TrimEnd('/');
    }

    private void ConfigureAuthentication(string? apiKey, string? user, string? password)
    {
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Remove("X-Requested-With");
        _httpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        _httpClient.DefaultRequestHeaders.Remove("X-Auth-Token");
        _httpClient.DefaultRequestHeaders.Authorization = null;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            string key = apiKey.Trim();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", key);
            _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", key);
        }
        else if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user.Trim()}:{password.Trim()}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_serverUrl);

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            // 1. Try v2/users/me (Modern Komga)
            string endpointV2 = $"{_serverUrl}/api/v2/users/me";
            using var responseV2 = await _httpClient.GetAsync(endpointV2, ct);
            if (responseV2.IsSuccessStatusCode)
            {
                string? mediaType = responseV2.Content.Headers.ContentType?.MediaType;
                if (mediaType == null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogInfo($"[KomgaClient] Successfully authenticated with Komga server at '{_serverUrl}'.");
                    return true;
                }
            }

            // 2. Try v1/libraries (Direct library access check)
            string librariesEndpoint = $"{_serverUrl}/api/v1/libraries";
            using var libResponse = await _httpClient.GetAsync(librariesEndpoint, ct);
            if (libResponse.IsSuccessStatusCode)
            {
                string? mediaType = libResponse.Content.Headers.ContentType?.MediaType;
                if (mediaType == null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogInfo($"[KomgaClient] Successfully authenticated with Komga server at '{_serverUrl}'.");
                    return true;
                }
            }

            // 3. Try v1/users/me (Legacy user endpoint)
            string endpointV1 = $"{_serverUrl}/api/v1/users/me";
            using var responseV1 = await _httpClient.GetAsync(endpointV1, ct);
            if (responseV1.IsSuccessStatusCode)
            {
                string? mediaType = responseV1.Content.Headers.ContentType?.MediaType;
                if (mediaType == null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogInfo($"[KomgaClient] Successfully authenticated with Komga server at '{_serverUrl}'.");
                    return true;
                }
            }

            if ((int)libResponse.StatusCode == 401 || (int)libResponse.StatusCode == 403)
            {
                AppLogger.LogWarning($"[KomgaClient] Authentication rejected by Komga server at '{_serverUrl}' (HTTP {(int)libResponse.StatusCode}). Check your API Key or Username/Password.");
                return false;
            }

            // Fallback for servers without auth enabled
            string claimEndpoint = $"{_serverUrl}/api/v1/claim";
            using var claimResponse = await _httpClient.GetAsync(claimEndpoint, ct);
            if (claimResponse.IsSuccessStatusCode)
            {
                string? mediaType = claimResponse.Content.Headers.ContentType?.MediaType;
                return mediaType == null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Connection test failed for '{_serverUrl}': {ex.Message}");
        }

        return false;
    }

    public async Task<IReadOnlyList<KomgaLibraryDto>> GetLibrariesAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Array.Empty<KomgaLibraryDto>();

        try
        {
            string endpoint = $"{_serverUrl}/api/v1/libraries";
            var libraries = await GetJsonSafeAsync<List<KomgaLibraryDto>>(endpoint, ct);
            return libraries ?? new List<KomgaLibraryDto>();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to retrieve libraries: {ex.Message}");
            return Array.Empty<KomgaLibraryDto>();
        }
    }

    public async Task<KomgaBookDto?> FindBookByFilePathAsync(
        string filePath,
        IEnumerable<KomgaPathMapping>? mappings = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(filePath)) return null;

        try
        {
            string fileName = Path.GetFileName(filePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string translatedPath = TranslatePath(filePath, mappings);

            // Search by filename without extension on Komga
            string endpoint = $"{_serverUrl}/api/v1/books?search={Uri.EscapeDataString(fileNameWithoutExt)}&size=50";
            var page = await GetJsonSafeAsync<KomgaPageWrapper<KomgaBookDto>>(endpoint, ct);

            // Fallback search with full filename if not found
            if (page?.Content == null || page.Content.Count == 0)
            {
                endpoint = $"{_serverUrl}/api/v1/books?search={Uri.EscapeDataString(fileName)}&size=50";
                page = await GetJsonSafeAsync<KomgaPageWrapper<KomgaBookDto>>(endpoint, ct);
            }

            if (page?.Content != null && page.Content.Count > 0)
            {
                // 1. Exact translated path match priority
                var match = page.Content.FirstOrDefault(b =>
                    string.Equals(b.Url, translatedPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(b.Url), fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Name, fileNameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Name, fileName, StringComparison.OrdinalIgnoreCase));

                return match ?? page.Content[0];
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to find book on Komga for '{filePath}': {ex.Message}");
        }

        return null;
    }

    public async Task<KomgaSeriesDto?> FindSeriesByPathOrNameAsync(
        string seriesPath,
        string seriesName,
        IEnumerable<KomgaPathMapping>? mappings = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        try
        {
            string searchName = !string.IsNullOrWhiteSpace(seriesName) ? seriesName : Path.GetFileName(seriesPath);
            string translatedPath = TranslatePath(seriesPath, mappings);

            string endpoint = $"{_serverUrl}/api/v1/series?search={Uri.EscapeDataString(searchName)}&size=20";
            var page = await GetJsonSafeAsync<KomgaPageWrapper<KomgaSeriesDto>>(endpoint, ct);

            if (page?.Content != null && page.Content.Count > 0)
            {
                var match = page.Content.FirstOrDefault(s =>
                    string.Equals(s.Url, translatedPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, searchName, StringComparison.OrdinalIgnoreCase));

                return match ?? page.Content[0];
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to find series on Komga for '{seriesName}': {ex.Message}");
        }

        return null;
    }

    public async Task<bool> AnalyzeBookAsync(string bookId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(bookId)) return false;

        try
        {
            string endpoint = $"{_serverUrl}/api/v1/books/{bookId}/analyze";
            using var response = await _httpClient.PostAsync(endpoint, null, ct);
            if (response.IsSuccessStatusCode)
            {
                AppLogger.LogDebug($"[KomgaClient] Triggered targeted analysis for book '{bookId}'.");
                return true;
            }
            else
            {
                AppLogger.LogWarning($"[KomgaClient] Analyze book '{bookId}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to trigger analysis for book '{bookId}': {ex.Message}");
        }

        return false;
    }

    public async Task<bool> AnalyzeSeriesAsync(string seriesId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(seriesId)) return false;

        try
        {
            string endpoint = $"{_serverUrl}/api/v1/series/{seriesId}/analyze";
            using var response = await _httpClient.PostAsync(endpoint, null, ct);
            if (response.IsSuccessStatusCode)
            {
                AppLogger.LogDebug($"[KomgaClient] Triggered targeted analysis for series '{seriesId}'.");
                return true;
            }
            else
            {
                AppLogger.LogWarning($"[KomgaClient] Analyze series '{seriesId}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to trigger analysis for series '{seriesId}': {ex.Message}");
        }

        return false;
    }

    public async Task<bool> UpdateSeriesStatusAsync(string seriesId, string status, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(seriesId) || string.IsNullOrWhiteSpace(status)) return false;

        try
        {
            string endpoint = $"{_serverUrl}/api/v1/series/{seriesId}/metadata";
            var payload = new { status = status.ToUpperInvariant() };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Patch, endpoint) { Content = content };
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to update series status for '{seriesId}': {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SyncStoryArcCollectionAsync(string seriesId, string storyArcName, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(seriesId) || string.IsNullOrWhiteSpace(storyArcName)) return false;

        try
        {
            string searchEndpoint = $"{_serverUrl}/api/v1/collections?search={Uri.EscapeDataString(storyArcName)}&size=20";
            var page = await GetJsonSafeAsync<KomgaPageWrapper<KomgaCollectionDto>>(searchEndpoint, ct);

            var existing = page?.Content?.FirstOrDefault(c => string.Equals(c.Name, storyArcName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!existing.SeriesIds.Contains(seriesId, StringComparer.OrdinalIgnoreCase))
                {
                    existing.SeriesIds.Add(seriesId);
                    string patchEndpoint = $"{_serverUrl}/api/v1/collections/{existing.Id}";
                    var payload = new { seriesIds = existing.SeriesIds };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(HttpMethod.Patch, patchEndpoint) { Content = content };
                    using var response = await _httpClient.SendAsync(request, ct);
                    return response.IsSuccessStatusCode;
                }
                return true;
            }

            // Create new collection
            string createEndpoint = $"{_serverUrl}/api/v1/collections";
            var newCollection = new KomgaCollectionCreationDto
            {
                Name = storyArcName.Trim(),
                Ordered = false,
                SeriesIds = new List<string> { seriesId }
            };

            using var createResponse = await _httpClient.PostAsJsonAsync(createEndpoint, newCollection, JsonOptions, ct);
            return createResponse.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to sync story arc collection '{storyArcName}': {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<KomgaBookDto>> GetUntaggedOrErrorBooksAsync(string? libraryId = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return Array.Empty<KomgaBookDto>();

        try
        {
            string endpoint = $"{_serverUrl}/api/v1/books?media_status=UNSUPPORTED,ERROR&size=100";
            if (!string.IsNullOrWhiteSpace(libraryId))
            {
                endpoint += $"&library_id={libraryId}";
            }

            var page = await GetJsonSafeAsync<KomgaPageWrapper<KomgaBookDto>>(endpoint, ct);
            return page?.Content ?? new List<KomgaBookDto>();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[KomgaClient] Failed to fetch error books: {ex.Message}");
            return Array.Empty<KomgaBookDto>();
        }
    }

    private async Task<T?> GetJsonSafeAsync<T>(string endpoint, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(endpoint, ct);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.LogWarning($"[KomgaClient] GET '{endpoint}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return default;
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.LogWarning($"[KomgaClient] GET '{endpoint}' returned HTML instead of JSON. Server may have redirected to login.");
            return default;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    public static string TranslatePath(string localPath, IEnumerable<KomgaPathMapping>? mappings)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return string.Empty;
        if (mappings == null) return localPath;

        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.LocalPrefix) && !string.IsNullOrWhiteSpace(mapping.ServerPrefix))
            {
                string localPrefix = mapping.LocalPrefix.Trim().TrimEnd('/', '\\');
                string serverPrefix = mapping.ServerPrefix.Trim().TrimEnd('/', '\\');

                if (localPath.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = localPath.Substring(localPrefix.Length).TrimStart('/', '\\');
                    return $"{serverPrefix}/{relative.Replace('\\', '/')}";
                }
            }
        }

        return localPath;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InkTag.Core.Scrapers;

public interface IMetadataScraperProvider
{
    string ProviderName { get; }
    bool RequiresApiKey { get; }
    bool SupportsSeriesSearch { get; }
    Task<IEnumerable<ComicSearchResult>> SearchAsync(ComicSearchQuery query, string apiKey, CancellationToken ct = default);
    Task<ComicInfo> FetchComicMetadataAsync(string issueId, string apiKey, CancellationToken ct = default);
    Task<IEnumerable<SeriesSearchResult>> SearchSeriesAsync(string seriesTitle, string apiKey, CancellationToken ct = default);
    Task<IEnumerable<ComicSearchResult>> FetchSeriesIssuesAsync(string volumeId, string apiKey, int page = 1, int pageSize = 50, ComicSearchQuery? query = null, CancellationToken ct = default);
}

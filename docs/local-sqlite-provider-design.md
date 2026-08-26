# RFC: Local SQLite Metadata Provider Backend

## 1. Overview & Objective
Provide an offline, zero-rate-limit alternative to the online ComicVine API by enabling InkTag to query a local SQLite database (`.sqlite` / `.db` / `.sqlite3`) for comic metadata, series volume clustering, and cover perceptual matching.

---

## 2. Architecture & Abstraction Layer

InkTag already abstracts metadata scrapers via [`IMetadataScraperProvider`](../src/InkTag.Core/Scrapers/IMetadataScraperProvider.cs):

```csharp
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
```

A new `SqliteMetadataProvider : IMetadataScraperProvider` will be implemented using `Microsoft.Data.Sqlite` or Dapper.

---

## 3. Database Interrogation & Schema Discovery Process

When the SQLite database file is provided, run the following read-only interrogation steps:

### A. Schema Extraction
```sql
-- Dumps table DDLs, views, and indexes
SELECT type, name, sql FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY name;
```

### B. Table Introspection
```sql
-- For each table, inspect column names, types, nullability, and primary keys
PRAGMA table_info('table_name');
PRAGMA foreign_key_list('table_name');
```

### C. Sample Record Inspection
```sql
SELECT * FROM [table_name] LIMIT 3;
```

---

## 4. Semantic Field Mapping Target (`ComicInfo`)

The query adapter will map table records to InkTag's core models:

| Target Model / Property | Target XML Field | Description & Typical Source Table Column |
| :--- | :--- | :--- |
| `ComicInfo.Series` | `<Series>` | Series/Volume title |
| `ComicInfo.Number` | `<Number>` | Issue number string (e.g., `"121"`, `"0"`, `"1/2"`) |
| `ComicInfo.Title` | `<Title>` | Story or issue title |
| `ComicInfo.Volume` | `<Volume>` | Volume number or publication start year |
| `ComicInfo.Summary` | `<Summary>` | Issue plot synopsis or description |
| `ComicInfo.Year` / `Month` / `Day` | `<Year>`, `<Month>`, `<Day>` | Publication or cover date |
| `ComicInfo.Publisher` | `<Publisher>` | Publishing house (Marvel, DC, Image, etc.) |
| `ComicInfo.Imprint` | `<Imprint>` | Imprint (Vertigo, Black Label, Max, etc.) |
| `ComicInfo.Writer` | `<Writer>` | Comma-separated writer names from credits |
| `ComicInfo.Penciller` | `<Penciller>` | Comma-separated penciller names |
| `ComicInfo.Inker` | `<Inker>` | Comma-separated inker names |
| `ComicInfo.Colorist` | `<Colorist>` | Comma-separated colorist names |
| `ComicInfo.Letterer` | `<Letterer>` | Comma-separated letterer names |
| `ComicInfo.CoverArtist` | `<CoverArtist>` | Comma-separated cover artist names |
| `ComicInfo.Editor` | `<Editor>` | Comma-separated editor names |
| `ComicInfo.Characters` | `<Characters>` | Comma-separated character appearances |
| `ComicInfo.Teams` | `<Teams>` | Comma-separated team appearances |
| `ComicInfo.Locations` | `<Locations>` | Comma-separated setting locations |
| `ComicInfo.StoryArc` | `<StoryArc>` | Story crossover / storyline name |
| `ComicInfo.SeriesGroup` | `<SeriesGroup>` | Series grouping / franchise name |
| `ComicSearchResult.CoverHash` | N/A | Pre-calculated 64-bit dHash integer if stored in DB |

---

## 5. Implementation Roadmap

1. **Schema Interrogation & Mapping Definition**:
   - Run discovery queries on the target SQLite file.
   - Author specific SQL queries for Series Search, Volume Issues, and Issue Metadata.
2. **Provider Implementation**:
   - Implement `SqliteMetadataProvider.cs` under `InkTag.Core/Scrapers/`.
   - Implement query caching or in-memory index if beneficial.
3. **Settings & Configuration UI**:
   - Add Provider selection (`ComicVine API` vs `Local SQLite Database`) in Settings.
   - Add SQLite file path picker and connection verification test button.
4. **Scraper Service & GUI Decoupling**:
   - Parameterize UI labels and bypass API Key validation dialog when SQLite provider is active.
5. **CLI & MCP Tool Extensions**:
   - Add `--provider sqlite --db <path>` flags to CLI `scrape` command.
   - Expose SQLite provider options to MCP scraping tools.
6. **Automated Unit Testing**:
   - Unit tests against sample SQLite database fixtures in `tests/InkTag.Tests/`.

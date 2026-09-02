# Core Metadata Editor API

This page documents the API of the `InkTag.Core` domain library, along with the agent-facing CLI (`InkTag.Cli`) and MCP (`InkTag.Mcp`) interfaces.

---

## 1. `ComicInfo.cs` (Data Model)
The `ComicInfo` class corresponds to the standard XML schema (`ComicInfo.xml`) used by major comic readers (e.g., ComicRack, YACReader).

### Fields & Schema Properties
All properties are nullable to avoid writing default XML tags when optional metadata is omitted:

| Property Name | XML Tag | Data Type | Description |
| :--- | :--- | :--- | :--- |
| `Title` | `<Title>` | `string?` | Issue title |
| `Series` | `<Series>` | `string?` | Series name |
| `Number` | `<Number>` | `string?` | Issue number (supports decimal/alphanumeric like "1.5") |
| `Count` | `<Count>` | `int?` | Total issue count in series |
| `Volume` | `<Volume>` | `int?` | Series volume number |
| `AlternateSeries` | `<AlternateSeries>` | `string?` | Alternate / cross-over series name |
| `AlternateNumber` | `<AlternateNumber>` | `string?` | Alternate issue number |
| `AlternateCount` | `<AlternateCount>` | `int?` | Alternate issue count |
| `Summary` | `<Summary>` | `string?` | Story summary / plot synopsis |
| `Notes` | `<Notes>` | `string?` | General notes |
| `Year` | `<Year>` | `int?` | Publication year (4-digit) |
| `Month` | `<Month>` | `int?` | Publication month (1-12) |
| `Day` | `<Day>` | `int?` | Publication day (1-31) |
| `Writer` | `<Writer>` | `string?` | Writer credits |
| `Penciller` | `<Penciller>` | `string?` | Penciller credits |
| `Inker` | `<Inker>` | `string?` | Inker credits |
| `Colorist` | `<Colorist>` | `string?` | Colorist credits |
| `Letterer` | `<Letterer>` | `string?` | Letterer credits |
| `CoverArtist` | `<CoverArtist>` | `string?` | Cover artist credits |
| `Editor` | `<Editor>` | `string?` | Editor credits |
| `Publisher` | `<Publisher>` | `string?` | Publishing company (e.g. Marvel, DC) |
| `Imprint` | `<Imprint>` | `string?` | Publisher imprint |
| `Genre` | `<Genre>` | `string?` | Genre classifications |
| `Tags` | `<Tags>` | `string?` | Keywords / user tags |
| `Web` | `<Web>` | `string?` | Web link URL |
| `PageCount` | `<PageCount>` | `int?` | Total page count |
| `LanguageISO` | `<LanguageISO>` | `string?` | ISO language code (e.g. "en", "ja") |
| `Format` | `<Format>` | `string?` | Publication format |
| `BlackAndWhite` | `<BlackAndWhite>` | `string?` | `"Yes"` or `"No"` |
| `Manga` | `<Manga>` | `MangaDirection?` | Enum: `Unknown`, `No`, `Yes`, `YesAndRightToLeft` |
| `Characters` | `<Characters>` | `string?` | Featured characters |
| `Teams` | `<Teams>` | `string?` | Featured super teams |
| `Locations` | `<Locations>` | `string?` | Featured story locations |
| `ScanInformation` | `<ScanInformation>` | `string?` | Scanner / release group details |
| `StoryArc` | `<StoryArc>` | `string?` | Story arc name |
| `SeriesGroup` | `<SeriesGroup>` | `string?` | Series group designation |
| `AgeRating` | `<AgeRating>` | `string?` | Content age rating |
| `CommunityRating` | `<CommunityRating>` | `decimal?` | Community star rating (0-5) |
| `MainCharacterOrTeam` | `<MainCharacterOrTeam>` | `string?` | Primary character or team focus |
| `Review` | `<Review>` | `string?` | Comic review / critical remarks |
| `Pages` | `<Pages>` | `PageCollection?` | Structured page-level array (`Page[]`) |

---

## 2. `MetadataEditor.cs` (Engine)

The core engine handles loading, modifying, dynamic JSON patching, cover extraction, and safely writing XML metadata back into compressed CBZ/CBR archives.

### Core & Agent API Method Signatures

#### `OpenReadOptimized`
* **Signature**: `public static FileStream OpenReadOptimized(string filePath, int bufferSize = 65536)`
* **Description**: Opens a `FileStream` configured with a 64KB buffer, `FileShare.ReadWrite` (eliminating sharing collisions with media servers like Komga/Kavita/Plex), and `FileOptions.None` (enabling bidirectional backward seeks over Linux FUSE / GVFS / FTP / SMB network mounts).

#### `HasMetadata`
* **Signature**: `public bool HasMetadata(string filePath, CancellationToken cancellationToken = default)`
* **Description**: Fast check to verify whether an archive contains an embedded `ComicInfo.xml` entry without fully parsing and loading the document.

#### `ReadMetadata` / `ReadMetadataAsync` / `ReadMetadataAsJson`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, CancellationToken cancellationToken = default)`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, CancellationToken cancellationToken = default)`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, out bool usedSequentialFallback, CancellationToken cancellationToken = default)`
* **Signature**: `public Task<ComicInfo> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default)`
* **Signature**: `public string ReadMetadataAsJson(string filePath)`
* **Description**: Parses `ComicInfo.xml` directly in-memory with zero temporary disk extraction via `ComicArchiveHandler`.
  1. *Fast-Path (.cbz)*: Reads the Central Directory using .NET's built-in `System.IO.Compression.ZipArchive` for 1-seek metadata access.
  2. *Sequential Forward Fallback*: If backward seeks fail on virtual mounts (e.g. Linux GVFS FTP / FUSE), wraps the stream in a `NonSeekableStream` with `CancellationToken` checks, reading local headers forward from byte 0.
  3. *Fallback & .cbr (RAR)*: Reads via SharpCompress `ArchiveFactory.OpenArchive(stream, new ReaderOptions { LookForHeader = true })` to safely recover metadata across high-latency network shares.

#### `EditMetadata` / `EditMetadataAsync` / `EditMetadataFromJson`
* **Signature**: `public void EditMetadata(string filePath, Action<ComicInfo> editAction, string? batchJobId = null, string? changeReason = null, string? coverDHash = null, string? matchedThumbnailUrl = null, double? matchConfidence = null, double? visualSimilarity = null)`
* **Signature**: `public Task EditMetadataAsync(string filePath, Action<ComicInfo> editAction, string? batchJobId = null, string? changeReason = null, string? coverDHash = null, string? matchedThumbnailUrl = null, double? matchConfidence = null, double? visualSimilarity = null, CancellationToken cancellationToken = default)`
* **Signature**: `public void EditMetadataFromJson(string filePath, string jsonPatch)`
* **Description**: Takes an automated pre-write metadata backup snapshot via `MetadataBackupService`, unpacks the archive into a temporary folder, deserializes existing metadata or creates a new instance, applies edits (via lambda or dynamic JSON patch), serializes back to XML using cached `XmlSerializer`, compresses to `.tmp`, validates, and performs an atomic backup swap via `ArchiveSwapService`.

#### `BulkEditMetadata` / `BulkEditMetadataAsync` / `BulkEditMetadataFromJson`
* **Signature**: `public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction, bool recursive = false)`
* **Signature**: `public Task<BulkEditReport> BulkEditMetadataAsync(string directoryPath, Action<ComicInfo> editAction, bool recursive = false, IProgress<BulkEditProgress>? progress = null, CancellationToken cancellationToken = default)`
* **Signature**: `public BulkEditReport BulkEditMetadataFromJson(string directoryPath, string jsonPatch, bool recursive = false)`
* **Description**: Executes metadata edits on all `.cbz`/`.cbr` archives in a directory using lazy `Directory.EnumerateFiles` streaming to minimize memory footprint. Reports real-time item-by-item progress and supports `CancellationToken` cancellation.

#### `GetMetadataDiff`
* **Signature**: `public List<MetadataDiffItem> GetMetadataDiff(string filePath, string jsonPatch)`
* **Description**: Previews property-level before/after diffs between the archive's current metadata and a proposed JSON patch.

#### `ApplyJsonPatch`
* **Signature**: `public static List<string> ApplyJsonPatch(ComicInfo comicInfo, string jsonPatch)`
* **Description**: Mutates a `ComicInfo` instance in-place by parsing property key-values from a JSON patch string. Returns warnings for unrecognized properties.

#### `ExtractCoverImage` / `ExtractCoverImageBytes` / `ExtractCoverImageBytesAsync` / `GetCoverHash`
* **Signature**: `public string? ExtractCoverImage(string comicFilePath, string outputFilePath, int pageIndex = 0)`
* **Signature**: `public byte[]? ExtractCoverImageBytes(string filePath, int pageIndex = 0)`
* **Signature**: `public Task<byte[]?> ExtractCoverImageBytesAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default)`
* **Signature**: `public ulong GetCoverHash(string filePath, int pageIndex = 0)`
* **Signature**: `public Task<ulong> GetCoverHashAsync(string filePath, int pageIndex = 0, CancellationToken cancellationToken = default)`
* **Signature**: `public List<(int PageIndex, ulong Hash, byte[] Bytes)> GetCandidateCoverHashes(string filePath, int maxPages = 2)`
* **Description**: Extracts a 0-based page image (default `0` / front cover) from a `.cbz` or `.cbr` archive in-memory via stream decoding, or computes its 64-bit perceptual dHash. `GetCandidateCoverHashes` returns the first `maxPages` in order for provider-intro-page detection.

#### `StripFirstPage` / `RemoveArchivePages`
* **Signature**: `public PageRemovalResult StripFirstPage(string filePath)`
* **Signature**: `public PageRemovalResult RemoveArchivePages(string filePath, IEnumerable<int> pageIndices)`
* **Description**: Removes one or more 0-based page images (e.g. a provider/scanner intro page) from an archive, decrements `ComicInfo.xml` `PageCount`, renumbers the `Pages` collection, repacks (`.cbr` → `.cbz`), and returns a `PageRemovalResult` with the removed entry names and before/after page counts. Uses a temporary `.bak` for rollback.

#### `IsSupportedComicFile`
* **Signature**: `public static bool IsSupportedComicFile(string? filePath)`
* **Description**: Checks whether a given path is a valid comic archive (`.cbz` or `.cbr`), filtering out macOS resource forks (`._*`), AppleDouble folders (`.AppleDouble`), `__MACOSX`, `.git`, `.Trash`, and hidden system files.

#### `ExportJsonSchema`
* **Signature**: `public static string ExportJsonSchema()`
* **Description**: Returns the JSON Schema specification for `ComicInfo` objects.

---

## 3. Domain Exception Hierarchy (`InkTag.Core.Exceptions`)

All operational and data integrity errors are structured under `InkTag.Core.Exceptions`:

* **`InkTagException`**: Base exception for all InkTag domain operational errors.
* **`ComicArchiveException`**: Base exception for archive operations, carrying `FilePath`.
* **`ComicArchiveCorruptException`**: Thrown when an archive is corrupted, truncated, empty, or fails integrity validation.
* **`MetadataXmlSanitizationException`**: Thrown when ComicInfo XML cannot be sanitized or parsed, carrying `XmlContentSnippet`.
* **`UnsafeArchiveEntryException`**: Thrown when zip-slip path traversal (`../`) is detected during extraction, carrying `EntryName`.

---

## 4. Supplementary Core Services

### `MetadataBackupService.cs` (Disaster Recovery & Rollback Engine)
* **Namespace**: `InkTag.Core.Backup`
* **Methods**:
  * `CreateBackup(string archivePath, string? originalXml, string operationType, string? batchJobId = null, string? sourceFileHash = null, string? coverDHash = null, string? matchedThumbnailUrl = null, double? matchConfidence = null, double? visualSimilarity = null, string? changeReason = null, List<MetadataDiffItem>? fieldDiffs = null)`: Writes a timestamped pre-write snapshot of `ComicInfo.xml` to `~/.local/share/InkTag/backups/` and appends a rich provenance record to `backups_manifest.json`.
  * `RestoreBackup(string archivePath, string? backupId = null)`: Restores an archive's `ComicInfo.xml` from a specific `backupId`, or the most recent snapshot for that archive when `backupId` is null.
  * `ListBackups(string? archivePath = null, int limit = 50)`: Returns snapshot history for one archive or the whole store.
  * `GetBackupEntry(string backupId)` / `GetBackupXml(string backupId)`: Fetch a single provenance record, or its raw snapshot XML.
  * `ListBatchJobs(int limit = 20)`: Lists recorded multi-file batch operations grouped by `BatchJobId`.
  * `RestoreBatchJob(string batchJobId)`: Restores every archive in a batch to its pre-batch snapshot, continuing past individual failures (returns a `BatchRollbackReport` with the failure list). Not transactional — see [#21](https://github.com/aurora7795/InkTag/issues/21).

### `PerceptualHashService.cs` (Perceptual Image Hashing & dHash)
* **Namespace**: `InkTag.Core.Images`
* **Methods** (all `static`):
  * `ComputeDHash(byte[] imageBytes)` / `ComputeDHash(Stream imageStream)`: Computes a 64-bit difference hash (dHash) by downscaling the image to 9×8 grayscale and comparing horizontal gradient intensities. Returns `0` if the image cannot be decoded.
  * `ComputeHammingDistance(ulong hashA, ulong hashB)`: Number of differing bits (`BitOperations.PopCount`).
  * `CalculateSimilarity(ulong hashA, ulong hashB)`: Visual similarity score (0.0 to 1.0) from Hamming distance; returns `0.0` if either hash is `0`.
  * `IsVisualMatch(ulong hashA, ulong hashB, double threshold = 0.90)`: Fast boolean match check.

### `ComicBookInfoParser.cs` (Legacy CBI Ingestion)
* **Namespace**: `InkTag.Core.Parsing`
* **Methods**:
  * `TryParse(string jsonOrComment, out ComicInfo comic)`: Parses legacy ComicBookInfo JSON embedded in zip archive comments and maps it to modern `ComicInfo` schema properties.

### `KomgaSyncService.cs` & `KomgaClient.cs` (Media Server Integration)
* **Namespace**: `InkTag.Core.Komga`
* **`KomgaSyncService`**:
  * `IsConfigured`: True when a Komga server URL is set (settings or `KOMGA_SERVER_URL`).
  * `SyncComicFileAsync(string filePath, ComicInfo info)`: Locates the book on Komga (with Docker/NAS path translation), triggers a targeted book/series re-analysis, and syncs `<StoryArc>` into a Komga Collection. Returns a `KomgaSyncReport`.
  * `SyncMultipleComicsAsync(IEnumerable<(string FilePath, ComicInfo Info)> files)`: Batch form of the above.
* **`KomgaClient`** (`IDisposable`): `TestConnectionAsync`, `GetLibrariesAsync`, `FindBookByFilePathAsync`, `AnalyzeBookAsync`, `AnalyzeSeriesAsync`, `SyncStoryArcCollectionAsync`, `GetUntaggedOrErrorBooksAsync`, `UpdateSeriesStatusAsync`. Owns its `HttpClient` only when it created it (uses a pooled `SocketsHttpHandler`).

### `ComicFilenameParser.cs` (Smart Filename & Ancestor Path Parsing)
* **Namespace**: `InkTag.Core.Parsing`
* **Method**: `public static ParsedComicFilename Parse(string filenameOrPath, bool inspectParentHierarchy = true)`
* **Description**: Extracts `Series`, `Number`, `Volume`, and `Year` from raw filenames. Interrogates 2-level ancestor directory hierarchies to resolve series names and start years from parent folders (e.g. `/The Avengers (1963)/048.cbz` -> Series: `"The Avengers"`, Issue: `"48"`, Year: `1963`).

### `ComicFileRenamer.cs` (Bulk File Renaming Engine)
* **Namespace**: `InkTag.Core.Renaming`
* **Properties**:
  * `StandardTemplates`: `IReadOnlyList<string>` single source of truth for standard renaming templates across CLI, MCP, and GUI.
* **Methods**:
  * `GenerateFilename(ComicInfo comic, string originalFilePath, string templatePattern, bool preserveScanInfo = false)`: Generates a sanitized, filesystem-safe filename using token replacement (`{Series}`, `#{Number:3}`, `{Year}`, `{Title}`, `{Publisher}`, `{Volume}`, `{ScanInfo}`).
  * `PreviewBatchRename(...)`: Generates batch rename previews with collision detection across the batch and against existing disk files.
  * `RenameFile(...)`: Atomically moves the file to its new name.
  * `ExecuteBatchRename(...)`: Executes a validated rename batch and returns a `RenameBatchResult`.

### `BulkScrapeQueueService.cs` (Parallel Auto-Tag Queue Pipeline)
* **Namespace**: `InkTag.Core.Scrapers`
* **Methods**:
  * `CreateQueue(IEnumerable<string> filePaths)`: Parses filenames and parent directory hierarchies and initializes staged queue items with local cover extractions.
  * `ProcessQueueAsync(...)`: Executes streaming parallel cover hashing, smart series volume clustering, on-demand candidate thumbnail hashing for all matched issues, and volume lifespan confidence scoring.
  * `ApplyMatchedMetadataAsync(...)`: Writes matched ComicVine metadata back to comic archives on disk with automated pre-write backup snapshotting.

### Scraper Networking & Lifecycle
* **`InkTag.Core.Net.SharedHttpClient`**: A process-wide singleton `HttpClient` over a pooled `SocketsHttpHandler` (`PooledConnectionLifetime` 2 min, `AutomaticDecompression`). All outbound scraper and cover-thumbnail traffic flows through it — `RateLimitedHttpClient`, `MetadataScraperService`, and `BulkScrapeQueueService` never allocate their own client. Per-request time limits are enforced by callers with linked `CancellationTokenSource`s. Never disposed.
* **`RateLimitedHttpClient`**: Serializes ComicVine API calls behind a global `SemaphoreSlim` with a ~1.05s minimum interval and exponential backoff on HTTP 420 / 429. Accepts an injected `HttpClient` for tests; otherwise uses `SharedHttpClient.Instance`.
* **`ScraperCacheService`**: Debounced (2s) JSON disk cache for ComicVine responses at `~/.cache/InkTag/scraper_cache.json` (7-day TTL, newest 500 entries persisted). `IDisposable` — `Dispose()` flushes synchronously.
* **`MetadataScraperService` / `ComicVineProvider`**: Both `IDisposable`. Disposal flushes the scraper cache to disk, which is required for one-shot processes (the CLI `scrape` command and the `search_external_metadata` / `scrape_comic_metadata` / `bulk_scrape_directory` MCP tools) that exit before the debounce timer fires. `BulkScrapeQueueService.ProcessQueueAsync` also flushes explicitly at completion.

---

## 5. Interfaces (`InkTag.Mcp` & `InkTag.Cli`)

### Model Context Protocol (`InkTag.Mcp`) Tools (18 Tools)

Full parameter specs live in [cli_mcp.md](cli_mcp.md). Summary:

* **`read_comic_metadata`** (`path`): Reads metadata XML as JSON.
* **`update_comic_metadata`** (`path`, `patch`, `dryRun`, `recursive`): Applies a JSON patch to an archive or folder.
* **`extract_cover_image`** (`path`, `pageIndex`, `outputPath`, `returnBase64`): Extracts a page image (default cover).
* **`remove_comic_page`** (`path`, `pageIndex`, `dryRun`): Removes a page and repacks the archive.
* **`scan_comics`** (`directory`, `missingFields`, `recursive`, `onlyUntagged`): Audits a directory for missing metadata.
* **`search_external_metadata`** (`series`, `issueNumber`, `year`, `apiKey`): ComicVine candidate search.
* **`scrape_comic_metadata`** (`path`, `mode`, `coverPageIndex`, `detectIntroPage`, `stripIntroPage`, `dryRun`, `apiKey`): Scrapes and applies metadata to one archive.
* **`bulk_scrape_directory`** (`directory`, `mode`, `detectIntroPage`, `stripIntroPages`, `dryRun`, `recursive`, `apiKey`): Parallel auto-tag queue.
* **`rename_comic_files`** (`path`, `template`, `preserveScanInfo`, `dryRun`, `recursive`): Metadata-driven file renaming.
* **`get_comic_schema`**: Returns the JSON Schema for `ComicInfo`.
* **`list_metadata_backups`** (`path`, `limit`) / **`get_backup_provenance`** (`backupId`): Backup history & forensic detail.
* **`restore_comic_backup`** (`path`, `backupId`): Restores one archive's `ComicInfo.xml` from a snapshot.
* **`list_batch_jobs`** (`limit`) / **`restore_batch_job`** (`batchJobId`): Multi-file batch history & rollback (best-effort per file).
* **`check_komga_server`** (`serverUrl`, `apiKey`) / **`sync_komga_book_or_series`** (`path`, `storyArc`) / **`audit_komga_library`** (`libraryId`): Komga connectivity, targeted sync, and UNSUPPORTED/ERROR book audit.

All mutating tools default to `dryRun = true` and are blocked entirely under `INKTAG_MCP_READ_ONLY`.

### Agentic CLI (`InkTag.Cli`) Subcommands
* **`read <file> [--json]`**: Read metadata from an archive.
* **`update <path> --patch '<json>' [--dry-run] [--recursive] [--json]`**: Update file or directory metadata.
* **`rename <path> [--template '<pattern>'] [--strip-scan-info] [--dry-run] [--recursive] [--json]`**: Rename archives from metadata.
* **`scan <directory> [--untagged] [--missing Writer,Series] [--recursive] [--json]`**: Scan directory for incomplete tags.
* **`cover <file> [--page <n>] [--output <path>] [--json]`**: Extract a cover or page image.
* **`scrape <path> [--api-key KEY] [--mode fill-missing|overwrite] [--cover-page <n>] [--strip-intro-page] [--dry-run] [--recursive] [--json]`**: Auto-tag from ComicVine.
* **`strip-intro <file|dir> [--recursive] [--dry-run] [--json]`**: Strip the first (provider/scanner) page.
* **`remove-page <file> --index <n> [--dry-run] [--json]`**: Remove a specific page by 0-based index.
* **`schema [--json]`**: Export the `ComicInfo` JSON schema.


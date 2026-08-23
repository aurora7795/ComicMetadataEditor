# Core Metadata Editor API

This page documents the API of the `InkTag.Core` domain library, along with the agent-facing CLI (`InkTag.Cli`) and MCP (`InkTag.Mcp`) interfaces.

---

## 1. `ComicInfo.cs` (Data Model)
The `ComicInfo` class corresponds to the standard XML schema (`ComicInfo.xml`) used by major comic readers (e.g., ComicRack, YACReader).

### Fields & Schema Schema Properties
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
* **Signature**: `public void EditMetadataFromJson(string filePath, string jsonPatch, string? batchJobId = null, string? changeReason = null)`
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
* **Signature**: `public string? ExtractCoverImage(string comicFilePath, string outputFilePath)`
* **Signature**: `public byte[]? ExtractCoverImageBytes(string filePath)`
* **Signature**: `public Task<byte[]?> ExtractCoverImageBytesAsync(string filePath, CancellationToken cancellationToken = default)`
* **Signature**: `public ulong GetCoverHash(string filePath)`
* **Signature**: `public Task<ulong> GetCoverHashAsync(string filePath, CancellationToken cancellationToken = default)`
* **Description**: Extracts front cover art or first page image from a `.cbz` or `.cbr` archive in-memory via stream decoding, or computes 64-bit perceptual dHash.

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
  * `CreateBackup(string filePath, string? currentXmlContent, byte[]? coverBytes, string? batchJobId, string? changeReason, ...)`: Creates a timestamped pre-write snapshot of `ComicInfo.xml` with source SHA-256 and cover dHash in `~/.local/share/InkTag/backups/`.
  * `RestoreBackup(string filePath, string timestamp)`: Restores an archive's metadata to a specified snapshot.
  * `ListBackups(string? filePath = null)`: Returns snapshot history for a file or the entire system.
  * `ListBatchJobs()`: Lists all recorded multi-file batch operations.
  * `RestoreBatchJob(string batchJobId)`: Atomically rolls back all files modified in a multi-file batch.
  * `GetProvenance(string filePath, string timestamp)`: Retrieves forensic audit metadata for a snapshot.

### `PerceptualHashService.cs` (Perceptual Image Hashing & dHash)
* **Namespace**: `InkTag.Core.Images`
* **Methods**:
  * `ComputeDHash(ReadOnlySpan<byte> imageBytes)`: Computes a 64-bit difference hash (dHash) by downscaling image bytes to 9×8 grayscale and comparing horizontal gradient intensities.
  * `CalculateSimilarity(ulong hashA, ulong hashB)`: Returns visual similarity score (0.0 to 1.0) based on Hamming distance.
  * `IsVisualMatch(ulong hashA, ulong hashB, double threshold = 0.90)`: Fast boolean match check.

### `ComicBookInfoParser.cs` (Legacy CBI Ingestion)
* **Namespace**: `InkTag.Core.Parsing`
* **Methods**:
  * `TryParse(string jsonOrComment, out ComicInfo comic)`: Parses legacy ComicBookInfo JSON embedded in zip archive comments and maps it to modern `ComicInfo` schema properties.

### `KomgaSyncService.cs` & `KomgaClient.cs` (Media Server Integration)
* **Namespace**: `InkTag.Core.Komga`
* **Methods**:
  * `TestConnectionAsync()`: Verifies connectivity and authentication with self-hosted Komga servers.
  * `SyncComicsAsync(IEnumerable<string> filePaths, KomgaSyncOptions options)`: Analyzes remote Komga book metadata and synchronizes `<StoryArc>` and `<SeriesGroup>` into Komga Collections with Docker/NAS path translation.

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

---

## 5. Interfaces (`InkTag.Mcp` & `InkTag.Cli`)

### Model Context Protocol (`InkTag.Mcp`) Tools (14 Tools)
* **`read_comic_metadata`**: Reads metadata XML as JSON object (`path`).
* **`update_comic_metadata`**: Applies JSON patch to archive or folder (`path`, `patch`, `dryRun`).
* **`extract_cover_image`**: Unpacks front cover art (`path`, `outputPath`, `returnBase64`).
* **`bulk_scrape_directory`**: Parallel auto-tag queue on directory with volume clustering and visual matching (`directory`, `mode`, `dryRun`).
* **`scrape_comic_metadata`**: Scrapes and applies metadata from ComicVine to a single local comic archive (`path`, `mode`, `dryRun`).
* **`rename_comic_files`**: Renames comic archives on disk using configurable metadata templates (`path`, `template`, `preserveScanInfo`, `dryRun`).
* **`scan_comics`**: Scans directory for archives missing specified metadata tags (`directory`, `missingFields`, `onlyUntagged`).
* **`get_comic_schema`**: Returns JSON Schema for `ComicInfo`.
* **`search_external_metadata`**: Searches ComicVine issues matching series, issue, and year (`series`, `issueNumber`, `year`).
* **`list_metadata_backups`**: Lists pre-write metadata backups (`path`).
* **`restore_comic_backup`**: Restores archive metadata to a timestamped snapshot (`path`, `timestamp`).
* **`list_batch_jobs`**: Lists multi-file batch operations available for rollback.
* **`restore_batch_job`**: Atomically rolls back an entire multi-file batch job (`batchJobId`).
* **`get_backup_provenance`**: Retrieves deep forensic provenance for a snapshot (`path`, `timestamp`).

### Agentic CLI (`InkTag.Cli`) Subcommands
* **`read <file-path> [--json]`**: Read metadata from an archive.
* **`update <path> --patch '<json>' [--dry-run] [--json]`**: Update file or directory metadata.
* **`rename <path> [--template '<pattern>'] [--preserve-scans] [--dry-run] [--json]`**: Rename comic archives based on metadata.
* **`scan <directory-path> [--missing Writer,Series] [--json]`**: Scan directory for incomplete tags.
* **`cover <file-path> [--output <path>] [--json]`**: Extract cover art.
* **`scrape <path> [--api-key KEY] [--mode fill-missing|overwrite] [--dry-run] [--json]`**: Auto-tag metadata from ComicVine.
* **`schema [--json]`**: Export `ComicInfo` JSON schema.


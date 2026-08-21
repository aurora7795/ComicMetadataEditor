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

#### `ReadMetadata` / `ReadMetadataAsJson`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, CancellationToken cancellationToken = default)`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, CancellationToken cancellationToken = default)`
* **Signature**: `public ComicInfo ReadMetadata(string filePath, out bool hasEmbeddedXml, out bool usedSequentialFallback, CancellationToken cancellationToken = default)`
* **Signature**: `public string ReadMetadataAsJson(string filePath)`
* **Description**: Parses `ComicInfo.xml` directly in-memory with zero temporary disk extraction.
  1. *Fast-Path (.cbz)*: Reads the Central Directory using .NET's built-in `System.IO.Compression.ZipArchive` for 1-seek metadata access.
  2. *Sequential Forward Fallback*: If backward seeks fail on virtual mounts (e.g. Linux GVFS FTP / FUSE), wraps the stream in a `NonSeekableStream` with `CancellationToken` checks, reading local headers forward from byte 0.
  3. *Fallback & .cbr (RAR)*: Reads via SharpCompress `ArchiveFactory.OpenArchive(stream, new ReaderOptions { LookForHeader = true })` to safely recover metadata across high-latency network shares.

#### `EditMetadata` / `EditMetadataFromJson`
* **Signature**: `public void EditMetadata(string filePath, Action<ComicInfo> editAction)`
* **Signature**: `public void EditMetadataFromJson(string filePath, string jsonPatch)`
* **Description**: Unpacks the file into a temporary folder, deserializes existing metadata or creates a new instance, applies edits (via lambda or dynamic JSON patch), serializes back to XML, compresses to `.tmp`, validates, and performs an atomic backup swap.

#### `BulkEditMetadata` / `BulkEditMetadataFromJson`
* **Signature**: `public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)`
* **Signature**: `public BulkEditReport BulkEditMetadataFromJson(string directoryPath, string jsonPatch)`
* **Description**: Executes metadata edits on all `.cbz`/`.cbr` archives in a directory, catching individual errors and returning a `BulkEditReport`.

#### `GetMetadataDiff`
* **Signature**: `public List<MetadataDiffItem> GetMetadataDiff(string filePath, string jsonPatch)`
* **Description**: Previews property-level before/after diffs between the archive's current metadata and a proposed JSON patch.

#### `ApplyJsonPatch`
* **Signature**: `public static void ApplyJsonPatch(ComicInfo comicInfo, string jsonPatch)`
* **Description**: Mutates a `ComicInfo` instance in-place by parsing property key-values from a JSON patch string.

#### `ExtractCoverImage` / `ExtractCoverImageBytes`
* **Signature**: `public string? ExtractCoverImage(string comicFilePath, string outputFilePath)`
* **Signature**: `public byte[]? ExtractCoverImageBytes(string filePath)`
* **Description**: Extracts front cover art or first page image from a `.cbz` or `.cbr` archive in-memory via stream decoding.

#### `IsSupportedComicFile`
* **Signature**: `public static bool IsSupportedComicFile(string? filePath)`
* **Description**: Checks whether a given path is a valid comic archive (`.cbz` or `.cbr`), filtering out macOS resource forks (`._*`), AppleDouble folders (`.AppleDouble`), `__MACOSX`, `.git`, `.Trash`, and hidden system files.

#### `ExportJsonSchema`
* **Signature**: `public static string ExportJsonSchema()`
* **Description**: Returns the JSON Schema specification for `ComicInfo` objects.

---

## 3. Supplementary Core Services

### `ImageHasher.cs` (Perceptual Image Hashing)
* **Namespace**: `InkTag.Core`
* **Method**: `public static ulong ComputeDHash64(ReadOnlySpan<byte> imageBytes)`
* **Method**: `public static int HammingDistance(ulong hashA, ulong hashB)`
* **Description**: Computes a 64-bit difference hash (dHash) by downscaling image bytes to 9×8 grayscale and comparing horizontal gradient intensities. Used for cover deduplication and visual matching against scraper candidates.

### `ComicFilenameParser.cs` (Smart Filename Parsing)
* **Namespace**: `InkTag.Core.Parsing`
* **Method**: `public static ParsedComicFilename Parse(string filenameOrPath, bool inspectParentHierarchy = true)`
* **Description**: Extracts `Series`, `Number` (including attached acronym numbers like `IM015` -> issue `15`, alphanumeric/decimal/annual issues like `#005`, `1.5`, `Annual #1`), `Volume`, and `Year` from raw filenames. Interrogates parent and grandparent directory hierarchies to resolve series names from abbreviations/initials (e.g. `/iron man/IM015.cbz` -> Series: `"Iron Man"`, Issue: `"15"`).

### `ComicFileRenamer.cs` (Bulk File Renaming Engine)
* **Namespace**: `InkTag.Core.Renaming`
* **Properties**:
  * `StandardTemplates`: `IReadOnlyList<string>` single source of truth for standard renaming templates across CLI, MCP, and GUI.
* **Methods**:
  * `GenerateFilename(ComicInfo comic, string originalFilePath, string templatePattern, bool preserveScanInfo = false)`: Generates a sanitized, filesystem-safe filename using token replacement (`{Series}`, `#{Number:3}`, `{Year}`, `{Title}`, `{Publisher}`, `{Volume}`, `{ScanInfo}`). Automatically clears scanner/release tags by default unless `{ScanInfo}` is explicitly requested.
  * `PreviewBatchRename(IEnumerable<(string FilePath, ComicInfo Comic)> items, string templatePattern, bool preserveScanInfo = false)`: Generates batch rename previews with collision detection across the batch and against existing disk files.
  * `RenameFile(string originalFilePath, string newFilename, bool overwrite = false)`: Atomically moves the file to its new name.
  * `ExecuteBatchRename(IEnumerable<RenameItemPreview> items, bool overwrite = false)`: Executes a validated rename batch and returns a `RenameBatchResult`.

### `AppLogger.cs` (Structured Diagnostics & Rotation)
* **Namespace**: `InkTag.Core.Logging`
* **Description**: Cross-platform thread-safe diagnostic logger with automatic 5 MB file size rotation (`InkTag.log.bak`), formatted timestamp output, and system file manager reveal.

### `BulkScrapeQueueService.cs` (Parallel Auto-Tag Queue Pipeline)
* **Namespace**: `InkTag.Core.Scrapers`
* **Methods**:
  * `CreateQueue(IEnumerable<string> filePaths)`: Parses filenames and parent directory hierarchies (handling untagged files or short acronyms like `/Iron Man/IM015.cbz` -> Series: `"Iron Man"`) and initializes staged queue items with local cover extractions.
  * `ProcessQueueAsync(IEnumerable<BulkScrapeQueueItem> queue, BulkScrapeOptions options, ...)`: Executes streaming parallel cover hashing, smart series volume clustering, chronological ComicVine matching, and perceptual visual similarity calculation.
  * `ApplyMatchedMetadataAsync(IEnumerable<BulkScrapeQueueItem> items, ScrapeMergeMode mergeMode, bool renameFiles, string renameTemplate, bool preserveScanInfo, ...)`: Writes matched ComicVine metadata back to comic archives on disk, with optional safe auto-renaming.

---

## 4. Interfaces (`InkTag.Mcp` & `InkTag.Cli`)

### Model Context Protocol (`InkTag.Mcp`) Tools
* **`read_comic_metadata`**: Reads metadata XML as JSON object (`path`).
* **`update_comic_metadata`**: Applies JSON patch to archive or folder (`path`, `patch`, `dryRun`).
* **`extract_cover_image`**: Unpacks front cover art (`path`, `outputPath`, `returnBase64`).
* **`bulk_scrape_directory`**: Parallel auto-tag queue on directory with volume clustering and visual matching.
* **`rename_comic_files`**: Renames comic archives on disk using configurable metadata templates.
* **`scan_comics`**: Scans directory for archives missing specified metadata tags (`directory`, `missingFields`).
* **`get_comic_schema`**: Returns JSON Schema for `ComicInfo`.
* **`search_external_metadata`**: Searches ComicVine issues matching series, issue, and year.
* **`scrape_comic_metadata`**: Scrapes and applies metadata from ComicVine to a local comic archive.

### Agentic CLI (`InkTag.Cli`) Subcommands
* **`read <file-path> [--json]`**: Read metadata from an archive.
* **`update <path> --patch '<json>' [--dry-run] [--json]`**: Update file or directory metadata.
* **`rename <path> [--template '<pattern>'] [--preserve-scans] [--dry-run] [--json]`**: Rename comic archives based on metadata.
* **`scan <directory-path> [--missing Writer,Series] [--json]`**: Scan directory for incomplete tags.
* **`cover <file-path> [--output <path>] [--json]`**: Extract cover art.
* **`scrape <path> [--api-key KEY] [--mode fill-missing|overwrite] [--dry-run] [--json]`**: Auto-tag metadata from ComicVine.
* **`schema [--json]`**: Export `ComicInfo` JSON schema.


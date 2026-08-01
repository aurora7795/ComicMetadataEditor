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
| `Manga` | `<Manga>` | `string?` | `"Yes"` (right-to-left) or `"No"` |
| `Characters` | `<Characters>` | `string?` | Featured characters |
| `Teams` | `<Teams>` | `string?` | Featured super teams |
| `Locations` | `<Locations>` | `string?` | Featured story locations |
| `ScanInformation` | `<ScanInformation>` | `string?` | Scanner / release group details |
| `StoryArc` | `<StoryArc>` | `string?` | Story arc name |
| `SeriesGroup` | `<SeriesGroup>` | `string?` | Series group designation |
| `AgeRating` | `<AgeRating>` | `string?` | Content age rating |
| `Pages` | `<Pages>` | `PageCollection?` | Structured page-level array (`Page[]`) |

---

## 2. `MetadataEditor.cs` (Engine)

The core engine handles loading, modifying, dynamic JSON patching, cover extraction, and safely writing XML metadata back into compressed CBZ/CBR archives.

### Core & Agent API Method Signatures

#### `ReadMetadata` / `ReadMetadataAsJson`
* **Signature**: `public ComicInfo ReadMetadata(string filePath)`
* **Signature**: `public string ReadMetadataAsJson(string filePath)`
* **Description**: Parses `ComicInfo.xml` from the target `.cbz` or `.cbr` (RAR format) archive using random-access `ArchiveFactory.OpenArchive(stream)`, validates it against `ComicInfo.xsd`, and returns the `ComicInfo` object or its JSON representation.

#### `EditMetadata` / `EditMetadataFromJson`
* **Signature**: `public void EditMetadata(string filePath, Action<ComicInfo> editAction)`
* **Signature**: `public void EditMetadataFromJson(string filePath, string jsonPatch)`
* **Description**: Unpacks the file using random-access `ArchiveFactory.OpenArchive(stream)`, deserializes existing metadata or creates a new instance, applies edits (via lambda or dynamic JSON patch), serializes back to XML, compresses to `.tmp`, validates, and performs an atomic backup swap.

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

#### `ExtractCoverImage`
* **Signature**: `public string? ExtractCoverImage(string comicFilePath, string outputFilePath)`
* **Description**: Extracts the front cover image or first page image from a `.cbz` or `.cbr` (RAR format) archive using random-access `ArchiveFactory.OpenArchive(stream)` for visual inspection.

#### `ExportJsonSchema`
* **Signature**: `public static string ExportJsonSchema()`
* **Description**: Returns the JSON Schema specification for `ComicInfo` objects.

---

## 3. Interfaces (`InkTag.Mcp` & `InkTag.Cli`)

### Model Context Protocol (`InkTag.Mcp`) Tools
* **`read_comic_metadata`**: Reads metadata XML as JSON object (`path`).
* **`update_comic_metadata`**: Applies JSON patch to archive or folder (`path`, `patch`, `dryRun`).
* **`extract_cover_image`**: Unpacks front cover art (`path`, `outputPath`, `returnBase64`).
* **`scan_comics`**: Scans directory for archives missing specified metadata tags (`directory`, `missingFields`).
* **`get_comic_schema`**: Returns JSON Schema for `ComicInfo`.

### Agentic CLI (`InkTag.Cli`) Subcommands
* **`read <file-path> [--json]`**: Read metadata from an archive.
* **`update <path> --patch '<json>' [--dry-run] [--json]`**: Update file or directory metadata.
* **`scan <directory-path> [--missing Writer,Series] [--json]`**: Scan directory for incomplete tags.
* **`cover <file-path> [--output <path>] [--json]`**: Extract cover art.
* **`schema [--json]`**: Export `ComicInfo` JSON schema.


# Core Metadata Editor API

This page documents the API of the `InkTag.Core` domain library.

---

## 1. `ComicInfo.cs` (Data Model)
The `ComicInfo` class corresponds to the standard XML schema (`ComicInfo.xml`) used by major comic readers (e.g., ComicRack, YACReader).

### Key Fields & Types
Fields are nullable to avoid writing default XML values when optional tags are omitted:

| Property Name | XML Tag | Data Type | Notes |
| :--- | :--- | :--- | :--- |
| `Title` | `<Title>` | `string?` | Title of the issue |
| `Series` | `<Series>` | `string?` | Name of the series |
| `Number` | `<Number>` | `string?` | Issue number (supports alphanumeric like "1.5") |
| `Volume` | `<Volume>` | `int?` | Series volume number |
| `Summary` | `<Summary>` | `string?` | Story synopsis |
| `Publisher` | `<Publisher>` | `string?` | Publishing house (e.g., Marvel, DC) |
| `Year` | `<Year>` | `int?` | Publication year (4-digit) |
| `Month` | `<Month>` | `int?` | Publication month (1-12) |
| `Genre` | `<Genre>` | `string?` | Comic genre tags |
| `Tags` | `<Tags>` | `string?` | User/Archival keywords |
| `Writer` | `<Writer>` | `string?` | Writer credits |
| `LanguageISO` | `<LanguageISO>` | `string?` | Language code (e.g. "en", "ja") |
| `Manga` | `<Manga>` | `string?` | `"Yes"` indicates right-to-left orientation, `"No"` otherwise |

---

## 2. `MetadataEditor.cs` (Engine)

The core engine handles loading, modifying, dynamic JSON patching, cover extraction, and safely writing XML metadata back into compressed CBZ/CBR archives.

### Core & Agent API Method Signatures

#### `ReadMetadata` / `ReadMetadataAsJson`
* **Signature**: `public ComicInfo ReadMetadata(string filePath)`
* **Signature**: `public string ReadMetadataAsJson(string filePath)`
* **Description**: Extracts `ComicInfo.xml` from the target archive into a temporary folder, validates it against `ComicInfo.xsd`, and returns the `ComicInfo` object or its JSON representation.

#### `EditMetadata` / `EditMetadataFromJson`
* **Signature**: `public void EditMetadata(string filePath, Action<ComicInfo> editAction)`
* **Signature**: `public void EditMetadataFromJson(string filePath, string jsonPatch)`
* **Description**: Unpacks the file, deserializes existing metadata or creates a new instance, applies edits (via lambda or dynamic JSON patch), serializes back to XML, compresses to `.tmp`, validates, and performs an atomic backup swap.

#### `BulkEditMetadata` / `BulkEditMetadataFromJson`
* **Signature**: `public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)`
* **Signature**: `public BulkEditReport BulkEditMetadataFromJson(string directoryPath, string jsonPatch)`
* **Description**: Executes metadata edits on all `.cbz`/`.cbr` archives in a directory, catching individual errors and returning a `BulkEditReport`.

#### `GetMetadataDiff`
* **Signature**: `public List<MetadataDiffItem> GetMetadataDiff(string filePath, string jsonPatch)`
* **Description**: Previews property-level before/after diffs between the archive's current metadata and a proposed JSON patch.

#### `ExtractCoverImage`
* **Signature**: `public string? ExtractCoverImage(string comicFilePath, string outputFilePath)`
* **Description**: Extracts the front cover image or first page image from a `.cbz` or `.cbr` archive for visual inspection.

#### `ExportJsonSchema`
* **Signature**: `public static string ExportJsonSchema()`
* **Description**: Returns the JSON Schema specification for `ComicInfo` objects.

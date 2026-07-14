# Core Metadata Editor API

This page documents the API of the `ComicMetadataEditor` core project library.

---

## 1. `ComicInfo.cs` (Data Model)
The `ComicInfo` class corresponds to the standard XML schema (`ComicInfo.xml`) used by major comic readers (e.g., ComicRack, YACReader).

### Key Fields & Types
All fields are nullable to avoid writing default XML values when optional tags are omitted:

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

The core engine handles loading, modifying, and safely writing XML metadata back into compressed CBZ/CBR archives.

### Method Signatures

#### `ReadMetadata`
* **Signature**: `public ComicInfo ReadMetadata(string filePath)`
* **Description**: Extracts only `ComicInfo.xml` from the target archive into a temporary folder, validates it against the schema (`ComicInfo.xsd`), deserializes it to a `ComicInfo` object, and returns it. If `ComicInfo.xml` is missing from the archive, returns a new empty `ComicInfo` instance.

#### `EditMetadata`
* **Signature**: `public void EditMetadata(string filePath, Action<ComicInfo> editAction)`
* **Description**: Performs a single file edit. It unpacks the file, checks for `ComicInfo.xml` (deserializing it or initializing a new instance), executes the `editAction` lambda to modify the properties, serializes back to XML, compresses the temporary path to a `.tmp` file, validates it, and performs a safe backup swap.

#### `BulkEditMetadata`
* **Signature**: `public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)`
* **Description**: Discovers all `.cbz`/`.cbr` archives in the specified folder, executes `EditMetadata` sequentially on them, catches individual errors to prevent halting the entire batch, and generates a structured `BulkEditReport`.

```csharp
public class BulkEditReport
{
    public int TotalFound { get; set; }
    public List<string> Successes { get; } = new();
    public List<(string Path, Exception Exception)> Failures { get; } = new();
}
```

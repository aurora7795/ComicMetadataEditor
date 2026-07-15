# ComicMetadataEditor

A lightweight, robust C# utility and console tool designed to bulk-edit `ComicInfo.xml` metadata embedded inside comic book archives (`.cbz` and `.cbr`). 

The project contains a core class library (`ComicMetadataEditor`), a CLI utility (`ComicEditorConsole`), and an upcoming desktop application interface (`AvaloniaApp`).

---

## 🚀 Key Features

* **Dual-Format Processing:** Supports reading both `.cbz` (ZIP-based) and `.cbr` (RAR/RAR-like) archives.
* **Safe Replacement Strategy:** Minimizes the risk of data loss or corruption by repacking to a temporary archive, validating readability, making a `.bak` backup, swapping target paths, and cleaning up only on complete success.
* **Metadata Schema Validation:** Validates `ComicInfo.xml` files against the official schema (`ComicInfo.xsd`) using .NET's `XmlReader` before processing.
* **Error Resilience:** Individual file errors during bulk operations are caught and logged, allowing the rest of the batch to complete successfully.
* **Detailed Reporting:** Generates a structured `BulkEditReport` tracking successful operations, failures, and error stack traces.

---

## 📁 Repository Structure

```text
ComicMetadataEditor/
├── ComicMetadataEditor/         # Core Library (targets .NET 10.0)
│   ├── Schema/
│   │   └── ComicInfo.xsd        # XML schema definition for validation
│   ├── ComicInfo.cs             # Deserialization model (nullable properties)
│   └── MetadataEditor.cs        # Core bulk-editing & repackaging logic
│
├── ComicEditorConsole/          # CLI Application (targets .NET 10.0)
│   ├── Program.cs               # Scans directory and runs bulk edits
│   └── test_comics/             # Sample comics for testing validation
│
├── AvaloniaApp/                 # Cross-platform Desktop App (Completed)
│   ├── App.axaml
│   ├── App.axaml.cs
│   ├── Program.cs
│   ├── Converters/
│   ├── Services/
│   ├── ViewModels/
│   ├── Views/
│   └── AvaloniaApp.csproj
│
└── docs/                        # Project reports and review logs
    └── code-review-report.md    # Active quality assurance documentation
```

---

## 🛠️ Getting Started

### Prerequisites
* [.NET SDK 8.0 / 9.0 / 10.0](https://dotnet.microsoft.com/download)

### Building the Project
From the repository root, compile the entire solution:
```bash
dotnet build
```

### Running the Console Application
To execute bulk edits in a specific directory (by default, it sets the `Manga` property to `"No"`), run:
```bash
dotnet run --project ComicEditorConsole/ComicEditorConsole.csproj -- /path/to/your/comics
```
If no directory path is provided, it defaults to the current working directory.

### Running the Desktop Application
To launch the graphical bulk metadata editor, run:
```bash
dotnet run --project AvaloniaApp/AvaloniaApp.csproj
```

---

## 💻 Library Usage Example

You can integrate the core editor into your own projects by referencing `ComicMetadataEditor.csproj`.

```csharp
using ComicMetadataEditor;

var editor = new MetadataEditor();

// Perform bulk edits
BulkEditReport report = editor.BulkEditMetadata("/path/to/comics", comic =>
{
    // Modify metadata properties safely
    comic.Publisher = "Marvel";
    comic.Series = "The Amazing Spider-Man";
    comic.LanguageISO = "en";
    
    // Optional properties are nullable to avoid writing default XML values
    comic.Year = 2026;
    comic.Month = 7;
});

// Review the report summary
Console.WriteLine($"Discovered: {report.TotalFound}");
Console.WriteLine($"Succeeded: {report.Successes.Count}");
Console.WriteLine($"Failed: {report.Failures.Count}");

foreach (var failure in report.Failures)
{
    Console.WriteLine($"Error on {failure.Path}: {failure.Exception.Message}");
}
```

---

## ⚙️ Technical Details & Design Choices

### Upgraded Archive Handler
Uses the latest patched `SharpCompress` (`0.48.0`) to read and write archives, securing the application against directory traversal vulnerabilities.

### Atomic-Like File Swapping
When editing a file:
1. Contents are extracted to a unique GUID-named directory.
2. `ComicInfo.xml` is modified (or created) and validated against the XSD schema.
3. The folder is repacked to a temporary `.cbz.tmp` archive.
4. The `.cbz.tmp` archive is verified to ensure it is readable and contains files.
5. If the target path exists, it is renamed to `.bak`.
6. The new archive is moved into the target slot.
7. Backups are deleted. On failure, a rollback function automatically restores original files.

---

## 🗺️ Roadmap
* **[x] Fix XSD validation edge case:** Handled gracefully on missing metadata files and converted XML schema warnings to non-fatal logs.
* **[x] Complete Avalonia Desktop App:** Implemented a modern spreadsheet grid, side panels, lazy-loaded cover thumbs, validation, find-replace, and safe background saving.
* **Enhance nested models:** Adjust properties on the sub-class `Page` in `ComicInfo.cs` to be nullable, preventing default numeric values from serializing into the output XML.

# Technical Specification: Comic Metadata Editor (Avalonia App)

This document provides a highly detailed, file-by-file blueprint for implementing a cross-platform desktop application using Avalonia UI and MVVM. This specification is designed to be passed to an LLM to generate the entire implementation.

---

## 1. Executive Summary & Goals
* **Goal**: Provide a fast, reliable, and user-friendly desktop application to view and bulk-edit `ComicInfo.xml` metadata files embedded inside comic archives (`.cbz` and `.cbr`).
* **Core Library Re-use**: The app MUST wrap and leverage the existing core library (`ComicMetadataEditor` / [ComicInfo.cs](file:///home/aurora7795/AntiGravProjects/ComicMetadataEditor/ComicMetadataEditor/ComicInfo.cs) and [MetadataEditor.cs](file:///home/aurora7795/AntiGravProjects/ComicMetadataEditor/ComicMetadataEditor/MetadataEditor.cs)).
* **Target Audience**: Comic readers, archivists, and catalogers managing local collections.

---

## 2. Technology Stack & Architecture
* **Target Framework**: `.NET 10.0` (matching the core library/console app).
* **UI Framework**: [Avalonia UI](https://avaloniaui.net/) (v11.0.0+).
* **Design Pattern**: Model-View-ViewModel (MVVM) using `CommunityToolkit.Mvvm` (using source generators for properties and commands).
* **Dependencies**:
  * `CommunityToolkit.Mvvm` (v8.2.0+)
  * `Avalonia` (v11.0.0+)
  * `Avalonia.Themes.Fluent` (v11.0.0+)
  * Project reference to `ComicMetadataEditor`
* **Theming**: Fluent Theme (Dark mode default).

---

## 3. UI Layout & User Experience
* **Layout Design**: Hybrid Layout (split screen / side panel).
  * **Top Toolbar**: 
    * *Open Folder* button (launches directory picker).
    * *Scan Subfolders recursively* checkbox (default: unchecked).
    * *Save All* button (saves dirty items to disk).
    * *Export CSV* button (saves current grid view to a `.csv` file).
    * Loading / Saving progress bar (displayed in toolbar or status bar).
  * **Main Left Panel**: A Virtualized DataGrid showing a spreadsheet-style view of comic files. Cells are inline-editable for rapid correction.
    * **Visible Columns (by default)**:
      1. **File Name** (string, read-only)
      2. **Title** (string, editable)
      3. **Series** (string, editable)
      4. **Number** (string, editable - representing issue number)
      5. **Volume** (integer, editable)
      6. **Publisher** (string, editable)
      7. **Year** (integer, editable)
      8. **Genre** (string, editable)
      9. **Tags** (string, editable)
      10. **Writer** (string, editable)
      11. **LanguageISO** (string, editable)
      12. **Manga / Orientation** (string/checkbox, editable - specifies if read right-to-left. Corresponds to `Manga` property in `ComicInfo`)
  * **Main Right Panel (Sidebar)**: 
    * **Detail Viewer / Editor**: Shows details of the currently active/selected single comic. Displays a prominent **Cover Thumbnail** (extracted from the first image of the archive) at the top of the sidebar.
    * **Bulk Edit Tools**: Panel containing metadata input fields with checkbox toggles (to enable applying only specific fields) and an "Apply to Selected" button.
    * **Find & Replace Tool**: Fields to choose the target column, search string, replace string, and a button to run the find & replace batch.
* **Cover Thumbnails**: Extracted dynamically from the archive using a background task. Extracted image is cached in memory (associated with the `ComicItemViewModel`) to ensure instant rendering during selection changes.

---

## 4. Feature Requirements & Interactive Flows
* **Scanning & Enforcing Scope**:
  * Scan directory selected by user.
  * Checkbox toggle "Scan Subfolders recursively".
  * Identify `.cbz` (zip) and `.cbr` (rar) files. Ignore other files/folders.
* **Inline Spreadsheet Editing**:
  * Users can double-click cells in the DataGrid to modify individual values.
  * Changes are tracked in memory (`ComicItemViewModel.IsDirty = true`) and cell/row styling indicates unsaved state.
* **Bulk Edit Panel Flow**:
  * Select multiple rows in the DataGrid.
  * In the sidebar, select target metadata fields, type the new values, and click "Apply to Selected".
  * Changes propagate in memory, marking the target view models as dirty.
* **Find & Replace Flow**:
  * Specify target field (dropdown of text columns or "All Text Fields").
  * Type search query and replacement text.
  * Execute on selected rows to modify text inline.
* **Save Workflow**:
  * Executed in the background to prevent freezing the UI.
  * Runs repacking asynchronously using `MetadataEditor` from the core library.
  * **Safe File Operations**: Temporary `.tmp` archive is created, verified, and swapped.
  * **Backup Policy**: Original backups (`.bak` files) are automatically cleaned up (deleted) after successful repackaging.
  * **Progress Tracking**: Status bar shows a progress bar and text (e.g., "Saving: 4/12 comics...") allowing the user to browse or edit other cells while saving.
* **Error Handling & Reporting**:
  * **Grid Markers**: Any row that fails to save is flagged with a red background/indicator, and hovering over the row displays a tooltip with the specific exception/error message.
  * **Summary Popup**: When the batch save finishes, if there were any failures, a dialog displays a list of failed files along with their reasons/errors.

---

## 5. Detailed Component & Class Definitions

The Avalonia project is located in [AvaloniaApp/](file:///home/aurora7795/AntiGravProjects/ComicMetadataEditor/AvaloniaApp) and relies on `CommunityToolkit.Mvvm`. Below is the file-by-file structure and interface specification.

### 5.1 Project Structure Overview
```text
AvaloniaApp/
├── AvaloniaApp.csproj
├── App.axaml
├── App.axaml.cs
├── Program.cs
├── Models/
│   └── FieldDefinition.cs          # Helper for dropdown lists and mapping
├── Services/
│   ├── ComicScannerService.cs      # Handles CBZ/CBR scanning
│   └── ArchiveCoverService.cs      # Performs asynchronous lazy cover extraction
├── ViewModels/
│   ├── ViewModelBase.cs            # Root ViewModel utilizing CommunityToolkit.Mvvm
│   ├── MainWindowViewModel.cs      # Main logic, commands, bulk edit, and state
│   └── ComicItemViewModel.cs       # Wraps ComicInfo model and implements ObservableValidator
└── Views/
    ├── MainWindow.axaml            # Layout: Toolbar, Grid, Sidebar Panel
    ├── MainWindow.axaml.cs         # Window logic, folder picker, close guard
    └── ErrorSummaryWindow.axaml    # Modal popup detailing batch save failures
```

---

### 5.2 Model & Service Specifications

#### `Services/ComicScannerService.cs`
* **Purpose**: Scans directories for `.cbz` and `.cbr` archives, loading their metadata into view models.
* **Signature**:
  ```csharp
  public class ComicScannerService
  {
      public Task<List<ComicItemViewModel>> ScanDirectoryAsync(
          string directoryPath, 
          bool recursive, 
          CancellationToken cancellationToken);
  }
  ```
* **Implementation Details**:
  * Enumerate files filtering by `.cbz` and `.cbr` (case-insensitive).
  * Instantiate the core library's `MetadataEditor`.
  * For each file, read metadata safely. If a file lacks a `ComicInfo.xml`, instantiate an empty `ComicInfo`.
  * Instantiate and return `ComicItemViewModel` for each file.

#### `Services/ArchiveCoverService.cs`
* **Purpose**: Performs safe background cover extraction with in-memory caching.
* **Signature**:
  ```csharp
  public class ArchiveCoverService
  {
      private readonly ConcurrentDictionary<string, Bitmap> _coverCache = new();

      public Task<Bitmap?> LoadCoverAsync(string archivePath, CancellationToken cancellationToken);
  }
  ```
* **Implementation Details**:
  * If the path exists in `_coverCache`, return the cached `Bitmap`.
  * Otherwise, use `SharpCompress.Archive.ArchiveFactory` to open the archive stream.
  * Search alphabetically for the first file entry with an image extension (`.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`).
  * Extract the entry to a memory stream, instantiate an Avalonia `Bitmap`, cache it, and return it.
  * Execute asynchronously on the ThreadPool to keep UI responsive.

---

### 5.3 ViewModel Specifications

#### `ViewModels/ComicItemViewModel.cs`
* **Inheritance**: Inherits `ObservableValidator` from `CommunityToolkit.Mvvm` (implements `INotifyDataErrorInfo` for automatic XAML validation highlighting).
* **Properties**:
  * `FilePath` (string, read-only)
  * `FileName` (string, read-only)
  * `IsDirty` (bool, observable)
  * `CoverImage` (Bitmap, observable, default: null)
  * **ComicInfo Mapped Fields (with Validation Attributes)**:
    * `Title` (string?): No validation.
    * `Series` (string?): No validation.
    * `Number` (string?): Represents issue. No validation.
    * `Volume` (int?): Validated with `[Range(0, int.MaxValue, ErrorMessage = "Volume must be a positive integer")]`.
    * `Publisher` (string?): No validation.
    * `Year` (int?): Validated with `[Range(1000, 9999, ErrorMessage = "Year must be a 4-digit number")]`.
    * `Month` (int?): Validated with `[Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]`.
    * `Genre` (string?): No validation.
    * `Tags` (string?): No validation.
    * `Writer` (string?): No validation.
    * `LanguageISO` (string?): No validation.
    * `Manga` (bool): Maps to the string `Manga` property of `ComicInfo`. `Checked` represents `"Yes"`, `Unchecked` represents `"No"`.
* **Implementation Details**:
  * Setters of metadata properties must compare with original values, update the backing field, call `ValidateProperty`, and set `IsDirty = true` if modified.
  * Expose `ApplyChangesToModel(ComicInfo info)` to dump current properties into a `ComicInfo` instance.

#### `ViewModels/MainWindowViewModel.cs`
* **Inheritance**: Inherits `ViewModelBase`.
* **Observable Properties**:
  * `SelectedDirectory` (string)
  * `IsRecursive` (bool)
  * `Comics` (ObservableCollection<ComicItemViewModel>)
  * `ActiveComic` (ComicItemViewModel? - selected in grid)
  * `IsLoading` (bool)
  * `IsSaving` (bool)
  * `ProgressValue` (double, 0 to 100)
  * `ProgressText` (string)
  * **Bulk Edit Fields**:
    * `BulkSeries` (string?), `BulkSeriesEnabled` (bool)
    * `BulkPublisher` (string?), `BulkPublisherEnabled` (bool)
    * `BulkYear` (int?), `BulkYearEnabled` (bool)
    * `BulkGenre` (string?), `BulkGenreEnabled` (bool)
    * `BulkManga` (bool), `BulkMangaEnabled` (bool)
  * **Find & Replace Fields**:
    * `FindText` (string), `ReplaceText` (string)
    * `SelectedReplaceColumn` (string - e.g. "Title", "Series", "Publisher")
* **Commands**:
  * `LoadDirectoryCommand` (Runs `ComicScannerService`)
  * `SaveAllCommand` (Filters `Comics.Where(c => c.IsDirty)`, saves asynchronously)
  * `BulkApplyCommand` (Applies checked bulk fields to all currently selected comics in the DataGrid)
  * `FindReplaceCommand` (Executes Find & Replace inline on selected comics)
  * `ExportCsvCommand` (Dumps current DataGrid items to CSV)
* **Status Flags**:
  * `CanSave`: Evaluates to `true` if any comic `IsDirty` and no active validation errors exist in `Comics`.

---

### 5.4 View Specifications (`Views/MainWindow.axaml`)
* **Layout Grid**:
  * Row 0: Toolbar (Menu, buttons, scan checkboxes, progress indicator).
  * Row 1: SplitView or 2-column Grid.
    * **Left Column (DataGrid)**: Virtualized `DataGrid` with selection mode `Extended`. Binds column cells to `ComicItemViewModel` properties.
    * **Right Column (Sidebar Panel)**:
      * Tab 1: **Active Comic details**. Displays Cover Thumbnail (loads lazily when `ActiveComic` changes) and basic read-only paths.
      * Tab 2: **Bulk Edit Panel**. Checkboxes next to metadata input textboxes, plus a button labeled "Apply to Selected".
      * Tab 3: **Find & Replace**. Form containing Search input, Replace input, Target Column dropdown, and "Execute" button.
  * Row 2: Status Bar (Progress details, file counts, error warnings).

---

## 6. Performance, Safety, and Edge Cases

### 6.1 Edge Cases
1. **Closing Guard with Unsaved Changes**:
   * The `MainWindow.axaml.cs` handles the `Closing` event. If `DataContext.HasDirtyItems` is true, display an Avalonia Dialog with Save, Discard, and Cancel options. Cancel halts closing.
2. **Missing Metadata**:
   * If a CBZ/CBR doesn't contain a `ComicInfo.xml`, the scanner uses default/empty fields rather than failing. A new XML is structured and injected upon saving.
3. **Invalid Archives**:
   * If an archive is corrupt and fails to open, mark the item in the list as `Error` and log the exception. Do not crash the entire loading routine.

### 6.2 Performance Tuning
* **Virtualization**: The `DataGrid` MUST use `VirtualizingStackPanel` for row rendering to support directories containing up to 10,000 files.
* **Asynchronous Save Operations**: Save logic must write to disk sequentially but asynchronously, reporting progress updates to the UI thread using `IProgress<T>`.

### 6.3 Backup Actions
* The core library creates a `.bak` backup file during repacking. The Avalonia app will automatically delete `.bak` files once a repack operation has been validated and completed successfully, preserving disk space.

---

## 7. Verification and Testing Plan

### 7.1 Automated Integration Verification
* Create mock unit tests in `AvaloniaApp.Tests/` using standard NUnit/XUnit.
* Test `ComicScannerService` with dummy archives in a local workspace directory.
* Verify `ArchiveCoverService` returns null gracefully for text/non-image archives.
* Test `ComicItemViewModel` validation rules using `Validator.TryValidateObject`.

### 7.2 Manual Verification Checklist
1. **Open Directory**: Load a directory containing 10+ `.cbz` files. Verify progress bar works and rows appear with file names.
2. **Lazy Thumbnail Load**: Select a row. Verify the cover thumbnail appears in the sidebar, and check that scrolling fast does not trigger UI lag.
3. **Inline Edit & State Change**: Double-click the `Title` column of a row, edit the value, and hit enter. Verify that the row is visually flagged as unsaved/dirty.
4. **Validation Test**: Change `Year` to `"abcd"` or `"99"`. Verify a red border highlights the field and the "Save All" button becomes disabled.
5. **Bulk Edit Test**: Select 3 rows. In the sidebar, check `Publisher`, enter `"Marvel Comics"`, and click *Apply*. Verify all 3 selected rows display the new publisher value.
6. **Find & Replace Test**: Select 2 rows. Enter find `"Vol 1"`, replace `"Volume 1"`. Execute and verify values update.
7. **Save Checklist**: Click *Save All*. Confirm that saving runs in the background, backups (`.bak`) are not left on disk, and saved items clear their "dirty" styling.
8. **Export CSV**: Click *Export CSV*, choose a location, and verify that the file compiles successfully.

---

## 8. Future Roadmap (Next Iteration)
* Support automated numbering (`1, 2, 3...`) across selected records.
* Support prepending/appending tags rather than replacing them.
* Support editing raw loose files and folders containing a `ComicInfo.xml`.

---

## 9. Appendix: Core Library Reference API

To ensure correct integration with the core `ComicMetadataEditor` library, the implementing model must use the following API schemas.

### 9.1 `ComicInfo` Metadata Class (from `ComicMetadataEditor/ComicInfo.cs`)
```csharp
namespace ComicMetadataEditor;

public class ComicInfo
{
    public string? Title { get; set; }
    public string? Series { get; set; }
    public string? Number { get; set; } // Issue number
    public int? Count { get; set; }
    public int? Volume { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public string? Writer { get; set; }
    public string? Penciller { get; set; }
    public string? Inker { get; set; }
    public string? Colorist { get; set; }
    public string? Letterer { get; set; }
    public string? CoverArtist { get; set; }
    public string? Editor { get; set; }
    public string? Publisher { get; set; }
    public string? Imprint { get; set; }
    public string? Genre { get; set; }
    public string? Tags { get; set; }
    public string? Web { get; set; }
    public int? PageCount { get; set; }
    public string? LanguageISO { get; set; }
    public string? Format { get; set; }
    public string? BlackAndWhite { get; set; } // "Yes", "No"
    public string? Manga { get; set; } // "Yes" (RTL), "No" (LTR)
    public string? Characters { get; set; }
    public string? Teams { get; set; }
    public string? Locations { get; set; }
    public string? ScanInformation { get; set; }
    public string? StoryArc { get; set; }
    public string? SeriesGroup { get; set; }
    public string? AgeRating { get; set; }
}
```

### 9.2 `MetadataEditor` Class (from `ComicMetadataEditor/MetadataEditor.cs`)
```csharp
namespace ComicMetadataEditor;

public class BulkEditReport
{
    public int TotalFound { get; set; }
    public List<string> Successes { get; }
    public List<(string Path, Exception Exception)> Failures { get; }
}

public class MetadataEditor
{
    // Bulk edit all files in a directory using an edit action
    public BulkEditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction);

    // Reads ComicInfo from a single CBZ/CBR file (returns empty ComicInfo if missing)
    public ComicInfo ReadMetadata(string filePath);

    // Edits ComicInfo for a single CBZ/CBR file, re-serializes, and repacks safely
    public void EditMetadata(string filePath, Action<ComicInfo> editAction);
}
```


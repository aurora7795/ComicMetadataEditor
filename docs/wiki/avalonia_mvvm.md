# Avalonia UI & MVVM Design

This page outlines the MVVM framework and code blueprints for the `InkTag.Gui` project.

---

## 🏗️ View-ViewModel Relationship

The desktop client follows the standard MVVM design pattern:

* **Views**:
  * `MainWindow.axaml`: Main workspace layout, top toolbar, spreadsheet DataGrid, resizable details/bulk sidebar, and status bar.
  * `SeriesSearchWizardWindow.axaml`: Search and interactive volume picker with publisher badges, aliases, descriptions, and truncated text tooltips.
  * `ScraperMatchWindow.axaml`: Candidate matcher with cover image previews, confidence scores, and conflict resolution.
  * `ApiKeyRequiredWindow.axaml`: Modal prompt guiding users to configure their ComicVine API key.
  * `SettingsWindow.axaml`: Application settings (ComicVine API key, caching, scraper thresholds).
  * `AboutWindow.axaml`, `ThirdPartyLicensesWindow.axaml`, `ErrorSummaryWindow.axaml`, `PromptWindow.axaml`: Utility and diagnostic dialogs.
* **ViewModels**:
  * `MainWindowViewModel`: Main controller managing collection state, scanning, saving, bulk tools, and updates.
  * `ComicItemViewModel`: File-level model wrapping `ComicInfo` with property validation and dirty tracking.
  * `SeriesSearchWizardViewModel` & `SeriesItemViewModel`: Series search query orchestrator and item representations.
  * `ScraperMatchViewModel` & `CandidateMatchViewModel`: Issue matching and candidate comparison.
  * `SettingsViewModel`: User preferences manager.
  * `BulkEditRuleViewModel`: Multi-rule batch modifier.
* **Services**:
  * `ComicScannerService`: Bounded parallel directory scanner (`Parallel.ForEachAsync` with 2–8 workers, real-time `ScanProgressReport` callbacks reporting active files and sizes, unseekable virtual mount detection, and microsecond cancellation).
  * `ArchiveCoverService`: Asynchronous thumbnail extractor with size-capped LRU bitmap cache.
  * `UpdateService`: Dual-mode update manager (Velopack in-place + direct GitHub Releases API fallback).
* **Converters**:
  * `IsDirtyToBrushConverter.cs`: Highlights rows/cells with unsaved changes.

---

## 💾 ViewModel Specifications

### 1. `ComicItemViewModel` (Individual File Row)
* **Inheritance**: `ObservableValidator`
* **Purpose**: Manages the edit status and validation state of a single comic archive.
* **Key Fields & Validation Rules**:

| Property | Bindable Type | Validation Attribute | Description |
| :--- | :--- | :--- | :--- |
| `FileName` | `string` (read-only) | *None* | Archive file name |
| `IsDirty` | `bool` | *None* | Flags unsaved changes |
| `Year` | `int?` | `[Range(1000, 9999)]` | Must be a 4-digit number |
| `Month` | `int?` | `[Range(1, 12)]` | Must be between 1 and 12 |
| `Volume` | `int?` | `[Range(0, int.MaxValue)]` | Must be positive |
| `Manga` | `bool` | *None* | Checked maps to `MangaDirection.Yes`, Unchecked to `MangaDirection.No` |

* **Validation Flow**:
  * Setters invoke `ValidateProperty(value, nameof(PropertyName))`.
  * If validation fails, `HasErrors` becomes true, coloring DataGrid cells in red and disabling `SaveAllCommand`.
  * `IsDirty` becomes `true` when a property deviates from its baseline archive state.

### 2. `MainWindowViewModel` (Main Controller)
* **Inheritance**: `ViewModelBase` (inherits `ObservableObject`)
* **Core Collections**:
  * `Comics`: `ObservableCollection<ComicItemViewModel>` (populates the DataGrid).
* **Key Observable Properties**:
  * `IsLoading` / `ProgressValue` / `ProgressText`: Real-time scan progress counter and dynamic streaming diagnostics.
  * `IsSlowShareWarningVisible` / `SlowShareWarningMessage`: Flags unseekable remote mounts (FTP/FUSE) and presents overlay advisory guidance.
* **Core Commands**:
  * `LoadDirectoryCommand`: Triggers directory scanning with bounded parallel concurrency, active file tracking, and live progress reporting.
  * `CancelScanCommand`: Aborts active folder scanning in milliseconds via stream-level `CancellationToken` checks while preserving whatever files were already parsed.
  * `SaveAllCommand`: Asynchronously writes all `IsDirty` items to disk using atomic repackaging.
  * `BulkApplyCommand`: Iterates selected grid items and applies sidebar-checked metadata fields.
  * `FindReplaceCommand`: Executes search-and-replace strings across target columns.
  * `RefreshGridCommand`: Re-scans active directory (`F5`).
  * `ToggleInspectorCommand`: Toggles visibility of the right-hand details/bulk inspector sidebar (`IsInspectorVisible`).
  * `ScrapeMetadataCommand`: Initiates ComicVine matching for selected items.
  * `SeriesSearchWizardCommand`: Opens interactive Series Search Wizard.
  * `InferMetadataFromFilenamesCommand`: Infers series, issue number, volume, and year from archive filenames.
  * `CheckForUpdatesCommand` & `ApplyUpdateCommand`: Queries and downloads GitHub updates.
  * `OpenLogsCommand`: Opens log directory in the system file manager.

---

## 🖥️ Layout Grid Specifications (`MainWindow.axaml`)

The main workspace area (`Grid.Row="2"`) uses a 3-column layout equipped with an interactive `GridSplitter`:

* **Column 0** (`Width="*"`, `MinWidth="300"`): Main DataGrid area containing virtualized spreadsheet rows with all 35 `ComicInfo` metadata fields. Includes a semi-transparent loading overlay and cancel button when `IsLoading == true`.
* **Column 1** (`Width="Auto"`): Vertical `GridSplitter` (`Width="6"`, `ResizeDirection="Columns"`). Allows interactive drag-resizing between `MinWidth="250"` and `MaxWidth="800"`.
* **Column 2** (`x:Name="InspectorColumn"`, `Width="350"`): Collapsible details, bulk editing, and search & replace inspector panel (`Border` container).

```text
+------------------------------------------------------------------------------------------------------------------+
|  Top MenuBar: File  Edit  View  Tools  Help  (NativeMenu on macOS)                                              |
|  Toolbar: [📁 Open] [💾 Save All] [☑ Recursive] Path... [🌐 Scrape] [🧙 Series Wizard] [🔄 Refresh] [👁️ Inspector]|
+----------------------------------------------------+---+---------------------------------------------------------+
|                                                    | G | Right Inspector Panel (Collapsible & Resizable):        |
| Left Column: Spreadsheet DataGrid                  | r |  - Tab 1: Metadata (Cover Art, 35 Metadata Fields)      |
| (Virtualized rows, dirty indicators,               | i |  - Tab 2: Bulk Tools (Multi-Field Apply, Find & Replace)|
|  semi-transparent loading overlay on scan)         | d |                                                         |
|                                                    | S |  Sidebar (TabControl):                                  |
|                                                    | p |  +---------------------------+                          |
|                                                    | l |  |  [Cover Image Thumbnail]  |                          |
|                                                    | i |  |  Title: [100 Bullets    ] |                          |
+----------------------------------------------------+---+---------------------------------------------------------+
| Status Bar: X files loaded | [ProgressBar] Scanning: 45/200 (22%) [✕ Cancel] | UpdateStatus | InkTag v0.9.1      |
+------------------------------------------------------------------------------------------------------------------+
```

# Avalonia UI & MVVM Design

This page outlines the MVVM framework and code blueprints for the `InkTag.Gui` project.

---

## 🏗️ View-ViewModel Relationship

The desktop client follows the standard MVVM design pattern:

* **Views** (`MainWindow.axaml`, `AboutWindow.axaml`, `ThirdPartyLicensesWindow.axaml`, `ErrorSummaryWindow.axaml`, `PromptWindow.axaml`): Declares the layout structure, cross-platform MenuBar & native macOS menu integration (`NativeMenu.Menu`), modal close guards, and error display dialogs. Bindings hook UI element properties directly to ViewModel properties.
* **ViewModels** (`MainWindowViewModel`, `ComicItemViewModel`): Holds the state of the UI and handles execution commands. Uses `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]` and `[RelayCommand]`).
* **Services** (`ComicScannerService`, `ArchiveCoverService`, `UpdateService`): Background directory scanner, async cover image loader with size-capped LRU bitmap disposal, and dual-mode updater manager (Velopack in-place + direct GitHub API fallback for portable builds).
* **Converters** (`IsDirtyToBrushConverter.cs`): Binds cell/row background colors to indicate unsaved changes visually inside the DataGrid.

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
  * Setters must invoke `ValidateProperty(value, nameof(PropertyName))`.
  * If validation fails, `HasErrors` becomes true, which automatically colors DataGrid cells in red.
  * `IsDirty` is set to `true` when a property is changed from its original value.

### 2. `MainWindowViewModel` (Main Controller)
* **Inheritance**: `ViewModelBase` (inherits `ObservableObject`)
* **Core Collections**:
  * `Comics`: `ObservableCollection<ComicItemViewModel>` (populates the DataGrid).
* **Core Commands**:
  * `LoadDirectoryCommand`: Triggers directory scanning recursively or flat.
  * `SaveAllCommand`: Asynchronously writes all `IsDirty` view models to disk using `InkTag.Core`'s `EditMetadata`. Disabled if `Comics` contains active validation errors (`!CanSave`).
  * `BulkApplyCommand`: Iterates selected grid items and applies sidebar-checked metadata fields.
  * `FindReplaceCommand`: Executes search-and-replace strings on selected items.
  * `RefreshGridCommand`: Re-scans active directory (`F5`).
  * `ToggleInspectorCommand`: Toggles visibility of the right-hand details/bulk inspector sidebar (`IsInspectorVisible`).
  * `CheckForUpdatesCommand`: Asynchronously queries GitHub Releases via `UpdateService` (with portable mode API fallback).
  * `ApplyUpdateCommand`: Downloads pending delta packages in-place, or launches default system browser to GitHub Release URL in portable mode.
  * `OpenLogsCommand`: Opens log directory in system file manager.

---

## 🖥️ Layout Grid Specifications (`MainWindow.axaml`)

The main interface is split into three main regions:

```text
+----------------------------------------------------------------------------------------------------------------+
|  Top MenuBar: File  Edit  View  Tools  Help  (NativeMenu on macOS)                                            |
+--------------------------------------------------+-------------------------------------------------------------+
|                                                  | Right Inspector Panel (Collapsible via View Menu):           |
| Left Column: Spreadsheet DataGrid                |  - Tab 1: Details (Cover Art, Metadata Editor)              |
| (Resizable columns, row dirty status indicators) |  - Tab 2: Bulk Edit (Batch Metadata Multi-Apply)            |
|                                                  |  - Tab 3: Find & Replace (Batch String Substitution)        |
+--------------------------------------------------+-------------------------------------------------------------+
| Bottom Status Bar: X files loaded | [ProgressBar] ProgressText | UpdateStatus | InkTag v0.4.2                 |
+----------------------------------------------------------------------------------------------------------------+
```
|                                                  |  Sidebar (TabControl):                                      |
|                                                  |  +---------------------------+                              |
|                                                  |  | Active Details | Bulk Edit|                              |
|                                                  |  +---------------------------+                              |
|  Main DataGrid (Left Column)                     |  |  [Cover Image Thumbnail]  |                              |
|  - Virtualized spreadsheet                       |  |                           |                              |
|  - Columns: File Name, Title, Series, Issue,     |  |  Title: [Marvel Comics ]  |                              |
|    Volume, Publisher, Year, Genre, Tags, Writer, |  |  Publisher: [Marvel    ]  |                              |
|    Language, Manga orientation.                 |  |                           |                              |
|                                                  |  +---------------------------+                              |
+--------------------------------------------------+-------------------------------------------------------------+
|  Status Bar: Ready | 42 files loaded | 3 unsaved changes                                                      |
+----------------------------------------------------------------------------------------------------------------+
```

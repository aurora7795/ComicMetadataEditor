# Avalonia UI & MVVM Design

This page outlines the MVVM framework and code blueprints for the `InkTag.Gui` project.

---

## 🏗️ View-ViewModel Relationship

The desktop client follows the standard MVVM design pattern:

* **Views** (`MainWindow.axaml`, `ErrorSummaryWindow.axaml`, `PromptWindow.axaml`): Declares the layout structure, modal close guards, and error display dialogs. Bindings hook UI element properties directly to ViewModel properties.
* **ViewModels** (`MainWindowViewModel`, `ComicItemViewModel`): Holds the state of the UI and handles execution commands. Uses `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]` and `[RelayCommand]`).
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
| `Manga` | `bool` | *None* | Checked maps to `"Yes"`, Unchecked to `"No"` |

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

---

## 🖥️ Layout Grid Specifications (`MainWindow.axaml`)

The main interface is split into three main regions:

```text
+------------------------------------------------------------------------------------+
|  Top Toolbar: [Open Folder]  [x] Scan Subfolders  [Save All]  [Export CSV]  [Progress] |
+--------------------------------------------------+---------------------------------+
|                                                  |  Sidebar (TabControl):          |
|                                                  |  +---------------------------+  |
|                                                  |  | Active Details | Bulk Edit|  |
|                                                  |  +---------------------------+  |
|  Main DataGrid (Left Column)                     |  |  [Cover Image Thumbnail]  |  |
|  - Virtualized spreadsheet                       |  |                           |  |
|  - Columns: File Name, Title, Series, Issue,     |  |  Title: [Marvel Comics ]  |  |
|    Volume, Publisher, Year, Genre, Tags, Writer, |  |  Publisher: [Marvel    ]  |  |
|    Language, Manga orientation.                 |  |                           |  |
|                                                  |  +---------------------------+  |
+--------------------------------------------------+---------------------------------+
|  Status Bar: Ready | 42 files loaded | 3 unsaved changes                          |
+------------------------------------------------------------------------------------+
```

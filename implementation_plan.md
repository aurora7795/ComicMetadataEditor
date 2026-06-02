# Bulk‑Edit UI for ComicMetadataEditor (Avalonia)

## Goal
Create a modern, responsive Avalonia desktop application that lets users select a folder of comic files and edit multiple metadata fields (e.g., title, issue, series, publisher, tags, description) across many comics in one operation.

## High‑Level Architecture
- **Model Layer** – Re‑use existing `ComicInfo.cs` and `MetadataEditor.cs` classes for reading/writing ComicInfo XML files.
- **ViewModel Layer** – Implement MVVM pattern:
  - `MainWindowViewModel` – holds selected folder, list of `ComicItemViewModel`s, bulk‑edit commands, status.
  - `ComicItemViewModel` – wraps a single comic’s metadata, tracks edited values, provides `IsSelected` flag.
- **View Layer** – Avalonia XAML UI:
  - Toolbar with *Select Folder*, *Load*, *Save All*, *Export CSV*.
  - DataGrid showing one row per comic, columns for editable fields (Title, Issue, Series, Publisher, Tags, Description). Enable multi‑cell editing and row selection.
  - Bulk‑Edit panel: input fields plus *Apply to Selected* button.
  - Status bar with progress and messages.

## UI Design Details
- Use **Fluent/Material** theme (e.g., `Avalonia.Themes.Fluent`).
- Dark mode default, optional light toggle.
- DataGrid virtualization for large folders (thousands of files).
- Inline validation (e.g., required Title, numeric Issue).
- Keyboard shortcuts: `Ctrl+S` for Save, `Ctrl+O` for Open folder.
- Responsive layout – grid splits horizontally on wide screens, stacks on narrow.

## Bulk‑Edit Workflow
1. User selects a folder → `MainWindowViewModel.LoadFolderAsync()` scans for `*.cbz`, `*.cbr`, `*.xml` ComicInfo files.
2. For each file, create `ComicItemViewModel` by parsing existing metadata using `MetadataEditor`.
3. UI displays rows; user can edit any cell directly.
4. Bulk‑Edit panel allows setting a value (e.g., Publisher = "Marvel") and clicking *Apply* to propagate to all selected rows.
5. *Save All* writes changes back via `MetadataEditor.SaveAsync(comicItem)`.
6. Optional *Export CSV* dumps current view for external editing.

## Key Classes to Add
- `Views/MainWindow.axaml` & `Views/MainWindow.axaml.cs`
- `ViewModels/MainWindowViewModel.cs`
- `ViewModels/ComicItemViewModel.cs`
- `Services/FolderScanner.cs` – recursively enumerate comic files.
- `Services/BatchEditService.cs` – logic for applying bulk changes.

## Project Structure
```
ComicMetadataEditor/
├─ AvaloniaApp/               # New Avalonia project (net6.0‑windows, linux, macOS)
│   ├─ Views/
│   ├─ ViewModels/
│   ├─ Services/
│   └─ App.axaml & App.xaml.cs
├─ ComicInfo.cs               # Existing model
├─ MetadataEditor.cs          # Existing editor logic
└─ ...
```

## Steps to Implement (Task List)
1. **Create Avalonia project** (`dotnet new avalonia.app -o AvaloniaApp`).
2. Add references to existing project (`dotnet add AvaloniaApp reference ../ComicMetadataEditor.csproj`).
3. Implement `FolderScanner` to populate a collection of `ComicItemViewModel`s.
4. Build `ComicItemViewModel` exposing bind‑able properties for each metadata field.
5. Design `MainWindow.axaml` with Toolbar, DataGrid, Bulk‑Edit panel.
6. Wire commands (`SelectFolderCommand`, `LoadCommand`, `SaveAllCommand`, `BulkApplyCommand`).
7. Implement validation and progress reporting.
8. Test with a sample folder of comics (create dummy ComicInfo XML files if needed).
9. Polish UI – theming, dark mode toggle, responsive layout.
10. Add unit tests for `FolderScanner` and `BatchEditService`.

## Verification Plan
- **Automated**: Run `dotnet test` after adding unit tests.
- **Manual**: Launch the app, open a folder with 10+ comics, edit fields, bulk‑apply a value, save, and verify the XML files were updated correctly.
- **Cross‑platform**: Build and run on Windows, Linux, macOS.

---
**Open Questions**
- Do you need support for additional metadata formats (e.g., JSON, CSV) beyond the existing ComicInfo XML?
- Should the UI include image previews of comic covers?
- Any specific styling preferences (color palette, font) beyond the default Fluent theme?

---
*Please review the plan and let me know if any changes are needed. Once approved I will create the task list and start implementation.*

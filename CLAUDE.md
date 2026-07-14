# Project Constitution & AI Guide (CLAUDE.md)

This file sets the build, test, and style guidelines for the ComicMetadataEditor repository, and establishes the schema for maintaining the Layer 2 LLM Wiki.

---

## 1. Commands & Workflows

### 1.1 Compile & Build
* **Whole Solution**: `dotnet build`
* **Core Library**: `dotnet build ComicMetadataEditor/ComicMetadataEditor.csproj`
* **Console App**: `dotnet build ComicEditorConsole/ComicEditorConsole.csproj`
* **Avalonia App**: `dotnet build AvaloniaApp/AvaloniaApp.csproj`

### 1.2 Run Projects
* **CLI App**: `dotnet run --project ComicEditorConsole/ComicEditorConsole.csproj -- [directory]`
* **Avalonia App**: `dotnet run --project AvaloniaApp/AvaloniaApp.csproj`

---

## 2. Coding & Design Standards

### 2.1 General C# Style Guidelines
* **Language Level**: C# 10.0 / .NET 10.0 (Nullable references enabled).
* **Namespaces**: Use file-scoped namespaces (e.g. `namespace ComicMetadataEditor;`).
* **Naming Conventions**:
  * Classes, Interfaces, Methods, Properties: `PascalCase` (e.g., `ComicScannerService`, `ReadMetadata`).
  * Parameters, Local Variables: `camelCase` (e.g., `directoryPath`, `cancellationToken`).
  * Private Fields: Prefixed with underscore `_camelCase` (e.g., `_coverCache`).
* **Asynchronous Programming**:
  * Use `async`/`await` extensively for file/network I/O.
  * Always propagate `CancellationToken` to asynchronous tasks.
  * Execute background work on the ThreadPool (e.g., `Task.Run`) to prevent blocking UI main threads.

### 2.2 MVVM Conventions (CommunityToolkit.Mvvm)
* **ViewModels**: 
  * Must inherit from `ObservableObject` or `ObservableValidator` (for field validation).
  * Use C# Source Generators: annotate fields with `[ObservableProperty]` to generate properties, and methods with `[RelayCommand]` to generate commands.
  * Class names must end with `ViewModel` (e.g., `ComicItemViewModel`).
* **Views**:
  * Keep code-behind views clean. Views should only handle UI lifecycle, file/folder pickers, and raw windows events.
  * UI Logic must reside in the ViewModels.

---

## 3. Wiki Maintenance Rules (Layer 2 Schema)

As an AI Agent, you are responsible for maintaining the project's **Layer 2 Wiki** (located under [docs/wiki/](file:///home/aurora7795/AntiGravProjects/ComicMetadataEditor/docs/wiki/)). You must adhere to the following rules:

1. **Auto-Update**: Whenever you modify code (e.g., adding a new metadata field, changing validation rules, introducing a new service), you must read and update the corresponding wiki file in `docs/wiki/` to reflect these changes.
2. **Concept Cross-Linking**: Always cross-reference wiki pages using relative markdown paths (e.g., `[Metadata Editor](metadata_editor.md)`).
3. **Index Registry**: If you create a new wiki page, you MUST register it inside [docs/wiki/index.md](file:///home/aurora7795/AntiGravProjects/ComicMetadataEditor/docs/wiki/index.md).
4. **No Placeholders**: Never write placeholders, dummy comments, or TODOs in the wiki. Maintain complete, actual API surfaces, architectures, and guidelines.

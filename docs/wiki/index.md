# InkTag Wiki (Layer 2)

Welcome to the AI-maintained project wiki for **InkTag**. This directory compiles structural designs, API schemas, and specifications to ensure consistent system development and maintenance.

---

## 🗺️ Wiki Directory Map

### 1. [System Architecture](architecture.md)
* Details the layout of the project modules (`InkTag.Core`, `InkTag.Cli`, `InkTag.Mcp`, `InkTag.Gui`, `InkTag.Tests`).
* Includes communication flows, MCP tools, and archive repackaging lifecycles.

### 2. [Core Metadata Editor API](core_editor_api.md)
* Specifications for the `ComicInfo` schema model.
* Specifications and method descriptors for the `MetadataEditor` repackaging engine, dynamic JSON patching, and cover art extraction.

### 3. [Avalonia UI & MVVM Design](avalonia_mvvm.md)
* Class structures and interface definitions for views and view models in `InkTag.Gui`.
* State management, progress tracking, and validation rules.

### 4. [CLI & MCP Interface Specifications](cli_mcp.md)
* Command specifications for `InkTag.Cli` subcommands and flags.
* JSON-RPC stdio tool specifications for `InkTag.Mcp`.

### 5. [Testing & Verification Guide](testing.md)
* Guidelines for automated unit tests in `InkTag.Tests`.
* Detailed manual test checklist.

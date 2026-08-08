# InkTag

A lightweight, robust C# utility, CLI tool, MCP Server, and desktop application designed to bulk-edit `ComicInfo.xml` metadata embedded inside comic book archives (`.cbz` and `.cbr`). 

The project contains a core domain library (`InkTag.Core`), an AI-agent-friendly CLI (`InkTag.Cli`), a Model Context Protocol server (`InkTag.Mcp`), and an Avalonia desktop GUI interface (`InkTag.Gui`).

---

## 🚀 Key Features

* **Dual-Format Processing:** Supports reading both `.cbz` (ZIP-based) and `.cbr` (RAR/RAR-like) archives using random-access `ArchiveFactory.OpenArchive()`.
* **Cross-Platform MenuBar & NativeMenu:** Full `File`, `Edit`, `View`, `Tools`, and `Help` navigation with hotkeys (`Ctrl+O`, `Ctrl+S`, `F5`, `Ctrl+Q`) and native macOS screen top MenuBar integration.
* **Velopack Auto-Updater & Fallback:** Cross-platform auto-updates via Velopack with direct GitHub API release checking fallback for portable builds (Linux AppImages / macOS DMGs).
* **AI Agent Native & Tier-1 MCP SDK:** Built-in Model Context Protocol server (`InkTag.Mcp`) using the official **`ModelContextProtocol` C# SDK** (`v2.1.0`) over `stdio`, plus structured CLI (`InkTag.Cli`) with `--json` output mode and `--dry-run` safety checks.
* **Multimodal Cover Extraction:** Extracts front cover art for visual LLMs (Gemini, Claude, GPT-4o) to visually inspect titles, creators, and issue numbers.
* **Dynamic JSON Patching:** Mutate metadata via JSON strings without compiling C# lambdas.
* **Archive Security & Safe Replacement:** Active path containment (ZipSlip defense), safe temp extraction, and atomic archive repacking with automatic `.bak` rollbacks.
* **Open Source Attributions:** Full license attributions for third-party tools ([THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md)).

---

## 📁 Repository Structure

```text
InkTag/
├── src/
│   ├── InkTag.Core/          # Domain models, ComicInfo.xml parsing, metadata engines
│   │   ├── Schema/
│   │   │   └── ComicInfo.xsd # XML schema definition for validation
│   │   ├── ComicInfo.cs      # Deserialization model
│   │   └── MetadataEditor.cs # Core bulk-editing & repackaging logic
│   │
│   ├── InkTag.Cli/           # Command-line interface
│   │   └── Program.cs        # Subcommands (read, update, scan, cover, schema)
│   │
│   ├── InkTag.Mcp/           # Model Context Protocol (MCP) Server
│   │   └── Program.cs        # Stdio JSON-RPC tools for Claude, Cursor, Antigravity
│   │
│   └── InkTag.Gui/           # Avalonia UI desktop application
│       └── ...               # MVVM Avalonia UI
│
├── tests/
│   └── InkTag.Tests/         # Automated xUnit test suite
│
├── .agents/skills/           # Agent Skill package definitions
│   └── comic-metadata-curator/
│
└── docs/                     # Project wiki and design specifications
```

---

## 🛠️ Getting Started

### macOS App Installation Note
Because pre-built `.dmg` releases are open-source and unnotarized by Apple:
1. Drag **InkTag.app** into `/Applications`.
2. Clear the browser quarantine attribute via Terminal:
   ```bash
   xattr -cr /Applications/InkTag.app
   ```
*(Or go to **System Settings > Privacy & Security** and click **Open Anyway**).*

### Prerequisites
* [.NET SDK 8.0 / 9.0 / 10.0](https://dotnet.microsoft.com/download)

### Building the Project
From the repository root, compile the entire solution:
```bash
dotnet build InkTag.slnx
```

---

## 🤖 AI Agent Integration

### 1. Model Context Protocol (MCP) Server
Run the MCP server (powered by the official **`ModelContextProtocol` C# SDK** `v2.1.0`) to expose comic metadata tools directly to AI assistants (Claude Desktop, Cursor, Antigravity, VS Code):

```bash
dotnet run --project src/InkTag.Mcp/InkTag.Mcp.csproj
```

**Exposed MCP Tools:**
* `read_comic_metadata`: Read XML metadata from archive as JSON.
* `update_comic_metadata`: Apply JSON property edits (with optional `dryRun`).
* `extract_cover_image`: Extract cover image (optionally returns base64 for vision LLMs).
* `scan_comics`: Scan directory for files missing specific metadata fields.
* `get_comic_schema`: Get JSON Schema for `ComicInfo`.

### 2. Structured Agentic CLI (`InkTag.Cli`)
Run subcommands with `--json` for machine-parseable agent execution:

```bash
# Read metadata as JSON
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- read comic.cbz --json

# Preview proposed metadata updates (Dry Run)
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- update comic.cbz --patch '{"Writer": "Alan Moore"}' --dry-run --json

# Apply JSON patch updates to a file or directory
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- update /path/to/comics --patch '{"Publisher": "Marvel"}' --json

# Scan directory for comics missing required fields
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- scan /path/to/comics --missing "Writer,Series,Year" --json

# Extract cover image for visual inspection
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- cover comic.cbz --output cover.jpg --json

# Export JSON schema specification
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- schema --json
```

---

## 💻 Library Usage Example

You can integrate the core domain library into your own projects by referencing `InkTag.Core.csproj`.

```csharp
using InkTag.Core;

var editor = new MetadataEditor();

// Perform bulk edits via C# Lambda
BulkEditReport report = editor.BulkEditMetadata("/path/to/comics", comic =>
{
    comic.Publisher = "Marvel";
    comic.Series = "The Amazing Spider-Man";
    comic.LanguageISO = "en";
    comic.Year = 2026;
});

// Or edit via dynamic JSON Patch (ideal for scripts & agent payloads)
editor.EditMetadataFromJson("/path/to/comic.cbz", "{\"Writer\": \"Stan Lee\", \"Issue\": 1}");

// Extract cover image for multimodal processing
string? coverPath = editor.ExtractCoverImage("/path/to/comic.cbz", "/tmp/cover.jpg");
```

---

## ⚙️ Technical Details & Design Choices

### Upgraded Random-Access Archive Engine
Uses `SharpCompress` (`0.48.0`) with `ArchiveFactory.OpenArchive()` to parse archive structures via random-access entry streams instead of linear readers. This guarantees full compatibility with `.cbr` (RAR v4 / RAR v5) archives and non-standard or streamed `.cbz` files while preventing directory traversal vulnerabilities.

### Cross-Platform Auto-Updating
Integrates `Velopack` (v1.2.0) into **InkTag Desktop**, providing rate-limited update polling against GitHub Releases, background delta downloading, and seamless binary swapping on Windows (`.exe` setup), macOS (`.app`/`.pkg`), and Linux (`.AppImage`).

### Atomic-Like File Swapping
1. Contents extracted to unique GUID temporary folder.
2. `ComicInfo.xml` modified and validated against schema.
3. Repacked into `.cbz.tmp` archive and verified for readability.
4. Existing archive swapped with backup `.bak` safeguard.
5. On failure, rollback automatically restores original archive.

---

## 🗺️ Roadmap & Future Vision

### Completed Milestones
* **[x] Official Tier-1 MCP C# SDK Migration:** Upgraded `InkTag.Mcp` to the official `ModelContextProtocol` C# SDK (`v2.1.0`) with declarative `[McpServerTool]` tool definitions.
* **[x] Archive Security & Extraction Containment:** Implemented strict ZipSlip defenses, path containment checks, and safe temp archive extraction options.
* **[x] Fix XSD validation edge case:** Handled gracefully on missing metadata files.
* **[x] Complete Avalonia Desktop App (InkTag Desktop):** Implemented modern spreadsheet grid, side panels, lazy-loaded cover thumbs, validation, find-replace, and safe background saving.
* **[x] Make Library AI-Agent Ready:** Added stdio MCP server, single-file bundling, structured `--json` CLI subcommands, dynamic JSON patch API, cover art extraction, and agent skill package (`.agents/skills/comic-metadata-curator/`).
* **[x] Velopack Auto-Updater & CI/CD Pipelines:** Automated cross-platform GitHub Actions workflows producing Windows installers (`vpk`), macOS `.dmg`, Linux `.AppImage`, and standalone MCP binaries with silent update support.
* **[x] Project Restructuring:** Renamed solution to **InkTag** with clean `src/` and `tests/` architecture (`InkTag.Core`, `InkTag.Cli`, `InkTag.Mcp`, `InkTag.Gui`, `InkTag.Tests`).

### Upcoming Milestones

#### 📦 Distribution & Packaging
* **[ ] NuGet Package Publishing:** Publish core library (`InkTag.Core`) to [NuGet.org](https://www.nuget.org).
* **[ ] Global .NET Tool:** Package `InkTag.Mcp` as a global .NET tool (`dotnet tool install -g InkTag.Mcp`).

#### 🤖 AI Ecosystem & MCP Discoverability
* **[ ] MCP Registry Indexing:** Submit MCP server to official `modelcontextprotocol/servers`, Smithery.ai, Glama.ai, and `mcp.so`.
* **[ ] Visual Media Demos:** Add animated GIFs demonstrating Avalonia GUI grid editing and AI agent cover-art inspection in action.

#### 🌐 Media Server & Online Database Integrations
* **[ ] Komga & Kavita Sync:** Support direct API sync commands to push metadata updates directly to self-hosted Komga and Kavita comic servers.
* **[ ] External Metadata Scraper Adapters:** Add optional integrations (e.g., ComicVine / Metron API) for auto-fetching missing writer, penciller, and issue metadata.

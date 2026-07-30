# InkTag

A lightweight, robust C# utility, CLI tool, MCP Server, and desktop application designed to bulk-edit `ComicInfo.xml` metadata embedded inside comic book archives (`.cbz` and `.cbr`). 

The project contains a core domain library (`InkTag.Core`), an AI-agent-friendly CLI (`InkTag.Cli`), a Model Context Protocol server (`InkTag.Mcp`), and an Avalonia desktop GUI interface (`InkTag.Gui`).

---

## 🚀 Key Features

* **Dual-Format Processing:** Supports reading both `.cbz` (ZIP-based) and `.cbr` (RAR/RAR-like) archives.
* **AI Agent Native:** Built-in Model Context Protocol (MCP) server (`InkTag.Mcp`) over `stdio` and structured CLI (`InkTag.Cli`) with `--json` output mode and `--dry-run` safety checks.
* **Multimodal Cover Extraction:** Extracts front cover art for visual LLMs (Gemini, Claude, GPT-4o) to visually inspect titles, creators, and issue numbers.
* **Dynamic JSON Patching:** Mutate metadata via JSON strings without compiling C# lambdas.
* **Safe Replacement Strategy:** Minimizes risk of data loss by repacking to a temporary archive, validating readability, making backups, and swapping target paths on success.
* **Metadata Schema Validation:** Validates `ComicInfo.xml` files against official XML (`ComicInfo.xsd`) and exports JSON Schema specifications.

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
Run the MCP server to expose comic metadata tools directly to AI assistants (Claude Desktop, Cursor, Antigravity, VS Code):

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

### Upgraded Archive Handler
Uses `SharpCompress` (`0.48.0`) to read and write archives securely against directory traversal vulnerabilities.

### Atomic-Like File Swapping
1. Contents extracted to unique GUID temporary folder.
2. `ComicInfo.xml` modified and validated against schema.
3. Repacked into `.cbz.tmp` archive and verified for readability.
4. Existing archive swapped with backup `.bak` safeguard.
5. On failure, rollback automatically restores original archive.

---

## 🗺️ Roadmap & Future Vision

### Completed Milestones
* **[x] Fix XSD validation edge case:** Handled gracefully on missing metadata files.
* **[x] Complete Avalonia Desktop App:** Implemented modern spreadsheet grid, side panels, lazy-loaded cover thumbs, validation, find-replace, and safe background saving.
* **[x] Make Library AI-Agent Ready:** Added stdio MCP server, structured `--json` CLI subcommands, dynamic JSON patch API, cover art extraction, and agent skill package (`.agents/skills/comic-metadata-curator/`).
* **[x] Project Restructuring:** Renamed solution to **InkTag** with clean `src/` and `tests/` architecture (`InkTag.Core`, `InkTag.Cli`, `InkTag.Mcp`, `InkTag.Gui`, `InkTag.Tests`).

### Upcoming Milestones

#### 📦 Distribution & Packaging
* **[ ] NuGet Package Publishing:** Publish core library (`InkTag.Core`) to [NuGet.org](https://www.nuget.org).
* **[ ] Global .NET Tool:** Package `InkTag.Mcp` as a global .NET tool (`dotnet tool install -g InkTag.Mcp`).
* **[ ] Pre-Compiled Binary Releases:** Add GitHub Actions CI/CD to auto-generate standalone executables (`.exe`, Linux AppImage/binary, macOS DMG/binary).

#### 🤖 AI Ecosystem & MCP Discoverability
* **[ ] MCP Registry Indexing:** Submit MCP server to official `modelcontextprotocol/servers`, Smithery.ai, Glama.ai, and `mcp.so`.
* **[ ] Visual Media Demos:** Add animated GIFs demonstrating Avalonia GUI grid editing and AI agent cover-art inspection in action.

#### 🌐 Media Server & Online Database Integrations
* **[ ] Komga & Kavita Sync:** Support direct API sync commands to push metadata updates directly to self-hosted Komga and Kavita comic servers.
* **[ ] External Metadata Scraper Adapters:** Add optional integrations (e.g., ComicVine / Metron API) for auto-fetching missing writer, penciller, and issue metadata.

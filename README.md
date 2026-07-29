# ComicMetadataEditor

A lightweight, robust C# utility, CLI tool, MCP Server, and desktop application designed to bulk-edit `ComicInfo.xml` metadata embedded inside comic book archives (`.cbz` and `.cbr`). 

The project contains a core class library (`ComicMetadataEditor`), an AI-agent-friendly CLI (`ComicEditorConsole`), a Model Context Protocol server (`ComicMetadataEditor.Mcp`), and a desktop interface (`AvaloniaApp`).

---

## 🚀 Key Features

* **Dual-Format Processing:** Supports reading both `.cbz` (ZIP-based) and `.cbr` (RAR/RAR-like) archives.
* **AI Agent Native:** Built-in Model Context Protocol (MCP) server over `stdio` and structured CLI with `--json` output mode and `--dry-run` safety checks.
* **Multimodal Cover Extraction:** Extracts front cover art for visual LLMs (Gemini, Claude, GPT-4o) to visually inspect titles, creators, and issue numbers.
* **Dynamic JSON Patching:** Mutate metadata via JSON strings without compiling C# lambdas.
* **Safe Replacement Strategy:** Minimizes risk of data loss by repacking to a temporary archive, validating readability, making backups, and swapping target paths on success.
* **Metadata Schema Validation:** Validates `ComicInfo.xml` files against official XML (`ComicInfo.xsd`) and exports JSON Schema specifications.

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
├── ComicEditorConsole/          # CLI Application (Agent & Human friendly)
│   └── Program.cs               # Subcommands (read, update, scan, cover, schema)
│
├── ComicMetadataEditor.Mcp/     # Model Context Protocol (MCP) Server
│   └── Program.cs               # Stdio JSON-RPC tools for Claude, Cursor, Antigravity
│
├── AvaloniaApp/                 # Cross-platform Desktop App (Completed)
│   └── ...                      # MVVM Avalonia UI
│
├── .agents/skills/              # Agent Skill package definitions
│   └── comic-metadata-curator/  # Curation workflow instructions for AI agents
│
└── docs/                        # Project reports and documentation
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

---

## 🤖 AI Agent Integration

### 1. Model Context Protocol (MCP) Server
Run the MCP server to expose comic metadata tools directly to AI assistants (Claude Desktop, Cursor, Antigravity, VS Code):

```bash
dotnet run --project ComicMetadataEditor.Mcp/ComicMetadataEditor.Mcp.csproj
```

**Exposed MCP Tools:**
* `read_comic_metadata`: Read XML metadata from archive as JSON.
* `update_comic_metadata`: Apply JSON property edits (with optional `dryRun`).
* `extract_cover_image`: Extract cover image (optionally returns base64 for vision LLMs).
* `scan_comics`: Scan directory for files missing specific metadata fields.
* `get_comic_schema`: Get JSON Schema for `ComicInfo`.

### 2. Structured Agentic CLI (`ComicEditorConsole`)
Run subcommands with `--json` for machine-parseable agent execution:

```bash
# Read metadata as JSON
dotnet run --project ComicEditorConsole -- read comic.cbz --json

# Preview proposed metadata updates (Dry Run)
dotnet run --project ComicEditorConsole -- update comic.cbz --patch '{"Writer": "Alan Moore"}' --dry-run --json

# Apply JSON patch updates to a file or directory
dotnet run --project ComicEditorConsole -- update /path/to/comics --patch '{"Publisher": "Marvel"}' --json

# Scan directory for comics missing required fields
dotnet run --project ComicEditorConsole -- scan /path/to/comics --missing "Writer,Series,Year" --json

# Extract cover image for visual inspection
dotnet run --project ComicEditorConsole -- cover comic.cbz --output cover.jpg --json

# Export JSON schema specification
dotnet run --project ComicEditorConsole -- schema --json
```

---

## 💻 Library Usage Example

You can integrate the core editor into your own projects by referencing `ComicMetadataEditor.csproj`.

```csharp
using ComicMetadataEditor;

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

## 🗺️ Roadmap
* **[x] Fix XSD validation edge case:** Handled gracefully on missing metadata files.
* **[x] Complete Avalonia Desktop App:** Implemented modern spreadsheet grid, side panels, lazy-loaded cover thumbs, validation, find-replace, and safe background saving.
* **[x] Make Library AI-Agent Ready:** Added stdio MCP server, structured `--json` CLI subcommands, dynamic JSON patch API, cover art extraction, and agent skill package (`.agents/skills/comic-metadata-curator/`).
* **[x] Enhance nested models:** Made properties on the sub-class `Page` in `ComicInfo.cs` nullable to prevent default numeric values from serializing into XML.

# InkTag

A lightweight, high-performance C# utility, CLI tool, MCP Server, and Avalonia desktop application designed to inspect, match, and bulk-edit `ComicInfo.xml` metadata embedded inside comic book archives (`.cbz` and `.cbr`).

The solution comprises a domain library (`InkTag.Core`), an AI-agent-friendly CLI (`InkTag.Cli`), an official Model Context Protocol server (`InkTag.Mcp`), and a modern Avalonia desktop GUI (`InkTag.Gui`).

---

## 🚀 Key Features

* **Dual-Format Processing:** Supports reading and writing both `.cbz` (ZIP-based) and `.cbr` (RAR/RAR-like) archives using random-access entry streams via `ArchiveFactory.OpenArchive()`.
* **📚 Bulk Auto-Tagging Pipeline:** High-throughput parallel queue with automatic series volume clustering, chronological ComicVine matching, and perceptual cover art verification (`dHash`).
* **✏️ Bulk Comic File Renaming Engine:** Pattern-based renaming from embedded metadata (`{Series}`, `#{Number:3}`, `{Year}`, `{Title}`, `{Publisher}`, `{Volume}`, `{ScanInfo}`) with collision protection, atomic renaming, and clean scanner/release tag stripping by default.
* **👁️ Perceptual Cover Hashing (`dHash`):** 64-bit gradient fingerprinting with SIMD-accelerated (`BitOperations.PopCount`) Hamming distance comparison to automatically identify issues by cover art similarity across thousands of candidates in <1ms.
* **🌐 ComicVine Metadata Scraper & Live Matching:** Query ComicVine for issue and volume metadata, perform side-by-side field-by-field diff comparison, and apply updates using customizable merge policies (*Fill Missing Only* vs. *Overwrite All*).
* **🧙 Interactive Series Search Wizard:** 2-step wizard workflow to search series volumes by title, publisher, and year, browse issues in natural numerical order (`#1, #2... #10, #11`), and 1-click apply metadata.
* **🎯 Year-Weighted Matching & Volume-First Resolution:** Publication year alignment heavily influences candidate ranking with severe cross-decade penalties (`-40%`) to eliminate false volume matches.
* **🤖 AI Agent Native & Official MCP SDK:** Built-in Model Context Protocol server (`InkTag.Mcp`) using the official **`ModelContextProtocol` C# SDK** (`v2.1.0`) over `stdio`, exposing automated scraping, visual similarity metrics, schema validation, dynamic JSON patching, and bulk renaming to AI assistants (Claude Desktop, Cursor, Antigravity).
* **🖥️ Cross-Platform MenuBar & NativeMenu:** Full `File`, `Edit`, `View`, `Tools`, and `Help` navigation with hotkeys (`Ctrl+O`, `Ctrl+S`, `Ctrl+M`, `F5`, `Ctrl+Q`) and native macOS screen top MenuBar integration.
* **⚡ Velopack Auto-Updater & Fallback:** Seamless auto-updates via Velopack with GitHub Releases API polling fallback for portable builds (Linux AppImages / macOS DMGs).
* **🛡️ Archive Security & Atomic Swapping:** Strict ZipSlip defense, path containment validation, and atomic archive repacking with automatic `.bak` safety rollbacks.

---

## 📁 Repository Structure

```text
InkTag/
├── src/
│   ├── InkTag.Core/          # Domain models, ComicInfo.xml parsing, scrapers, image hashing
│   │   ├── Configuration/    # AppSettings & config management
│   │   ├── Images/           # PerceptualHashService (64-bit dHash & Hamming distance)
│   │   ├── Renaming/         # ComicFileRenamer (template formatting, collision checking, atomic rename)
│   │   ├── Schema/           # ComicInfo.xsd XML schema definition
│   │   ├── Scrapers/         # ComicVineProvider, MetadataScraperService, BulkScrapeQueueService
│   │   └── MetadataEditor.cs # Bulk editing, cover extraction, & archive repacking
│   │
│   ├── InkTag.Cli/           # Command-line interface
│   │   └── Program.cs        # Subcommands (read, update, scan, cover, scrape, rename, schema)
│   │
│   ├── InkTag.Mcp/           # Model Context Protocol (MCP) Server
│   │   └── ComicTools.cs     # Stdio JSON-RPC tools for Claude, Cursor, Antigravity
│   │
│   └── InkTag.Gui/           # Avalonia UI desktop application
│       ├── ViewModels/       # MVVM ViewModels (BulkScrape, RenamePreview, SeriesSearch, CandidateItem)
│       ├── Views/            # MainWindow, BulkScrapeQueueWindow, RenamePreviewWindow, SettingsWindow
│       └── Services/         # ArchiveCoverService, UpdateService
│
├── tests/
│   └── InkTag.Tests/         # Automated xUnit test suite (94 unit tests)
│
├── .agents/skills/           # Agent Skill package definitions
│   └── comic-metadata-curator/
│
└── docs/                     # Project wiki and design specifications
```

---

## 🛠️ Getting Started

### Prerequisites
* [.NET SDK 10.0 / 9.0 / 8.0](https://dotnet.microsoft.com/download)

### Building the Solution
```bash
dotnet build InkTag.slnx
```

### Running Unit Tests
```bash
dotnet test
```

### macOS App Installation Note
Because pre-built `.dmg` releases are open-source:
1. Drag **InkTag.app** into `/Applications`.
2. Clear the quarantine attribute via Terminal:
   ```bash
   xattr -cr /Applications/InkTag.app
   ```
*(Or go to **System Settings > Privacy & Security** and click **Open Anyway**).*

---

## 🤖 AI Agent & MCP Integration

### 1. Model Context Protocol (MCP) Server
Run the MCP server (powered by the official **`ModelContextProtocol` C# SDK** `v2.1.0`) to expose comic metadata tools directly to AI assistants:

```bash
dotnet run --project src/InkTag.Mcp/InkTag.Mcp.csproj
```

**Exposed MCP Tools:**
* `scrape_comic_metadata`: Scrape and apply ComicVine metadata with visual cover match verification and confidence metrics.
* `bulk_scrape_directory`: Automated parallel queue to scrape and cover-match whole directories of comics.
* `rename_comic_files`: Batch rename comic files based on metadata using configurable naming templates with collision protection.
* `search_comic_vine`: Search ComicVine candidate issues with confidence scores and thumbnail URLs.
* `read_comic_metadata`: Read XML metadata from archive as structured JSON.
* `update_comic_metadata`: Apply JSON property edits (with optional `dryRun`).
* `extract_cover_image`: Extract cover image (optionally returns base64 for vision LLMs).
* `scan_comics`: Scan directory for files missing specific metadata fields.
* `get_comic_schema`: Get JSON Schema for `ComicInfo`.

### 2. Structured Agentic CLI (`InkTag.Cli`)
Run subcommands with `--json` for machine-parseable execution:

```bash
# Auto-tag online metadata for an archive
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- scrape comic.cbz --json

# Bulk rename comic files based on metadata
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- rename /path/to/comics --template "{Series} #{Number:3} ({Year})" --json

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

Reference `InkTag.Core.csproj` to integrate metadata editing, perceptual cover hashing, and file renaming into your application:

```csharp
using InkTag.Core;
using InkTag.Core.Images;
using InkTag.Core.Renaming;
using InkTag.Core.Scrapers;

var editor = new MetadataEditor();

// 1. Bulk edit metadata via C# Lambda
BulkEditReport report = editor.BulkEditMetadata("/path/to/comics", comic =>
{
    comic.Publisher = "Dark Horse";
    comic.Series = "Eden: It's an Endless World!";
    comic.LanguageISO = "en";
    comic.Year = 2006;
});

// 2. Standardized File Renaming from Metadata
string newName = ComicFileRenamer.GenerateFilename(comic, "/comics/old_scan.cbz", "{Series} #{Number:3} ({Year})");
// Output: "Eden: It's an Endless World! #001 (2006).cbz"

// 3. Compute perceptual dHash for cover matching
ulong coverHash = editor.GetCoverHash("/path/to/comic.cbz");

// 4. Compare visual similarity between two cover hashes (0.0 to 1.0)
ulong onlineCoverHash = 0b1100110011001100UL;
double similarity = PerceptualHashService.CalculateSimilarity(coverHash, onlineCoverHash);
bool isMatch = PerceptualHashService.IsVisualMatch(coverHash, onlineCoverHash, threshold: 0.90);
```

---

## 🗺️ Roadmap & Milestones

### Completed Milestones
* **[x] Bulk Auto-Tag Pipeline & File Renamer Engine (`v0.10.0`):** Streaming parallel identification queue with cover visual hashing (`dHash`), chronological volume clustering, duplicate-save protection, and template-based file renaming engine (`ComicFileRenamer`) across Core, GUI, CLI, and MCP.
* **[x] Metadata Deserialization & Archive Recovery (`v0.9.1`):** Resilient handling of malformed or out-of-order `ComicInfo.xml` files during metadata edit operations, preventing save failures and guaranteeing XML schema compliance upon repack.
* **[x] Network Mount Resilience & Diagnostics (`v0.9.0`):** Slow virtual remote share detection (GVFS FTP / FUSE), sequential forward-only streaming fallback, real-time file download diagnostics, in-overlay advisory guidance, and sub-10ms instantaneous stream cancellation.
* **[x] Perceptual Cover Hashing & Visual Matching (`v0.8.0`):** 64-bit `dHash` image fingerprinting, live cover match badging (`👁 XX% Cover Match`), and Visual Override matching for unorganized files.
* **[x] Series Search Wizard (`v0.7.0`):** 2-step volume and issue search wizard with natural numerical ordering (`1, 2, 3... 10, 11`) and quick apply workflows.
* **[x] ComicVine Metadata Scraper System:** Built-in provider with caching, rate-limited HTTP client, field-by-field diff comparison, and selective merge policies.
* **[x] Official Tier-1 MCP C# SDK Integration:** Powered by the official `ModelContextProtocol` C# SDK (`v2.1.0`) with declarative tools and rich visual verification returns.
* **[x] Cross-Platform MenuBar & NativeMenu:** Menu and toolbar navigation with hotkeys and macOS system menu integration.
* **[x] Velopack Auto-Updater & Portable Fallback:** Automated cross-platform updates for Windows, macOS, and Linux AppImages.
* **[x] Archive Security Hardening:** Strict ZipSlip defense and atomic file swapping with `.bak` rollbacks.

### Upcoming Milestones
* **[ ] Komga & Kavita Sync:** Support direct API sync commands to push metadata updates directly to self-hosted Komga and Kavita comic servers.
* **[ ] Metron & GCD Provider Adapters:** Add Metron and Grand Comics Database (GCD) community scrapers alongside ComicVine.
* **[ ] NuGet Package Publishing:** Publish `InkTag.Core` to [NuGet.org](https://www.nuget.org).
* **[ ] Global .NET Tool:** Package `InkTag.Mcp` as a global .NET tool (`dotnet tool install -g InkTag.Mcp`).
* **[ ] MCP Registry Submission:** Submit `InkTag.Mcp` to the official Model Context Protocol server directory and Smithery.ai.

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
InkTag includes a high-performance, official **`ModelContextProtocol` C# SDK** (`v2.1.0`) server that connects AI assistants directly to your comic library for automated cataloging, auditing, visual verification, and Komga sync.

#### 🚀 Quick-Start Configuration
Add the configuration block below to your AI client (**OpenClaw**, **Claude Desktop**, **Cursor**, **Windsurf**, **Cline**, or **Antigravity**):

<details open>
<summary><b>🍏 macOS (Bundled with InkTag.app)</b></summary>

```json
{
  "mcpServers": {
    "inktag": {
      "command": "/Applications/InkTag.app/Contents/MacOS/InkTag.Mcp",
      "args": [],
      "env": {
        "COMICVINE_API_KEY": "YOUR_COMICVINE_API_KEY",
        "INKTAG_ALLOWED_ROOT_PATHS": "/Volumes/General/Comics:/Users/YOUR_USERNAME/Comics"
      }
    }
  }
}
```
</details>

<details>
<summary><b>🪟 Windows (Standalone MCP Download)</b></summary>

```json
{
  "mcpServers": {
    "inktag": {
      "command": "C:\\Tools\\InkTag.Mcp.exe",
      "args": [],
      "env": {
        "COMICVINE_API_KEY": "YOUR_COMICVINE_API_KEY",
        "INKTAG_ALLOWED_ROOT_PATHS": "D:\\Comics;C:\\Users\\YOUR_USERNAME\\Comics"
      }
    }
  }
}
```
</details>

<details>
<summary><b>🐧 Linux (Standalone Tarball or Source)</b></summary>

```json
{
  "mcpServers": {
    "inktag": {
      "command": "/usr/local/bin/InkTag.Mcp",
      "args": [],
      "env": {
        "COMICVINE_API_KEY": "YOUR_COMICVINE_API_KEY",
        "INKTAG_ALLOWED_ROOT_PATHS": "/media/comics:/home/YOUR_USERNAME/comics"
      }
    }
  }
}
```
*Or run directly from source:* `dotnet run --project src/InkTag.Mcp/InkTag.Mcp.csproj`
</details>

---

#### 🔐 Environment Variables & Security Sandboxing

| Variable | Required | Description |
| :--- | :---: | :--- |
| `INKTAG_ALLOWED_ROOT_PATHS` | **Recommended** | Restricts the AI agent to specific folders. Separate paths with `:` on macOS/Linux or `;` on Windows. |
| `INKTAG_MCP_READ_ONLY` | Optional | When set to `true` (or launch with `--read-only`), strictly disables all archive modifications, renames, and writes. |
| `COMICVINE_API_KEY` | Optional | ComicVine API key. If omitted, falls back to the key stored in InkTag Desktop settings. |
| `KOMGA_SERVER_URL` | Optional | Self-hosted Komga server URL (e.g. `http://192.168.1.30:25600`). |
| `KOMGA_API_KEY` | Optional | Komga API Key for automated targeted analysis and collection synchronization. |

---

#### 🛠️ Available MCP Tools

> [!NOTE]
> All mutating tools (`update_comic_metadata`, `rename_comic_files`, `scrape_comic_metadata`, `bulk_scrape_directory`) default to **`dryRun: true`** (preview only) for prompt-injection safety. AI agents must explicitly pass `dryRun: false` to write changes to disk. Every write automatically creates a timestamped pre-write backup snapshot in `~/.local/share/InkTag/backups/`.

| Tool | Parameters | Description |
| :--- | :--- | :--- |
| **`read_comic_metadata`** | `filePath` | Extracts full metadata (`ComicInfo.xml` & legacy CBI) as clean JSON. |
| **`update_comic_metadata`** | `filePath`, `patch`, `dryRun`, `recursive` | Applies JSON metadata patches (defaults to `dryRun: true`). |
| **`extract_cover_image`** | `filePath`, `outputPath`, `returnBase64` | Extracts the cover page (supports base64 image return for LLM vision models). |
| **`scan_comics`** | `directoryPath`, `missingFields`, `recursive`, `onlyUntagged` | Audits libraries for unorganized, untagged, or incomplete archives. |
| **`search_external_metadata`** | `series`, `issueNumber`, `year`, `apiKey` | Searches ComicVine volumes and issues with thumbnail URLs and confidence scores. |
| **`scrape_comic_metadata`** | `path`, `mode`, `dryRun`, `apiKey` | Scrapes ComicVine metadata, verifies cover perceptual hashes, and tags the archive (defaults to `dryRun: true`). |
| **`bulk_scrape_directory`** | `directory`, `mode`, `dryRun`, `recursive`, `apiKey` | Parallel identification queue with cover visual hashing and batch auto-tagging (defaults to `dryRun: true`). |
| **`rename_comic_files`** | `path`, `template`, `preserveScanInfo`, `dryRun`, `recursive` | Standardizes file names using configurable naming templates (defaults to `dryRun: true`). |
| **`list_metadata_backups`** | `path`, `limit` | Lists automated pre-write metadata backup snapshots for comic archives. |
| **`restore_comic_backup`** | `path`, `backupId` | Restores a comic archive's `ComicInfo.xml` metadata from a previous backup snapshot. |
| **`list_batch_jobs`** | `limit` | Lists recent multi-file batch jobs with total backup counts and affected files. |
| **`restore_batch_job`** | `batchJobId` | Atomically rolls back an entire multi-file batch job to its pre-batch state. |
| **`get_backup_provenance`** | `backupId` | Retrieves deep forensic provenance (SHA-256 hash, cover dHash, thumbnail URL, diffs). |
| **`check_komga_server`** | `serverUrl`, `apiKey` | Tests connectivity, version, and library roots on your Komga server. |
| **`sync_komga_book_or_series`** | `path`, `storyArc` | Performs targeted sub-second Komga cache invalidation and Collection synchronization. |
| **`audit_komga_library`** | `libraryId` | Audits Komga series count, total books, and library path bindings. |
| **`get_comic_schema`** | *none* | Returns the complete JSON schema for `ComicInfo` fields. |

---

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

* **[x] Bulk Scrape Visual Diagnostics, Multi-Year Volume Scoring & Settings Navigation (`v0.12.2`):** Smart volume lifespan year scoring (preventing false penalties on multi-year series runs), on-demand targeted thumbnail hashing for all matched issues (#51+), informative `Cover Match` badges (`Text Only`, `No Local Cover`, `No Remote Cover`), structured `Confidence` breakdown tooltips, and automatic focus/tab navigation to ComicVine API key settings from prompts.
* **[x] MCP Security Hardening, Batch Rollbacks & Forensic Provenance (`v0.12.1`):** Strict read-only mode (`INKTAG_MCP_READ_ONLY=true`), safe-by-default dry runs (`dryRun: true`), automated pre-write metadata snapshots in isolated AppData, atomic batch-level transaction rollbacks (`restore_batch_job`), forensic audit trails (source SHA-256, cover dHash, matched thumbnail URL, diffs), and visual match attribution notes.
* **[x] Light Mode, Dark Mode & UI Visual Harmony (`v0.12.0`):** Configurable theme modes (`System Default`, `Dark Mode`, `Light Mode`) with real-time live preview in Settings and menu shortcuts, semantic theme dictionaries, theme-aware pastel/emerald dirty row indicators, unified neutral outline action hierarchy, segmented filter pills, soft framed status bar, and real-time status badge indicators in bulk auto-tag queues.
* **[x] Komga Media Server REST Sync, Tabbed Settings & Tagging Notes Attribution (`v0.11.1`):** Direct REST API integration with self-hosted Komga media servers with sub-second targeted book/series cache analysis, automatic `<StoryArc>` and `<SeriesGroup>` to Komga Collections synchronization, Docker/NAS path translation, 4-tab modern settings layout, resizable DataGrid columns, and standardized ComicVine tagging attribution notes in the `<Notes>` field.
* **[x] Hierarchical Path Inference, MCP Sandboxing & Legacy CBI Ingestion (`v0.11.0`):** Smart 2-level ancestor directory metadata inference (resolving series, volume, and year from nested folder structures), strict MCP security root sandboxing (`AllowedRootPaths`), automatic ComicVine rate-limit backoff retry (HTTP 420/429), debounced scraper caching, and legacy ComicBookInfo (CBI) zip comment ingestion with automatic upgrade to ComicInfo.xml v2.1.
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

# InkTag CLI & MCP Interface Specifications

This page details the command-line interface (`InkTag.Cli`) and Model Context Protocol stdio server (`InkTag.Mcp`).

---

## 💻 InkTag.Cli (Command Line Interface)

`InkTag.Cli` provides machine-parseable and human-readable interfaces for querying and editing comic archive metadata.

### Invocation Syntax
```bash
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- <command> [options]
```

### Commands

| Command | Usage | Description |
|---|---|---|
| `read` | `read <file>` | Reads and displays `ComicInfo.xml` metadata as JSON or text. |
| `update` | `update <file\|dir> --patch '<json>' [--dry-run] [--recursive]` | Applies JSON property edits to a single archive or all archives in a directory. |
| `rename` | `rename <file\|dir> [--template '<pattern>'] [--preserve-scans] [--dry-run] [--recursive]` | Renames comic archives on disk based on their embedded metadata. |
| `scan` | `scan <directory> [--untagged] [--missing Field1,Field2] [--recursive]` | Scans a directory for comic files, untagged archives, and missing metadata fields. |
| `cover` | `cover <file> [--output <image-path>]` | Extracts front cover image from comic archive. |
| `scrape` | `scrape <file\|dir> [--api-key KEY] [--mode fill-missing\|overwrite] [--dry-run] [--recursive]` | Auto-tags metadata from ComicVine using cover perceptual dHash visual matching and smart series grouping. |
| `schema` | `schema` | Prints the JSON Schema specification for `ComicInfo` metadata objects. |
| `help` | `help` / `--help` / `-h` | Displays usage instructions and subcommand options. |

### Global Flags

- `--json`: Format command output as structured JSON payload.
- `--dry-run`: Preview metadata diffs without writing modifications to disk.
- `--recursive` / `-r`: Recursively search subdirectories when scanning or updating directory paths.
- `--verbose`: Include detailed stack trace in error JSON responses.

---

## 🔌 InkTag.Mcp (Model Context Protocol Server)

`InkTag.Mcp` is implemented using the official Tier-1 **`ModelContextProtocol` C# SDK** (`modelcontextprotocol/csharp-sdk`). It exposes stdio tool capabilities over standard input/output streams compatible with AI agents (such as Claude Desktop, Gemini Antigravity, or custom MCP clients). Tools are declaratively defined using `[McpServerToolType]` and `[McpServerTool]` attributes on `ComicTools`.

### Available Tools (`tools/list`)

#### 1. `read_comic_metadata`
Reads XML metadata embedded in a CBZ or CBR archive and returns formatted text/JSON.
- **Parameters**: `path` (string, required)

#### 2. `update_comic_metadata`
Updates metadata properties in a comic archive or directory using a JSON patch.
- **Parameters**:
  - `path` (string, required): Target file or directory path.
  - `patch` (object, required): Property updates object (e.g. `{"Writer": "Stan Lee"}`).
  - `dryRun` (boolean, optional): If `true`, returns metadata diffs without writing files.
  - `recursive` (boolean, optional): If `true`, applies edits to nested subdirectories.

#### 3. `extract_cover_image`
Extracts front cover art from a comic archive for multimodal vision inspection.
- **Parameters**:
  - `path` (string, required): Path to comic archive (.cbz / .cbr).
  - `outputPath` (string, optional): Destination file path.
  - `returnBase64` (boolean, optional): Returns base64 encoded image data.

#### 4. `bulk_scrape_directory`
Queues and executes a bulk auto-tag on a directory of comic files using smart series volume clustering and perceptual cover visual matching (dHash).
- **Parameters**:
  - `directory` (string, required): Directory path containing comic archives (.cbz / .cbr).
  - `mode` (string, optional): `"fill-missing"` (default) or `"overwrite"`.
  - `dryRun` (boolean, optional): Previews matched issues and visual similarity scores without modifying files.
  - `recursive` (boolean, optional): Scans nested subdirectories.
  - `apiKey` (string, optional): Optional ComicVine API key.

#### 5. `rename_comic_files`
Renames comic files based on their embedded metadata using standardized naming templates with collision detection.
- **Parameters**:
  - `path` (string, required): Path to a comic file or directory containing comic archives.
  - `template` (string, optional): Naming template (default: `"{Series} #{Number:3} ({Year})"`).
  - `preserveScanInfo` (boolean, optional): Whether to preserve scan/edition tags (default: `false`).
  - `dryRun` (boolean, optional): Previews proposed filename changes without writing to disk.
  - `recursive` (boolean, optional): Scans subdirectories recursively.

#### 6. `scan_comics`
Scans a directory for comic archives, checks for missing metadata fields, and optionally filters down to untagged comics.
- **Parameters**:
  - `directory` (string, required): Path to scan.
  - `missingFields` (array of strings, optional): List of required fields (e.g. `["Writer", "Series"]`).
  - `recursive` (boolean, optional): If `true`, scans subdirectories recursively.
  - `onlyUntagged` (boolean, optional): If `true`, filters and returns only untagged comics.

#### 7. `get_comic_schema`
Returns the JSON Schema specification for valid `ComicInfo` metadata properties.
- **Parameters**: None

#### 8. `search_external_metadata`
Searches ComicVine for candidate issues matching series name, issue number, and publication year.
- **Parameters**:
  - `series` (string, required): Series title.
  - `issueNumber` (string, optional): Issue number.
  - `year` (integer, optional): Publication year.
  - `apiKey` (string, optional): Optional ComicVine API key override.

#### 9. `scrape_comic_metadata`
Auto-tags and applies metadata from ComicVine to a local comic archive with visual cover match verification.
- **Parameters**:
  - `path` (string, required): Path to comic archive (.cbz / .cbr).
  - `mode` (string, optional): `"fill-missing"` (default) or `"overwrite"`.
  - `dryRun` (boolean, optional): Previews updates without modifying files on disk (default: `true`).
  - `apiKey` (string, optional): Optional ComicVine API key override.

#### 10. `list_metadata_backups`
Lists all historical pre-write metadata backups stored for a specific comic archive or across the entire system.
- **Parameters**:
  - `path` (string, optional): Filter history to a specific comic file path.

#### 11. `restore_comic_backup`
Rolls back a comic archive's metadata to a previous timestamped snapshot from the local backup store.
- **Parameters**:
  - `path` (string, required): Path to the comic archive to restore.
  - `timestamp` (string, required): Exact snapshot timestamp or ISO-8601 string to restore.

#### 12. `list_batch_jobs`
Lists all recorded multi-file batch operations (such as bulk auto-tags or directory updates) available for atomic rollback.
- **Parameters**: None

#### 13. `restore_batch_job`
Atomically rolls back all comic archives modified in a multi-file batch job back to their exact pre-write snapshot states.
- **Parameters**:
  - `batchJobId` (string, required): Unique batch job identifier (e.g. `batch_20260822_123456_a4f910`).

#### 14. `get_backup_provenance`
Retrieves forensic provenance metadata for a specific backup snapshot, including pre-write source SHA-256 hash, 64-bit cover visual dHash, matched thumbnail URL, confidence score, and property diffs.
- **Parameters**:
  - `path` (string, required): Path to the comic archive.
  - `timestamp` (string, required): Snapshot timestamp.

---

## 🛡️ MCP Security & Safety Defenses

### 1. Strict Read-Only Mode (`INKTAG_MCP_READ_ONLY=true` / `--read-only`)
When invoked with the `--read-only` command-line flag or when the `INKTAG_MCP_READ_ONLY=true` environment variable is set:
- All mutating operations (`update_comic_metadata`, `rename_comic_files`, `scrape_comic_metadata`, `bulk_scrape_directory`, `restore_comic_backup`, `restore_batch_job`) are strictly disabled.
- Any attempt by an AI agent to execute write operations immediately returns an `UnauthorizedAccessException` with descriptive guidance.
- Read-only analysis and inspection tools remain fully accessible.

### 2. Safe-by-Default Dry Runs
All mutating MCP tools default to `dryRun = true`. AI agents must explicitly pass `dryRun = false` in tool invocations to commit changes to disk.

### 3. Automated Pre-Write Disaster Recovery Backups
Whenever a mutation occurs (`dryRun = false`), `MetadataBackupService` automatically takes an atomic snapshot of the pre-write `ComicInfo.xml`, computes source SHA-256 and cover dHash fingerprints, and records the change in `~/.local/share/InkTag/backups/manifest.json`.

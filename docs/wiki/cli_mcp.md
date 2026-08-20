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
| `scan` | `scan <directory> [--missing Field1,Field2] [--recursive]` | Scans a directory for comic files and flags missing metadata fields. |
| `cover` | `cover <file> [--output <image-path>]` | Extracts front cover image from comic archive. |
| `scrape` | `scrape <file\|dir> [--api-key KEY] [--mode fill-missing\|overwrite] [--dry-run] [--recursive]` | Scrapes metadata from ComicVine using cover perceptual dHash visual matching and smart series grouping. |
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
Queues and executes a bulk scrape on a directory of comic files using smart series volume clustering and perceptual cover visual matching (dHash).
- **Parameters**:
  - `directory` (string, required): Directory path containing comic archives (.cbz / .cbr).
  - `mode` (string, optional): `"fill-missing"` (default) or `"overwrite"`.
  - `dryRun` (boolean, optional): Previews matched issues and visual similarity scores without modifying files.
  - `recursive` (boolean, optional): Scans nested subdirectories.
  - `apiKey` (string, optional): Optional ComicVine API key.

#### 5. `scan_comics`
Scans a directory for comic archives and checks for missing metadata fields.
- **Parameters**:
  - `directory` (string, required): Path to scan.
  - `missingFields` (array of strings, optional): List of required fields (e.g. `["Writer", "Series"]`).
  - `recursive` (boolean, optional): If `true`, scans subdirectories recursively.

#### 6. `get_comic_schema`
Returns the JSON Schema specification for valid `ComicInfo` metadata properties.
- **Parameters**: None

#### 7. `search_external_metadata`
Searches ComicVine for candidate issues matching series name, issue number, and publication year.
- **Parameters**:
  - `series` (string, required): Series title.
  - `issueNumber` (string, optional): Issue number.
  - `year` (integer, optional): Publication year.
  - `apiKey` (string, optional): Optional ComicVine API key override.

#### 8. `scrape_comic_metadata`
Scrapes and applies metadata from ComicVine to a local comic archive with visual cover match verification.
- **Parameters**:
  - `path` (string, required): Path to comic archive (.cbz / .cbr).
  - `mode` (string, optional): `"fill-missing"` (default) or `"overwrite"`.
  - `dryRun` (boolean, optional): Previews updates without modifying files on disk.
  - `apiKey` (string, optional): Optional ComicVine API key override.

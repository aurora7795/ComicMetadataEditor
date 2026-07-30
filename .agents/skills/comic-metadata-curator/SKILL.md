---
name: comic-metadata-curator
description: Standardized workflows and instructions for AI agents auditing, updating, normalizing, and extracting comic metadata (.cbz/.cbr) using InkTag CLI and MCP tools.
---

# Comic Metadata Curator Skill

This skill guides AI agents on auditing, updating, and standardizing metadata embedded in `.cbz` and `.cbr` comic archives using **InkTag**.

---

## 🛠️ Tooling & Interface Options

AI agents can interact with the comic metadata library through three interfaces:

1. **CLI Tool (`InkTag.Cli`)**: Ideal for terminal subagent executions via shell commands.
2. **MCP Server (`InkTag.Mcp`)**: Ideal for model-context-protocol stdio tool calls.
3. **C# Domain Library (`InkTag.Core`)**: Ideal for programmatic .NET applications.

---

## 📋 Recommended Curation Workflows

### 1. Auditing & Discovering Missing Metadata
Before modifying files, scan the target directory to locate incomplete or malformed metadata:

**CLI Command:**
```bash
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- scan /path/to/comics --missing "Writer,Series,Year,Genre" --json
```

**MCP Tool Call:**
```json
{
  "name": "scan_comics",
  "arguments": {
    "directory": "/path/to/comics",
    "missingFields": ["Writer", "Series", "Year", "Genre"]
  }
}
```

---

### 2. Multimodal Vision Cover Inspection
If metadata (like `Title`, `Series`, `Number`, `Writer`) is missing or uncertain, extract the cover image and use vision capabilities to visually inspect the comic cover:

**CLI Command:**
```bash
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- cover /path/to/comics/issue1.cbz --output /tmp/issue1_cover.jpg --json
```

**MCP Tool Call:**
```json
{
  "name": "extract_cover_image",
  "arguments": {
    "path": "/path/to/comics/issue1.cbz",
    "returnBase64": true
  }
}
```

---

### 3. Dry-Run Validation (Safety Preview)
Always perform a dry-run before applying bulk modifications to verify target changes:

**CLI Command:**
```bash
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- update /path/to/comics --patch '{"Publisher":"Marvel", "Manga":"No"}' --dry-run --json
```

**MCP Tool Call:**
```json
{
  "name": "update_comic_metadata",
  "arguments": {
    "path": "/path/to/comics/issue1.cbz",
    "patch": { "Publisher": "Marvel", "LanguageISO": "en" },
    "dryRun": true
  }
}
```

---

### 4. Executing Safe Metadata Updates
Apply validated property changes back into comic archives. The library automatically creates backup swap files to prevent corruption:

**CLI Command:**
```bash
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- update /path/to/comics/issue1.cbz --patch '{"Writer":"Stan Lee", "Penciller":"Steve Ditko", "Year":1962}' --json
```

---

## 📐 Metadata Standardization Guidelines

When normalizing comic metadata, follow these rules:

* **LanguageISO**: Use 2-letter ISO 639-1 codes (`"en"`, `"ja"`, `"fr"`, `"es"`).
* **Manga**: Set to `"Yes"` for right-to-left reading direction, `"No"` for standard left-to-right.
* **Creator Fields**: Separate multiple creators with commas (e.g. `"Stan Lee, Jack Kirby"`).
* **Dates**: Ensure `Year`, `Month`, and `Day` are valid numeric integers.
* **Issue Numbers**: Use standard numeric or issue strings (`"1"`, `"1.5"`, `"Annual 1"`).

# Testing & Verification Guide

This page details the testing strategy and validation checklists for verifying the InkTag application.

---

## 🧪 Automated Testing Strategy

### 1. Unit & Integration Test Suite (`InkTag.Tests`)
* **Target Project**: `tests/InkTag.Tests/InkTag.Tests.csproj`
* **Test Classes**:
  * `MetadataEditorTests`:
    * Deserialization of valid and invalid `ComicInfo.xml` schemas.
    * Validation of schemas against official XSD (`ComicInfo.xsd`), skipping missing files, and throwing on malformed XML.
    * Dynamic JSON patch application (`ApplyJsonPatch`), warning on unrecognized patch keys.
    * Property-level diff generation (`GetMetadataDiff`).
    * JSON Schema generation (`ExportJsonSchema`).
    * ZipSlip protection: verification that entry keys containing `../` traversal sequences cannot escape temporary directories.
    * Repacking safety: atomic swaps, rollback on failed edit callbacks, CBR to CBZ output conversion, and backup file cleanup.
  * `AgentOperationsTests`:
    * Top-level vs. recursive directory scanning (`ScanDirectory`) and missing metadata field detection.
    * Single-file and directory metadata updates (`UpdatePath`) with dry-run diff previews and live bulk editing.
  * `UpdateServiceTests`:
    * Version tag parsing (`TryParseVersion`) and dynamic `CurrentAppVersion` resolution from executing assembly metadata.
  * `ComicScannerServiceTests`:
    * Reading valid comic metadata and setting scanner read-error flags (`HasReadError`, `ReadErrorMessage`) on corrupted archives.

---

## 📋 Manual Quality Checklist

### Phase 1: Directory Operations
1. **Open Directory**: Click *Open Folder*, pick a path with mixed folders and archives. Check that scanning runs on background threads (UI does not freeze) and updates the progress indicator.
2. **Recursive Scan**: Toggle the recursive scan checkbox, reload, and verify that archives located inside subfolders populate correctly.

### Phase 2: Metadata Editing
1. **Inline Edit**: Double-click the `Series` cell of a loaded item, change it, and press Enter. Verify the cell background turns light yellow/indicates "unsaved".
2. **Input Validation**: Select a cell, type invalid data (e.g. Year = `"99"` or `"10000"`), and confirm:
   * A red outline appears around the cell.
   * A validation message is visible in tooltips.
   * The "Save All" button in the toolbar is disabled.
3. **Manga Toggle**: Check/uncheck the Manga checkbox. Save and verify that the output XML tag `<Manga>` is updated to `"Yes"` or `"No"` respectively.

### Phase 3: Bulk Actions & Agent Tools
1. **Selection & Apply**: Highlight multiple rows in the DataGrid. In the sidebar's Bulk Edit tab, check the `Publisher` box, enter `"DC Comics"`, and click *Apply*. Verify that all selected rows update in memory.
2. **CLI JSON Execution**: Run `dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- schema --json` and verify clean JSON output.
3. **MCP Stdio Communication**: Launch `dotnet run --project src/InkTag.Mcp/InkTag.Mcp.csproj` and test tool calls (`read_comic_metadata`, `update_comic_metadata`, `extract_cover_image`, `scan_comics`, `get_comic_schema`).

### Phase 4: Save & Safety
1. **Save Batch**: Click *Save All*. Verify saving runs sequentially in the background while updating the progress bar.
2. **Safety check**: Verify no backup `.bak` files are left in the source directory.
3. **Verify Integrity**: Open one of the saved `.cbz` files using a standard archive viewer, extract `ComicInfo.xml`, and check that the edited properties are present and serialized correctly.

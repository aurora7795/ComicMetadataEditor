# Testing & Verification Guide

This page details the testing strategy and validation checklists for verifying the Comic Metadata Editor application.

---

## 🧪 Automated Testing Strategy

### 1. Core Library Tests
* **Target Assembly**: `ComicMetadataEditor.Tests`
* **Coverage Scope**:
  * Deserialization of valid and invalid `ComicInfo.xml` schemas.
  * Validation of schemas against the official XSD.
  * Repacking safety: Verify renaming, rollbacks upon corrupted repacking streams, and backup file deletions.

### 2. Desktop Application Tests
* **Target Assembly**: `AvaloniaApp.Tests`
* **Coverage Scope**:
  * `ComicScannerService`: Verify that recursive and flat enumerations discover `.cbz` and `.cbr` extensions while ignoring other files.
  * `ArchiveCoverService`: Mock file archives and check that cover extraction handles corrupted headers and empty image sets gracefully.
  * `ComicItemViewModel` Validation: Unit test that validation attributes (`Year`, `Month`, `Volume`) fire events and restrict the parent view model from saving.

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

### Phase 3: Bulk Actions
1. **Selection & Apply**: Highlight multiple rows in the DataGrid. In the sidebar's Bulk Edit tab, check the `Publisher` box, enter `"DC Comics"`, and click *Apply*. Verify that all selected rows update in memory and indicate dirty status.
2. **Find & Replace**: Select a block of rows. Set find to `"Vol. 1"` and replace to `"Volume 1"`. Execute on the `Title` column and verify text swaps immediately.

### Phase 4: Save & Safety
1. **Save Batch**: Click *Save All*. Verify saving runs sequentially in the background while updating the progress bar.
2. **Safety check**: Verify no backup `.bak` files are left in the source directory.
3. **Verify Integrity**: Open one of the saved `.cbz` files using a standard archive viewer, extract `ComicInfo.xml`, and check that the edited properties are present and serialized correctly.

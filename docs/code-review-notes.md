# Code Review Notes

Repository: `aurora7795/ComicMetadataEditor`
Review date: 2026-05-09

## Overall assessment

Good small utility with a clear purpose, but **not ready for production use yet**. The main concerns are:

1. **Data-loss risk during archive rewrite**
2. **Format handling mismatch (`.cbr` input becomes `.cbz` output)**
3. **No validation/error handling around XML and archive operations**
4. **Build/runtime compatibility looks inconsistent**
5. **Repository includes generated/IDE artifacts**

Risk level: **medium-high** because the tool edits user files destructively.

---

## What the code does well

- Keeps the domain model simple with `ComicInfo`.
- Separates the metadata logic from the console entrypoint.
- Uses nullable reference types in the library.
- Rebuilds archive contents recursively, which is straightforward.

---

## Main review findings

### 1. Destructive rewrite can lose files

In `MetadataEditor.EditSingleFileMetadata`, the original `.cbr` is deleted before the replacement is fully validated.

Relevant code:

- `ComicMetadataEditor/MetadataEditor.cs` lines 80-95

**Problem:** if anything goes wrong after delete or move, the user may lose the original archive.

**Recommendation:** write to a temp output file, validate it, and only then replace atomically or keep the original as backup.

### 2. `.cbr` files are being read, but rewritten as `.cbz`

The code only searches for `*.cbr`, but writes ZIP output and renames extension to `.cbz`.

Relevant code:

- `ComicMetadataEditor/MetadataEditor.cs` lines 21-28
- `ComicMetadataEditor/MetadataEditor.cs` lines 80-82

**Why this matters:**

- CBR typically implies RAR-based archives.
- The output is definitely ZIP-based.
- That may be intentional, but it should be explicit because it changes file format, not just metadata.

**Recommendation:** either:

- support both `.cbz` and `.cbr` clearly, or
- rename the project/behavior to “convert + edit”, or
- preserve original format if possible.

### 3. No exception handling around malformed XML / invalid archives

The deserialization path assumes valid XML and valid archive contents.

Relevant code:

- `ComicMetadataEditor/MetadataEditor.cs` lines 58-62

If `ComicInfo.xml` is malformed or missing expected structure, this can throw and abort processing.

Likewise archive reading/writing may fail for:

- corrupted archives
- unsupported archive types
- locked files
- permission issues

**Recommendation:** catch per-file exceptions in bulk mode, log failures, and continue processing remaining files.

### 4. Bulk mode has no reporting

The console app always prints success, even though individual file operations may fail.

Relevant code:

- `ComicEditorConsole/Program.cs` lines 4-13

**Problem:** users can’t tell:

- how many files were found
- which succeeded
- which failed
- whether any were converted to `.cbz`

**Recommendation:** return a result object with counts and failures.

### 5. Directory existence is not checked

`Directory.GetFiles(directoryPath, "*.cbr", ...)` will throw if the path is invalid.

**Recommendation:** validate `directoryPath` before processing and print a helpful error.

### 6. Build compatibility looks odd

The library targets `netstandard2.0`, while the console app targets `net10.0`.

Relevant code:

- `ComicMetadataEditor/ComicMetadataEditor.csproj`
- `ComicEditorConsole/ComicEditorConsole.csproj`

**Potential issue:** `net10.0` is unusual unless preview/future SDK usage is intentional.

**Recommendation:** use a stable target like `net8.0` for the console app unless `net10.0` is deliberate.

### 7. Repository contains generated artifacts

The repo appears to include:

- `.idea/`
- `bin/`
- `obj/`

**Why it matters:**

- adds noise
- causes unnecessary diffs
- can create environment-specific issues

**Recommendation:** add a proper `.gitignore` and remove tracked generated files.

---

## Code-quality notes

### `ComicInfo` model

The XML model is fine for a simple serializer-driven approach.

One thing to watch is that some values such as `BlackAndWhite` and `Manga` are modeled as strings for compatibility, so it would help to document accepted values more explicitly or wrap them in helper methods/constants.

---

## Priority fixes

1. **Make file replacement safe**
   - never delete original before confirmed output exists
   - create backup or atomic replacement flow

2. **Clarify/archive-format behavior**
   - decide whether this edits CBR, converts CBR→CBZ, or should support both

3. **Add per-file error handling and result reporting**
   - continue on failure
   - summarize results at the end

4. **Validate input path and file existence**
   - better UX and fewer crashes

5. **Clean repo artifacts**
   - `.gitignore` for `.idea/`, `bin/`, `obj/`

6. **Revisit framework targets**
   - likely move console app to `net8.0`

---

## Merge/readiness assessment

**Not merge-ready** if this is intended for others to use on real comic archives, mainly because of the destructive file handling and unclear format conversion.

If it’s only a personal prototype, the structure is acceptable, but the replacement logic should still be fixed first.

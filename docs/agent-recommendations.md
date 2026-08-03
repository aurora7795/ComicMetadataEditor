# InkTag — Agent Implementation Recommendations

**Generated:** 2026-08-03  
**Source:** Full-project code review of `audit/security-audit` branch  
**Audience:** AI coding agent implementing fixes and hardening  
**Build baseline:** `dotnet build InkTag.slnx` · `dotnet test` (16 tests passing at time of review)

---

## Mission

Harden InkTag's archive-handling safety, close correctness gaps in the GUI, reduce CLI/MCP duplication, and expand test coverage around the critical `MetadataEditor.EditMetadata` pipeline. Do **not** redesign the architecture — make focused, minimal diffs.

---

## Staged Execution Status

- [x] **Stage 1: Security & Archive Hardening** (Branch: `fix/archive-security-hardening`) — **COMPLETED**
  - [x] **P1-A**: Safe `ExtractionOptions` & path containment validation in `MetadataEditor.EditMetadata`
  - [x] **P1-B**: `--verbose` flag and CLI JSON `stackTrace` gating in `Program.cs`
  - [x] Unit test: `EditMetadata_RejectsZipSlipEntry_EnforcesSafeExtractionOptions` (57 tests passing)

- [x] **Stage 2: Data Integrity & GUI Correctness** (Branch: `fix/gui-data-integrity`) — **COMPLETED**
  - [x] **P2-A**: CBR→CBZ path update in UI DataGrid after save
  - [x] **P2-B**: `MangaDirection` enum fidelity preservation (`YesAndRightToLeft`)
  - [x] **P2-C**: Unrecognized JSON patch key warnings
  - [x] **P2-D**: Scanner read-error flags (`HasReadError`)
  - [x] **P2-E**: Nullable `Page` attributes for clean XML round-trips

- [x] **Stage 3: CLI & MCP Refactoring & Parity** (Branch: `refactor/cli-mcp-deduplication`) — **COMPLETED**
  - [x] **P3-A**: Deduplicate CLI/MCP scan/update handlers into `InkTag.Core`
  - [x] **P3-B**: Add `--recursive` option to CLI and MCP server
  - [x] **P3-C**: Dynamic assembly version derivation from assembly metadata

- [x] **Stage 4: Test Expansion & Wiki Documentation** (Branch: `docs/update-wiki-and-tests`) — **COMPLETED**
  - [x] Edge-case integration tests (rollback on failed move, XML validation)
  - [x] Update `docs/wiki/` documentation for CLI/MCP flag parity and schema updates

---

## Agent Rules (mandatory)

1. **Branching:** Never commit directly to `main`. Create a feature branch per task group (e.g. `fix/archive-extraction-safety`, `test/metadata-editor-safety`).
2. **Scope:** Only change files required for the assigned task. No drive-by refactors.
3. **Wiki:** When modifying code behavior, update the corresponding page under `docs/wiki/` and cross-link from `docs/wiki/index.md` if you add a new page.
4. **Tests:** Every behavior change in `InkTag.Core` must include or extend xUnit tests in `tests/InkTag.Tests/`.
5. **Verification:** Run `dotnet build InkTag.slnx` and `dotnet test` before marking a task complete.
6. **Do not commit** unless explicitly asked by the user.

---

## Priority 1 — Security (do first)

### P1-A · Fix inconsistent archive extraction (ZipSlip risk)

**Problem:** `ReadMetadata` uses safe extraction options; `EditMetadata` does not.

| File | Location |
|---|---|
| `src/InkTag.Core/MetadataEditor.cs` | `EditMetadata()` extraction loop (~line 131) |
| `src/InkTag.Core/MetadataEditor.cs` | `ReadMetadata()` extraction loop (~line 84) — reference implementation |

**Current (unsafe):**
```csharp
entry.WriteToDirectory(tempDir, new ExtractionOptions());
```

**Required change:**
```csharp
entry.WriteToDirectory(tempDir, new ExtractionOptions { Overwrite = true, ExtractFullPath = false });
```

**Additional hardening (recommended):** After extraction, verify every file under `tempDir` resolves to a path still contained within `tempDir` (use `Path.GetFullPath` comparison). If any entry escapes, throw `InvalidDataException` and abort before repack.

**Acceptance criteria:**
- [ ] Both `ReadMetadata` and `EditMetadata` use identical safe `ExtractionOptions`.
- [ ] New test: archive entry with `../` in the key does not write outside temp dir.
- [ ] Existing 16 tests still pass.

---

### P1-B · Gate stack traces in CLI JSON errors

**Problem:** `--json` error responses include full `stackTrace`, leaking internal paths.

| File | Location |
|---|---|
| `src/InkTag.Cli/Program.cs` | Top-level catch block (~line 56) |

**Required change:**
- Default JSON error shape: `{ "success": false, "error": "<message>" }`
- Add optional `--verbose` global flag; include `stackTrace` only when `--verbose` is set.

**Acceptance criteria:**
- [ ] Normal `--json` errors omit `stackTrace`.
- [ ] `--verbose --json` errors include `stackTrace`.
- [ ] Help output documents the new flag.

---

## Priority 2 — Correctness & Data Integrity

### P2-A · Surface CBR → CBZ conversion in GUI

**Problem:** `.cbr` files are repackaged as `.cbz` on save (intentional — SharpCompress cannot write RAR), but the GUI gives no warning and keeps stale `.cbr` paths in the grid.

| File | Notes |
|---|---|
| `src/InkTag.Core/MetadataEditor.cs` | Conversion logic at ~line 119 (`targetPath` assignment) |
| `src/InkTag.Gui/ViewModels/ComicItemViewModel.cs` | Holds `FilePath` / `FileName` |
| `src/InkTag.Gui/ViewModels/MainWindowViewModel.cs` | `SaveAllAsync()` — update path after successful save |

**Required changes:**
1. After a successful save where the output extension changed (`.cbr` → `.cbz`), update the ViewModel's displayed path on the UI thread.
2. Show a one-time or per-save status message: e.g. `"Converted issue1.cbr → issue1.cbz"`.
3. Optionally: prompt before first CBR save in a session (keep simple — status bar text is enough unless user prefers a dialog).

**Acceptance criteria:**
- [ ] Saving a `.cbr` file results in grid showing the new `.cbz` path.
- [ ] User sees feedback that conversion occurred.
- [ ] Test: edit a temp `.cbr` archive → output is `.cbz`, original `.cbr` removed.

---

### P2-B · Preserve Manga enum fidelity in GUI

**Problem:** `YesAndRightToLeft` is collapsed to a bool and lost on save.

| File | Location |
|---|---|
| `src/InkTag.Gui/ViewModels/ComicItemViewModel.cs` | `LoadFromModel()` ~line 92, `ApplyChangesToModel()` ~line 121 |

**Required change:** Replace the `bool Manga` property with a three-state representation matching `MangaDirection` (`Unknown`, `No`, `Yes`, `YesAndRightToLeft`). Options:
- Bind a `ComboBox` in the inspector/grid to the enum directly, **or**
- Keep a bool for quick toggle but store the original enum in a private field and only overwrite when the user explicitly changes Manga.

**Acceptance criteria:**
- [ ] Loading a comic with `<Manga>YesAndRightToLeft</Manga>` and saving without touching Manga preserves that value.
- [ ] Unit or integration test covers round-trip.

---

### P2-C · Warn on unknown JSON patch keys

**Problem:** Typos in patch keys (e.g. `"Writter"`) are silently ignored — bad for AI agents.

| File | Location |
|---|---|
| `src/InkTag.Core/MetadataEditor.cs` | `ApplyJsonPatch()` ~line 445 |
| `src/InkTag.Cli/Program.cs` | Update command JSON output |
| `src/InkTag.Mcp/Program.cs` | `update_comic_metadata` tool response |

**Required change:**
- Change `ApplyJsonPatch` to return a list of unrecognized property names (or add `ApplyJsonPatchWithReport` to avoid breaking callers).
- Include `"warnings": ["Unknown property 'Writter'"]` in CLI `--json` and MCP tool responses when warnings exist.

**Acceptance criteria:**
- [ ] Patch with one valid + one invalid key applies the valid key and reports the invalid one.
- [ ] Test covers unknown-key warning.

---

### P2-D · Flag read failures in scanner (don't silently show empty metadata)

**Problem:** Corrupt archives appear as blank rows; user may overwrite thinking fields are empty.

| File | Location |
|---|---|
| `src/InkTag.Gui/Services/ComicScannerService.cs` | catch block ~line 44 |
| `src/InkTag.Gui/ViewModels/ComicItemViewModel.cs` | Add error state properties |
| `src/InkTag.Gui/Views/MainWindow.axaml` | Optional: visual indicator (icon, row tint, tooltip) |

**Required changes:**
1. Add `bool HasReadError` and `string? ReadErrorMessage` to `ComicItemViewModel`.
2. In the scanner catch block, set these instead of silently using `new ComicInfo()`.
3. Exclude `HasReadError` rows from `CanSave` / bulk apply, or warn on save attempt.

**Acceptance criteria:**
- [ ] Corrupt archive row shows an error indicator.
- [ ] Save All skips or blocks rows with read errors.

---

### P2-E · Nullable `Page` attributes (metadata round-trip)

**Problem:** Primitive defaults on `Page` (`ImageWidth`, `ImageHeight`, `DoublePage`, `ImageSize`) can alter layout metadata on round-trip.

| File | Location |
|---|---|
| `src/InkTag.Core/ComicInfo.cs` | `Page` class ~line 163 |

**Required change:** Convert optional attributes to nullable types with `ShouldSerialize*` methods (same pattern as `ComicInfo.Count`, `ComicInfo.Year`, etc.).

**Acceptance criteria:**
- [ ] CBZ containing `<Page Image="0" Type="FrontCover"/>` (no width/height attrs) round-trips without injecting `ImageWidth="0"`.
- [ ] Test added.

---

## Priority 3 — Consistency & Maintainability

### P3-A · Extract shared CLI/MCP scan/update logic

**Problem:** ~150 lines duplicated between `InkTag.Cli/Program.cs` and `InkTag.Mcp/Program.cs`.

**Recommended approach:** Add a static helper class in `InkTag.Core`, e.g. `AgentOperations` or extend `MetadataEditor` with:
- `ScanDirectory(string path, string[] missingFields, bool recursive)`
- `UpdatePath(string path, string jsonPatch, bool dryRun)` → structured result DTO

Both CLI and MCP call the shared helpers; delete duplicated loops.

**Acceptance criteria:**
- [ ] CLI and MCP behavior unchanged (same JSON shapes).
- [ ] No duplicated scan/update/missing-field logic in CLI or MCP entrypoints.

---

### P3-B · Add recursive scan to CLI/MCP (parity with GUI)

**Problem:** GUI supports recursive scan; CLI/MCP use `SearchOption.TopDirectoryOnly`.

| File | Notes |
|---|---|
| `src/InkTag.Cli/Program.cs` | `HandleScanCommand`, `HandleUpdateCommand` |
| `src/InkTag.Mcp/Program.cs` | `scan_comics`, `update_comic_metadata` (directory branch) |

**Required change:** Add `--recursive` flag (CLI) and `recursive: boolean` param (MCP). Default `false` for backward compatibility.

**Acceptance criteria:**
- [ ] `--recursive` scans/updates nested subfolders.
- [ ] Default behavior unchanged when flag omitted.

---

### P3-C · Derive app version from assembly metadata

**Problem:** `UpdateService.CurrentAppVersion` is hardcoded (`0.4.4`) and can drift from CI/releases.

| File | Location |
|---|---|
| `src/InkTag.Gui/Services/UpdateService.cs` | ~line 39 |
| `src/InkTag.Gui/InkTag.Gui.csproj` | Ensure `<Version>` / `<InformationalVersion>` set |

**Required change:** Read version from `Assembly.GetExecutingAssembly().GetName().Version` or `[AssemblyInformationalVersion]`. CI already tags releases — align csproj version with tag at release time.

**Acceptance criteria:**
- [ ] About window and update checker show assembly version, not a manually maintained constant.

---

## Priority 4 — Test Coverage (expand safety net)

Add these tests to `tests/InkTag.Tests/MetadataEditorTests.cs` (or new files as appropriate):

| Test | What it proves |
|---|---|
| `EditMetadata_RollbackOnFailedMove` | Simulate move failure → original file restored from `.bak` |
| `EditMetadata_CbrConvertsToCbz` | Temp `.cbr` in → `.cbz` out, original removed |
| `EditMetadata_RejectsZipSlipEntry` | Entry with `../` path does not escape temp dir |
| `ValidateXml_SkipsWhenFileMissing` | No throw when `ComicInfo.xml` absent (create-new-metadata path) |
| `ValidateXml_ThrowsOnInvalidXml` | Malformed XML rejected before deserialize |
| `ApplyJsonPatch_WarnsOnUnknownKeys` | After P2-C implementation |

**Also update** `docs/wiki/testing.md` to reflect actual test inventory (remove claims about tests that don't exist yet; add them as you implement).

---

## Explicitly Out of Scope (do not implement unless asked)

- NuGet package publishing
- MCP registry submission
- Komga/Kavita integration
- External metadata scrapers (ComicVine, Metron)
- Multi-targeting `net8.0` (consider later)
- Replacing custom MCP server with official MCP C# SDK
- RAR write support (not feasible with SharpCompress)

---

## Suggested Implementation Order

```
P1-A  Archive extraction safety     ← security, smallest diff
P1-B  CLI stack trace gating
P2-C  JSON patch warnings           ← high value for AI agents
P2-D  Scanner read-error flags
P2-A  CBR→CBZ GUI feedback
P2-B  Manga enum preservation
P2-E  Page nullable attributes
P4    Integration tests (parallel with above where possible)
P3-A  CLI/MCP deduplication
P3-B  Recursive CLI/MCP scan
P3-C  Assembly version derivation
```

---

## Verification Checklist (run after all tasks)

```bash
dotnet build InkTag.slnx
dotnet test
# Manual smoke (optional):
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- read <sample.cbz> --json
dotnet run --project src/InkTag.Cli/InkTag.Cli.csproj -- update <sample.cbz> --patch '{"Writer":"Test"}' --dry-run --json
```

- [ ] Build: 0 warnings, 0 errors
- [ ] Tests: all pass (count should exceed 16)
- [ ] No `.bak` files left after successful save
- [ ] Wiki pages updated for any behavior changes

---

## Reference: Key Files

| Component | Path |
|---|---|
| Core edit engine | `src/InkTag.Core/MetadataEditor.cs` |
| Domain model | `src/InkTag.Core/ComicInfo.cs` |
| CLI entrypoint | `src/InkTag.Cli/Program.cs` |
| MCP server | `src/InkTag.Mcp/Program.cs` |
| GUI ViewModel | `src/InkTag.Gui/ViewModels/MainWindowViewModel.cs` |
| GUI item VM | `src/InkTag.Gui/ViewModels/ComicItemViewModel.cs` |
| Scanner | `src/InkTag.Gui/Services/ComicScannerService.cs` |
| Tests | `tests/InkTag.Tests/` |
| Prior reviews | `docs/code-review-report.md`, `docs/code-review-notes.md` |

# InkTag — Review Remediation Plan

**Generated:** 2026-09-01
**Source:** Whole-codebase review of `main` @ `79cb9f5` (v0.13.0)
**Audience:** AI coding agent (or maintainer) implementing the fixes
**Build baseline:** `dotnet build InkTag.slnx` (0 warnings) · `dotnet test` (207 passing at time of review)

---

## Mission

Close a safety hole and a protocol-corruption bug in the MCP server, fix `HttpClient` and
image-memory handling that does not scale to bulk operations over large libraries, and harden
archive repacking and on-disk persistence. Make focused, minimal diffs — **do not redesign the
architecture**. Every phase is an independent branch that builds green and ships on its own.

Per [CLAUDE.md](../CLAUDE.md): work on a dedicated branch (never `main`), update
[CHANGELOG.md](../CHANGELOG.md) `[Unreleased]` and the relevant [`docs/wiki/`](wiki/) pages in
the same branch, and open a GitHub issue for every deferred item.

---

## Findings Summary

| ID | Sev | Finding | Primary location |
| :-- | :-- | :-- | :-- |
| H1 | High | MCP `RemoveComicPage` skips the read-only guard and defaults `dryRun=false` | `src/InkTag.Mcp/ComicTools.cs:199` |
| H2 | High | MCP server logs to **stdout**, corrupting the JSON-RPC stdio stream | `src/InkTag.Core/Logging/AppLogger.cs:108`, `src/InkTag.Mcp/Program.cs:31` |
| H3 | High | `HttpClient` created per candidate in the scrape hot path → socket exhaustion | `src/InkTag.Core/Scrapers/MetadataScraperService.cs:189`, `RateLimitedHttpClient.cs:22` |
| M1 | Med | `LruImageCache` never disposes evicted bitmaps (docstring says it does) | `src/InkTag.Gui/Services/LruImageCache.cs:57` |
| M2 | Med | Archive repack flattens folders + `Overwrite=true` → silent page loss | `src/InkTag.Core/ArchiveSwapService.cs:70`, `ComicArchiveHandler.cs:482` |
| M3 | Med | Scraper disk cache never persists for one-shot CLI/MCP runs (2s debounce, no dispose) | `src/InkTag.Core/Scrapers/ScraperCacheService.cs:80`, `MetadataScraperService.cs:32` |
| M4 | Med | Scrape applies metadata twice with two different merge modes | `src/InkTag.Mcp/ComicTools.cs:318`, `MetadataScraperService.cs:265` |
| M5 | Med | Bulk scrape retains every cover image in memory for the whole run | `src/InkTag.Core/Scrapers/BulkScrapeQueueService.cs:38` |
| M6 | Med | ComicVine / Komga secrets written to `settings.json` in plaintext, default perms | `src/InkTag.Core/Configuration/AppSettings.cs:166` |
| M7 | Med | `RestoreBatchJob` documented as "atomic" but is a best-effort file-by-file loop | `src/InkTag.Core/Backup/MetadataBackupService.cs:291` |
| M8 | Med | Manifest / settings / cache writes are non-atomic and fail silently | `src/InkTag.Core/Backup/MetadataBackupService.cs:375` |
| L1 | Low | Manifest fully re-serialized and rewritten on every archive edit | `MetadataBackupService.cs:139` |
| L2 | Low | `AppLogger` reopens + stats the log file per line under a global lock | `AppLogger.cs:103` |
| L3 | Low | Page stripping takes no provenance snapshot; fixed `.bak` name clobbers / collides | `ComicArchiveHandler.cs:625` |
| L4 | Low | Hard-coded "Buffy" title rewrites in a general-purpose service | `MetadataScraperService.cs:54` |
| L5 | Low | `GenerateTaggingNote` version fallback literal `"0.12.0"` (project is 0.13.0) | `ComicVineProvider.cs:600` |
| L6 | Low | Komga defaults to plaintext `http://` then sends `X-API-Key` / Basic auth over it | `KomgaClient.cs:64` |
| L7 | Low | `SanitizeFilename` misses Windows reserved device names + trailing dot/space | `ComicFileRenamer.cs:327` |
| L8 | Low | ~800 lines of hand-rolled CLI arg parsing; no `--opt=value` support | `src/InkTag.Cli/Program.cs` |
| L9 | Low | ImageSharp decodes untrusted archive / remote images with no pixel cap | `PerceptualHashService.cs:38` |

### What's working well (do not regress)

- Clean façade/service layering (`MetadataEditor` over `ArchiveSwapService`, `ComicArchiveHandler`, `ComicInfoXmlSanitizer`).
- XXE-safe XML (`DtdProcessing.Ignore`, null resolver), cached static `XmlSerializer`, regex fallback parser.
- Multi-tier archive reader (fast seek → `NonSeekableStream` → SharpCompress) with `CancellationToken` threaded throughout.
- Backup / provenance model: pre-write XML snapshots, cover dHash, match confidence, field diffs, batch grouping.
- MCP path allow-listing + read-only mode (applied on every tool **except** H1).
- `KomgaClient` uses the correct `HttpClient` + `SocketsHttpHandler` ownership pattern.
- dHash implementation correct (9×8 → 64 bits, hardware `PopCount`), test-covered.

---

## Staged Execution Status

- [ ] **Phase 1: MCP Safety & Logging Hotfix** — Branch: `fix/mcp-safety-and-logging`
- [ ] **Phase 2: Scraper HTTP & Lifecycle** — Branch: `refactor/scraper-http-lifecycle`
- [ ] **Phase 3: Scrape Merge Semantics** — Branch: `refactor/scrape-merge-semantics`
- [ ] **Phase 4: Archive Repack Integrity** — Branch: `fix/archive-repack-structure`
- [ ] **Phase 5: Persistence Durability** — Branch: `fix/persistence-durability`
- [ ] **Phase 6: GUI Image Memory** — Branch: `fix/gui-image-memory`
- [ ] **Phase 7: Polish Bundle** — Branch: `chore/review-polish-bundle`

Per-phase checklist: branch → implement → add/adjust tests → `dotnet test` green → CHANGELOG
`[Unreleased]` → relevant `docs/wiki/*` → PR.

Suggested release mapping: Phase 1 as a standalone `0.13.1` patch; Phases 2–3 as one scraper
work-stream; Phases 4 and 6 each get a dedicated review; Phases 5 and 7 are quick.

---

## Phase 1 — MCP Safety & Logging Hotfix

**Branch:** `fix/mcp-safety-and-logging` — no dependencies, ship first.

### P1-A · H1 — Guard `RemoveComicPage`
- [ ] `src/InkTag.Mcp/ComicTools.cs` (`RemoveComicPage`, ~line 199): change the parameter default to
      `bool dryRun = true`.
- [ ] Add `if (!dryRun) { EnsureWriteAccess("RemoveComicPage"); }` after `ValidatePathAccess(path)`.
- [ ] Update the `[Description]` string to the "Defaults to dryRun=true (preview only). Set
      dryRun=false to commit changes." wording used by the other write tools.
- [ ] Test (`McpSecurityAndBackupTests`): `ReadOnlyOverride = true` + `dryRun: false` throws
      `UnauthorizedAccessException`; `dryRun: true` still returns a preview.

### P1-B · H2 — Logger off the protocol stream
- [ ] `src/InkTag.Core/Logging/AppLogger.cs` (`Log`, ~line 108): `Console.WriteLine` →
      `Console.Error.WriteLine`. Diagnostics belong on stderr for the CLI and GUI too; the CLI
      emits its real output through its own `Console.WriteLine` calls, not `AppLogger`.
- [ ] `src/InkTag.Mcp/Program.cs` (~line 27): also
      `builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)` (or clear
      providers) as defence against the host's default console logger.
- [ ] Verify no test in the suite asserts on captured **stdout** log text (`AppLoggerTests` reads
      the file sink — safe).
- [ ] Wiki: `docs/wiki/cli_mcp.md` — note stdio purity / stderr logging.

---

## Phase 2 — Scraper HTTP & Lifecycle

**Branch:** `refactor/scraper-http-lifecycle` — H3 + M3 (same files).

### P2-A · H3 — One shared `HttpClient`
- [ ] New `src/InkTag.Core/Net/SharedHttpClient.cs`: a static `HttpClient` over a
      `SocketsHttpHandler` (`PooledConnectionLifetime = TimeSpan.FromMinutes(2)`,
      `AutomaticDecompression`), default `User-Agent`.
- [ ] `RateLimitedHttpClient.cs:22`: `_client = httpClient ?? SharedHttpClient.Instance`; keep the
      ctor parameter for test injection; stop allocating (and leaking) a client per instance.
- [ ] `MetadataScraperService.cs:189`: delete `using var client = new HttpClient()` in the
      per-candidate loop; use `SharedHttpClient.Instance`.
- [ ] `BulkScrapeQueueService.cs:108`: default the injected client to `SharedHttpClient.Instance`;
      never dispose a client it does not own.

### P2-B · M3 — Persist the scraper cache
- [ ] Make `ComicVineProvider` and `MetadataScraperService` `IDisposable`; `Dispose()` calls
      `ScraperCacheService.Flush()` synchronously.
- [ ] Wrap in `using` at call sites: CLI `HandleScrapeCommand`, and MCP `SearchExternalMetadata`,
      `ScrapeComicMetadata`, `BulkScrapeDirectory`.
- [ ] Test: `Set` → `Dispose` → new `ScraperCacheService` over the same temp path reads the entry
      back within `maxAge`.

### Verification
- [ ] `ScraperTests` (fake handler / provider injection) stays green.
- [ ] CHANGELOG `### Fixed`; wiki `docs/wiki/architecture.md` scraper section.

---

## Phase 3 — Scrape Merge Semantics

**Branch:** `refactor/scrape-merge-semantics` — M4. Do after Phase 2 (same file, larger blast radius).

- [ ] `MetadataScraperService.AutoScrapeComicAsync`: stop mutating the caller's `ComicInfo`.
      Populate a new `ScrapeResult.FetchedMetadata` and leave all merging to the single
      `ApplyMetadata` call performed at write time (which owns the merge mode).
- [ ] Audit and update callers of `AutoScrapeComicAsync` / `ScrapeResult.TargetComic`:
      CLI `HandleScrapeCommand`, MCP `ScrapeComicMetadata`, GUI `ScraperMatchWindow` /
      `MainWindowViewModel`, `ScraperTests`.
- [ ] Tests: explicit `fill-missing` vs `overwrite` assertions on the applied result; a
      "Notes attribution line is not appended twice" case.

---

## Phase 4 — Archive Repack Integrity

**Branch:** `fix/archive-repack-structure` — M2 + L3. Highest regression risk; budget for new fixtures.

### P4-A · M2 — Preserve archive structure
- [ ] `ArchiveSwapService.cs:70` and `ComicArchiveHandler.cs:482`: `ExtractFullPath = false` →
      `true` (keep `Overwrite = true`).
- [ ] **Ripple:** `ComicArchiveHandler.RemoveArchivePages` (~line 499) enumerates images with
      `SearchOption.TopDirectoryOnly` and looks for `ComicInfo.xml` only at the temp-dir root —
      make both recursive so nested archives are not silently emptied. Align with
      `EditMetadata`'s existing recursive handling.
- [ ] Keep the post-extraction zip-slip check; add a per-entry `..` / rooted-path check before
      extraction.
- [ ] Tests: nested-folder CBZ fixtures — `EditMetadata` round-trip preserves entry structure and
      page count; `RemoveArchivePages` on a foldered archive removes exactly one page. All 207
      existing tests stay green.

### P4-B · L3 — Page-strip provenance & backup naming
- [ ] In `RemoveArchivePages`: read the existing `ComicInfo.xml` and call
      `MetadataBackupService.CreateBackup(filePath, originalXml, "RemoveArchivePages", ...)` before
      mutating.
- [ ] Replace the fixed `filePath + ".bak"` with a GUID-suffixed name (as `EditMetadata` already
      does); stop deleting a pre-existing user file that happens to match the `.bak` name.
- [ ] Test (`BatchRollbackAndProvenanceTests` style): `StripFirstPage` creates a listable backup
      entry.

---

## Phase 5 — Persistence Durability

**Branch:** `fix/persistence-durability` — M8 + M6 + M7 (wording).

### P5-A · M8 — Atomic writes
- [ ] New `src/InkTag.Core/IO/AtomicFile.cs`: `WriteAllText(path, content)` writes
      `path + ".tmp-" + Guid` then `File.Move(tmp, path, overwrite: true)`.
- [ ] Apply in `MetadataBackupService.SaveManifestInternal`, `AppSettingsService.SaveSettings`,
      `ScraperCacheService.Flush`.
- [ ] Change the silent catch-all load paths (`LoadManifestInternal`, `LoadSettings`, `LoadCache`)
      to `AppLogger.LogWarning` before returning the empty fallback.
- [ ] Tests: `AtomicFile` round-trip; a deliberately corrupt manifest logs a warning and does not
      silently discard history without a trace.

### P5-B · M6 — Lock down secrets file
- [ ] After writing `settings.json`, on non-Windows: `File.SetUnixFileMode(path, UserRead |
      UserWrite)` and the config dir to `UserRead | UserWrite | UserExecute`.
- [ ] Test guarded to `!OperatingSystem.IsWindows()`.

### P5-C · M7 — Honest rollback wording
- [ ] Reword the `RestoreBatchJob` XML doc and the MCP `[Description]`: drop "atomically", state
      "restores each affected archive, continuing past individual failures; see the returned
      failure list".
- [ ] Open a GitHub issue for true staged-swap atomicity (see Deferred).

---

## Phase 6 — GUI Image Memory

**Branch:** `fix/gui-image-memory` — M1 + M5. Each needs a short investigation first.

### P6-A · M1 — Dispose evicted bitmaps
- [ ] Investigate cover-bitmap binding in `MainWindowViewModel` / `ComicItemViewModel`: a bitmap
      bound to a visible `Image.Source` must not be disposed while displayed.
- [ ] `LruImageCache.Set` eviction and `Clear()`: dispose the removed `Bitmap`. Add a "pinned key"
      the VM sets for the current on-screen cover, exempt from eviction.
- [ ] Consider generalising to `LruImageCache<T> where T : IDisposable` so eviction/disposal is
      unit-testable (the concrete `Avalonia...Bitmap` type cannot be faked).
- [ ] Tests (`LruImageCacheTests`): eviction disposes the removed value; the pinned key survives an
      over-capacity insert.

### P6-B · M5 — Release cover bytes
- [ ] After `item.LocalCoverHash` is computed in the producer (and after any intro-page fallback
      that needs the bytes), set `item.LocalCoverBytes = null`.
- [ ] Check `BulkScrapeItemViewModel` — if the queue window renders a thumbnail from those bytes,
      keep only a downscaled copy, or retain bytes for on-screen rows only.
- [ ] Add a `BulkScrapeOptions.DiscardCoverBytesAfterHash` flag (default `true` for CLI/MCP, which
      have no UI).

---

## Phase 7 — Polish Bundle

**Branch:** `chore/review-polish-bundle` — L4–L7, L9. Low-risk, batchable.

- [ ] **L5** `ComicVineProvider.cs:600` — resolve the version from the assembly unconditionally;
      remove the `"0.12.0"` literal (consider a shared `InkTagVersion` constant).
- [ ] **L4** `MetadataScraperService.cs:54` — move the "Buffy" title rules into a
      `SeriesAliasResolver` backed by a small data table.
- [ ] **L6** `KomgaClient.CleanServerUrl` — `AppLogger.LogWarning` when it injects `http://` for a
      schemeless URL.
- [ ] **L7** `ComicFileRenamer.SanitizeFilename` — reject/rewrite Windows reserved device names
      (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) and trim trailing dots/spaces;
      add `RenamingTests` cases.
- [ ] **L9** `PerceptualHashService.ComputeDHash` — `Image.Identify` first and reject images over
      ~40 MP before `Image.Load<L8>`.

---

## Deferred — open as GitHub issues

Per CLAUDE.md §1.4: `gh issue create --title "..." --body "..."`.

- **L2 — `AppLogger` buffered file I/O.** Keep a `StreamWriter` open (or a flush timer) instead of
  `File.AppendAllText` + `FileInfo` stat per line; rotation must close/reopen the handle.
- **L1 — Manifest write amplification.** `MetadataBackupService.CreateBackup` rewrites the whole
  (capped-1000) manifest per edit; move to an append-oriented journal or a batched flush.
- **M7 (full) — True atomic batch rollback.** Stage every XML rewrite to a temp CBZ, then swap them
  all with a recovery journal so a mid-run crash leaves either the old or the new state, never a
  mix.
- **L8 — CLI arg-parser migration.** Move `src/InkTag.Cli/Program.cs` to `System.CommandLine`;
  removes ~800 lines and adds `--opt=value` support.

---

## Change Log for this document

| Date | Change |
| :-- | :-- |
| 2026-09-01 | Initial plan from the `main` @ `79cb9f5` whole-codebase review. |

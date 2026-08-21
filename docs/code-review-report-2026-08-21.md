# InkTag Code Review Report

**Project:** InkTag
**Repository:** `aurora7795/InkTag`
**Review Date:** August 21, 2026
**Branch:** `agents/configure-copilot-deepseek-integration`
**Status:** **HEALTHY** (no critical blockers; several medium/low items recommended for follow-up)

---

## 1. Executive Summary

**Strong, well-architected project.** The codebase is clean, idiomatic C# 10/.NET 10, with excellent separation of concerns across the four projects (Core, CLI, MCP, GUI). The test suite is genuinely impressive — broad coverage, real-file integration tests, and security-focused tests (zip-slip, path confinement). The recent work on GVFS/FTP streaming fallbacks and perceptual cover matching is well-engineered.

The findings below are organized by severity. None are critical blockers, but several are worth addressing.

---

## 2. Review Findings

### 🔴 High Priority

#### A. Unbounded image caches & `Bitmap` leaks (GUI)
`BulkScrapeItemViewModel`, `SeriesItemViewModel`, and `CandidateItemViewModel` all use **static, unbounded `ConcurrentDictionary<string, Bitmap>` caches**. Avalonia `Bitmap` implements `IDisposable` and holds native/GPU resources. Across many scrapes or series searches, this will grow without limit and leak decoder/GPU memory.

**Fix:** Bound the caches (LRU, like `ArchiveCoverService` already does with its 50-item cap) and dispose bitmaps on eviction.

#### B. Per-request `HttpClient` creation (socket exhaustion)
`SeriesItemViewModel` and `CandidateItemViewModel` create a **new `HttpClient` per thumbnail load** (`using var client = new HttpClient()`). Under heavy series browsing this can exhaust sockets. `BulkScrapeItemViewModel` correctly uses a shared static client — the other two should follow suit.

#### C. `UpdateService` static mutable state is not thread-safe
`_cachedUpdateInfo`, `_cachedPortableResult`, `_lastCheckTime`, and `_resolvedAppVersion` are static mutable fields with no synchronization. `_resolvedAppVersion ??=` is a non-atomic read-modify-write. Concurrent `CheckForUpdatesAsync` calls can race.

---

### 🟠 Medium Priority

#### D. `SolidColorBrush` allocated on every getter (GUI perf)
`BulkScrapeQueueViewModel.ApplyButtonBackground`, `BulkScrapeItemViewModel.StatusBadgeBackground`/`VisualBadgeBackground`, and `RenameItemPreviewViewModel.StatusBadgeBackground`/`ProposedFilenameForeground` all allocate a **new brush on every property access**. These are read frequently during property-change notifications. Cache them or use `IValueConverter`/XAML resources.

#### E. `ComicSearchResult` mixes transport data with computed analytics
`MatchConfidence`, `CoverHash`, and `VisualSimilarity` are mutable properties on a model shared across threads (the VM mutates them directly). This is both a **concurrency concern** and a **separation-of-concerns** issue. Consider splitting raw API data from computed match analytics, or making the model immutable.

#### F. `AppLogger` opens/closes the file on every log call
`File.AppendAllText` opens and closes the file per call. Under heavy debug logging (which the scanner does per-file), this is a performance bottleneck. Consider a buffered writer or a `StreamWriter` with periodic flush. Also, **no log rotation** — the log grows unbounded.

#### G. `BulkScrapeQueueViewModel` constructor does heavy work
Creates services inline (poor testability), calls `CreateQueue`, and builds VMs in the constructor. Also `ApplyMatchedAsync` passes **all items** (`Items.Select(i => i.Item)`) rather than only selected/matched ones, relying on the service to filter — a latent risk of writing unselected items.

#### H. `EffectiveMergeMode` magic-number coupling
`_selectedMergeModeIndex == 1 ? OverwriteAll : FillMissingOnly` couples the VM to the combo-box ordering. Fragile if the UI reorders options.

#### I. `ScraperCacheService.Get` has a read-side-effect
`Get` mutates the cache (`TryRemove` on expired entries) and sets `_isDirty` — a read operation causing a write. Also no eviction policy beyond maxAge-on-read; stale entries accumulate on disk.

---

### 🟡 Low Priority / Nits

#### J. `ViewModelBase` is effectively dead code
It's an empty subclass of `ObservableObject`, and most VMs inherit `ObservableObject` directly instead. Either remove it or give it shared infrastructure (`IsBusy`, navigation).

#### K. `BulkEditCatalog.AllFields` is a mutable static array
Should be `IReadOnlyList`/`ReadOnlySpan` to prevent accidental replacement.

#### L. `Number` typed as `String` while `Volume`/`Count`/`Year` are `Numeric` in the bulk-edit catalog
Defensible (issue numbers contain decimals/fractions), but inconsistent and worth a comment.

#### M. `AgeRating` enum options mix vocabularies
Includes both ComicVine-style ("Everyone 10+") and standard ("PG", "M") values in one list.

#### N. `RenamePreviewViewModel.GeneratePreviews()` rebuilds the entire collection on every keystroke
Expensive for large batches and loses selection state. Debounce or incremental update.

#### O. `RenameTemplates` duplicated
Defined in both `BulkScrapeQueueViewModel` and `RenamePreviewViewModel` — DRY violation, two sources of truth.

#### P. `RateLimitedHttpClient` static `SemaphoreSlim`/`_lastRequestTime`
Static rate-limit state shared across all instances — fine for a single-process app, but worth documenting as intentional.

#### Q. `ComicVineProvider` API key threaded through every method
A provider-scoped configuration would be cleaner than passing `apiKey` to every call.

---

## 3. Strengths

- **Excellent security awareness:** zip-slip protection in `EditMetadata`, path confinement in MCP `ValidatePathAccess`, XML sanitization against invalid control chars and XXE (`DtdProcessing.Ignore`).
- **Robust archive handling:** the fast-path → sequential `NonSeekableStream` → SharpCompress fallback chain for GVFS/FTP/FUSE mounts is well-designed and well-tested.
- **Atomic-like save with rollback:** `EditMetadata` backs up originals, validates the repackaged archive, and rolls back on failure.
- **Strong test suite:** real temp CBZ archives, mock HTTP handlers, mock scraper providers, and concurrency tests. `MetadataEditorTests` and `ScraperTests` are particularly thorough.
- **Good MVVM discipline:** `[ObservableProperty]`/`[RelayCommand]` source generators used consistently; UI logic properly delegated to ViewModels.
- **Clean interface abstraction** (`IMetadataScraperProvider`) enabling testability.

---

## 4. Test Coverage Gaps (worth adding)

1. **`UpdateService` core logic is untested** — `CheckForUpdatesAsync`, `CheckGitHubReleasesFallbackAsync`, `DownloadAndApplyUpdateAsync` are the highest-risk untested code.
2. **`RateLimitedHttpClient` rate-limiting/backoff** behavior is untested.
3. **`ExecuteBatchRename`** (batch execution path) is untested — only preview and single-file rename.
4. **`BulkEditEngineTests`** missing `Clear`/`Prepend` operations.
5. **`ScraperCacheService` disk persistence** (debounced flush/reload) is untested.
6. **`ValidatePathAccess`** prefix-bypass case (e.g. `/allowed_root_evil`) is untested.
7. **`MetadataEditorTests.EditMetadata_RejectsZipSlipEntry`** — the name is misleading; it actually verifies *sanitization* (extracts as `evil.txt` within tempDir), not rejection.

---

## 5. Suggested Next Steps

1. **Fix the high-priority items** — bound the image caches + dispose bitmaps, share the `HttpClient`, and make `UpdateService` thread-safe.
2. **Add the missing tests** for `UpdateService`, `RateLimitedHttpClient`, and `ExecuteBatchRename`.
3. **Create GitHub issues** for the deferred items per the project's `CLAUDE.md` conventions.

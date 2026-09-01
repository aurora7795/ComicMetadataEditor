# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Review Remediation Plan**: Added [`docs/review-remediation-plan-2026-09-01.md`](docs/review-remediation-plan-2026-09-01.md) — a phased, branch-mapped implementation plan for the findings of the `main` @ `79cb9f5` whole-codebase review (3 high, 8 medium, 9 low), covering MCP safety, stdio-stream purity, `HttpClient` lifecycle, archive-repack structure preservation, and on-disk persistence durability.

---

## [0.13.0] - 2026-08-29

### Added
- **Provider/Scanner Title Page Detection & Fallback Matching**: Multi-tier perceptual cover hashing evaluating Page 1 when Page 0 visual similarity drops below 70% or confidence threshold, matching comic covers obscured by scanner credits or provider intro advertisements.
- **Natural Numeric Image Entry Sorting**: High-performance zero-allocation `NaturalStringComparer` for natural numeric sorting of archive image entries (`00_intro.jpg`, `01_cover.jpg`, `page1.jpg`, `page2.jpg`, `page10.jpg`).
- **Atomic Archive Page Stripping & Removal**: Clean removal of provider intro pages (`StripFirstPage`, `RemoveArchivePages`) with temp extraction, ZipSlip path validation, ComicInfo.xml `PageCount` decrement and `Pages` collection renumbering, archive repack, and `.bak` safety rollback.
- **Bulk Auto-Tag CBR ➔ CBZ Indicators & Badges**: Displayed dynamic amber `CBR ➔ CBZ` badges in the `Archive File / Detected Query` column of the Bulk Scrape queue with explanatory conversion tooltips.
- **Bulk Auto-Tag Pre-Save Confirmation Dialog**: Added `BulkApplyConfirmWindow` which prompts the user before applying bulk metadata changes whenever CBR repacking or automated file renaming (`AlsoRenameFiles`) is enabled, showing a live summary of all converted and renamed files.
- **CLI Commands & Flags**:
  - Added `strip-intro <file|dir> [--dry-run]` to strip provider/scanner intro pages from archives.
  - Added `remove-page <file> --index <n> [--dry-run]` to remove specific pages by 0-based index.
  - Added `--page <n>` option to `cover` command to extract arbitrary page images.
  - Added `--cover-page <n>` and `--strip-intro-page` options to `scrape` command.
- **MCP Server Tools & Enhancements**:
  - Added `remove_comic_page` tool for AI agent page removal with dry-run support.
  - Enhanced `extract_cover_image` with `pageIndex` parameter.
  - Enhanced `scrape_and_apply_metadata` and `bulk_scrape_and_apply` with `detectIntroPage`, `coverPageIndex`, and `stripIntroPages` options.
- **GUI Scraper Match Page Switcher & Stripping**:
  - Interactive page navigation (`◀ Page X/Y ▶`) in `ScraperMatchWindow` with live visual similarity re-ranking against candidate covers.
  - Added `[x] Remove provider intro page on apply` checkbox in `ScraperMatchWindow`.
  - Added `Page 2 Cover` badge on bulk scrape queue items where cover was matched on page 2.
  - Added `[x] Strip Detected Intro Pages` checkbox in `BulkScrapeQueueWindow` toolbar.
  - Added multi-page cover bitmap and perceptual hash caching in `ArchiveCoverService`.
- **Full-Series Background Cover Matching & Unified Issue List in Series Wizard**:
  - Automatically scans across all issues/pages in a selected series in the background (up to 500 issues per volume) with early-exit upon discovering $\ge 90\%$ visual similarity, eliminating manual pagination when matching covers for later issues in long runs.
  - Replaces rigid page buttons in Step 2 with a unified, continuous scrollable issue list that dynamically sorts and pins high-confidence visual matches ($\ge 70\%$) at the top with the ⭐ **Top Visual Match** badge and automatically selects $\ge 85\%$ matches.
  - Added a quick real-time filter box in Step 2 to search/filter issues by number or title across the entire series.
  - Added live local comic cover preview thumbnails in the Step 2 header and bottom action bar with side-by-side comparison (`[Local Cover] vs [Selected Candidate]`) and hover-to-zoom tooltips.
  - Added `[x] Rematch remaining unmatched comics in queue` checkbox in `SeriesSearchWizardWindow` when launched from the bulk auto-tag queue, automatically re-matching all remaining unmatched, low-confidence, or error queue items against the selected series volume.
  - Added direct `🧙 Series Search Wizard...` context menu option in `BulkScrapeQueueWindow`.
  - Added full-content hover tooltips (`ToolTip.ShowDelay="100"`) across all text fields in `BulkScrapeQueueWindow` (including Archive File Name, Detected Query, Matched Series Title, and Matched Issue Title), allowing users to immediately inspect full untruncated strings on hover.
  - Added native support for **Chronological & Reading-Order Pack filenames** (e.g., `YYYYMMDD - Franchise, Season X #Y - Arc Title, Part Z.cbz` and `YYYY-MM-DD - Series #X - Story.cbz`):
    - Automatically strips 8-digit ISO dates (`YYYYMMDD - ` / `YYYY-MM-DD - `) and extracts the 4-digit release year.
    - Accurately decomposes franchise season-arc structures (e.g. `Angel, Season 5 #14 - Smile Time, Part II` $\rightarrow$ `Angel: Smile Time #2`, `Buffy, Season 0 #1 - The Origin, Part I` $\rightarrow$ `Buffy: The Origin #1`).
    - Converts Roman numeral part designations (`Part I`, `Part II`, `Part III`, etc.) to issue numbers.
    - Expands series volume searches in ComicVine scraper with arc aliases and franchise parent volumes.

### Fixed
- **Dynamic File Name Column Layout with Hugging Format Badges**: Replaced fixed-width truncation in the DataGrid `File Name` column with `HuggingBadgePanel`, allowing filename text to expand dynamically as the column is resized while keeping format badges (`CBR`, `CBR ➔ CBZ`) hugging the right side of the text and gracefully truncating long filenames with ellipsis only when column boundaries are reached.
- **Bulk Scrape Queue Window Layout & Minimum Size Enforcement**: Enforced responsive minimum window dimensions (`MinWidth="1160"`, `MinHeight="580"`) and refactored the bottom actions bar layout into auto-sized control columns with an elastic spacer, preventing the `Strip Detected Intro Pages` checkbox and adjacent controls from getting compressed or bunched together on narrower window widths.
- **Inspector ScrollBar Viewport Padding**: Added right viewport padding (`Padding="0,0,12,0"`) to `ScrollViewer` containers across the sidebar tabs (`Details`, `Bulk Edit`, `Bulk Tools`), preventing textboxes and input controls from colliding with or sitting proudly beneath the vertical scrollbar.
- **DataGrid Column Header MinWidth Constraints**: Configured explicit `MinWidth` constraints and adjusted default widths across all 35 DataGrid columns in `MainWindow.axaml` and `BulkScrapeQueueWindow.axaml`, ensuring column headers (e.g. `Black & White`, `Age Rating`, `Language ISO`, `Page Count`, `Untagged`, `Manga (RTL)`) are never truncated on initial load or during interactive column resizing.
- **Prevent Premature Bitmap Disposal During Cache Eviction**: Removed aggressive manual `.Dispose()` invocations on evicted image cache items in `ArchiveCoverService` and `LruImageCache`, preventing UI layout passes (`Image.MeasureOverride`) from encountering disposed unmanaged handles during browsing and resizing.
- **Bulk Scrape Queue & Rename Error Tooltips**: Added interactive hover tooltips (`ToolTip.Tip="{Binding StatusTooltip}"`) on status badges in `BulkScrapeQueueWindow` and `RenamePreviewWindow`, surfacing the exact error or collision message when hovering over `Error` or `Collision` badges.
- **Dynamic Single vs Bulk Auto-Tag Control Enabling**: Automatically disables the singular `Auto-Tag` toolbar button, context menu items, and Tools menu commands whenever multiple items are selected in the main comic DataGrid with a helpful tooltip redirecting to `Bulk Auto-Tag`, and re-enables singular auto-tagging when exactly 1 item is chosen.
- **Bulk Auto-Tag Retry for Errored Items**: Fixed an issue in `BulkScrapeQueueViewModel` where clicking `Apply Matched to Comic Archives` a second time failed to attempt saving remaining errored items due to an overly restrictive status filter. Errored items with matched candidates remain selected and are now properly re-evaluated and retried on subsequent apply clicks.

---

## [0.12.3] - 2026-08-24

### Added
- **CBR ➔ CBZ Conversion Transparency & Pre-Save Notices**:
  - **Dynamic In-Grid Format Badges**: Added responsive format pill badges in the DataGrid `File Name` column displaying a subtle gray `CBR` badge on unmodified CBR files that dynamically transitions to an amber `CBR ➔ CBZ` conversion badge when modified (`IsDirty`).
  - **Comprehensive Explanatory Tooltips**: Added informative tooltips explaining why CBR (RAR) files are repacked into modern, open-standard CBZ (ZIP) files upon saving.
  - **Inspector Details Notice**: Displays the format badge and conversion reminder in the File Information sidebar panel.
  - **Pre-Save Confirmation Dialog**: Added `CbrConversionConfirmWindow` modal displaying a list of all affected CBR files prior to saving, with a "Do not ask again" preference checkbox.
  - **Configuration Setting**: Added `ConfirmCbrToCbzConversion` setting to `AppSettings` and the Settings GUI window under General preferences.
- **Core Refactoring & Modernization (Code Review Implementation)**:
  - **Modularized Core Services**: Extracted `ComicInfoXmlSanitizer` (XML sanitization, coercion, and validation), `ArchiveSwapService` (atomic swap, rollback safety, integrity validation), and `ComicArchiveHandler` (multi-tiered ZIP seek, non-seekable streams, SharpCompress fallback). `MetadataEditor` acts as a unified façade maintaining 100% backward compatibility.
  - **Custom Domain Exception Hierarchy**: Added `InkTagException` base class and specialized domain exceptions (`ComicArchiveException`, `ComicArchiveCorruptException`, `MetadataXmlSanitizationException`, `UnsafeArchiveEntryException`) under `InkTag.Core.Exceptions`.
  - **Native Async API Overloads**: Implemented `ReadMetadataAsync`, `EditMetadataAsync`, `BulkEditMetadataAsync`, `ExtractCoverImageBytesAsync`, and `GetCoverHashAsync` with `CancellationToken` propagation and progress reporting.
  - **Comprehensive XML Documentation**: Added standard `/// <summary>` docstrings across all public schema properties in [`ComicInfo.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/ComicInfo.cs).

### Changed
- **Save Status Bar Progress Reporting**: `MainWindowViewModel.SaveAllAsync` now explicitly reports CBR ➔ CBZ conversion counts during batch saves (e.g. `Saving (1/3): Issue.cbr (CBR ➔ CBZ)...`) and in the final completion message.
- **Cached `XmlSerializer` Instances**: `ComicInfoXmlSanitizer` now caches static `XmlSerializer` instances, eliminating runtime dictionary lookups and heap allocations per read/write cycle.
- **Lazy Directory File Enumeration**: Replaced `Directory.GetFiles` with `Directory.EnumerateFiles` across `MetadataEditor`, `AgentOperations`, and `ComicScannerService` to stream file paths lazily and eliminate large upfront array allocations when processing massive comic libraries.
- **Asynchronous GUI & MCP Adoption**: Updated `ArchiveCoverService` and `ComicScannerService` to consume non-blocking async methods.

### Fixed
- **Bulk Auto-Tag Queue Button Label**: Fixed misleading `"Saving..."` label on the bottom action button while the queue is actively searching and matching covers on ComicVine. It now clearly displays `"Searching & Matching..."` during the search phase and `"Saving to Archives..."` during the write phase.
- **Top Control Bar Overflow in Bulk Queue**: Resolved control clipping where the Cancel button overflowed the top card by adopting a responsive 3-column layout and compact spacing.
- **ViewModel Path-Change False-Dirty Trigger**: Fixed a bug where updating a comic's path (`.cbr` $\rightarrow$ `.cbz` or file renaming) triggered property change notifications that erroneously marked `IsDirty = true`.
- **Bulk Scrape CBR $\rightarrow$ CBZ Path Synchronization**: Ensured `BulkScrapeQueueService` immediately updates item paths to `.cbz` after conversion so subsequent file renaming and in-place main grid reloads reference the correct file.

---

## [0.12.2] - 2026-08-22

### Added
- **Informative Bulk Scrape Cover Match Badges & Tooltips**:
  - Added dedicated status badges in the **Cover Match** column for non-visual states (`Text Only`, `No Local Cover`, `No Remote Cover`) alongside perceptual dHash percentages (e.g. `94%`).
  - Added comprehensive explanatory tooltips for both the **Confidence** column (structured breakdown of series title, issue #, year validity, and visual score) and **Cover Match** column.

### Changed
- **Volume Lifespan Year Scoring**:
  - Scraper confidence calculation now recognizes that issue publication years within a series volume's lifespan (e.g. *The Avengers #63 (1969)* in *The Avengers (1963)* volume) are valid, preventing false-negative 30% mismatch penalties and restoring confidence to 95%+.
- **On-Demand Targeted Cover Hashing**:
  - Bulk queue processing now dynamically downloads thumbnails and computes dHash fingerprints on-demand for candidate issues being matched, ensuring comics beyond issue #50 receive complete visual cover comparison scores.

### Fixed
- **API Key Prompt Settings Navigation**:
  - Opening Settings from the "API Key Required" prompt dialog now immediately switches to the **🔍 Scraping** tab and automatically focuses the ComicVine API key input field.

---

## [0.12.1] - 2026-08-22

### Added
- **Batch-Level Transaction Rollbacks**:
  - Assigns a unique `BatchJobId` (e.g. `batch_20260822_123456_a4f910`) to multi-file operations across Core, GUI, and MCP.
  - New MCP tools `ListBatchJobs` and `RestoreBatchJob` enabling atomic, single-command rollback of entire multi-file batches.
- **Rich Metadata Provenance & Forensic Audit Trail**:
  - Captures pre-write SHA-256 source hash, 64-bit cover visual `dHash`, remote matched thumbnail URL, match confidence, visual similarity score, change reason, and property-level diffs in the backup manifest.
  - New MCP tool `GetBackupProvenance` to inspect complete forensic provenance for any snapshot.
  - Enhanced embedded `ComicInfo.xml` `<Notes>` attribution tag with cover match similarity percentages (e.g. `[Cover Match 97%]`).
- **MCP Strict Read-Only Mode (`INKTAG_MCP_READ_ONLY=true` / `--read-only`)**:
  - Adds environment variable and command-line flag enforcement to prevent all file modifications, renames, and archive writes during audit and indexing agent sessions.
  - Returns explicit `UnauthorizedAccessException` error messages when write operations are attempted.
- **Automated Pre-Write Metadata Backups & Disaster Recovery**:
  - Automatic timestamped snapshotting of `ComicInfo.xml` prior to any archive modification or metadata write across Core (`EditMetadata`, `UpdateMetadataXml`), GUI, CLI, and MCP.
  - Backups are stored cleanly in the user's isolated application data directory (`~/.local/share/InkTag/backups/`) with automatic retention cleanup.
  - New MCP tools `ListMetadataBackups` and `RestoreComicBackup` to query history and instantly rollback comic archives to earlier metadata states.

### Changed
- **Safe-by-Default Dry-Runs for MCP Mutating Tools**:
  - Mutating tools (`UpdateComicMetadata`, `RenameComicFiles`, `ScrapeComicMetadata`, `BulkScrapeDirectory`) now default to `dryRun = true`, requiring AI agents to explicitly pass `dryRun = false` to commit disk changes.

---

## [0.12.0] - 2026-08-22

### Added
- **Light Mode, Dark Mode & System Default Theme Support**:
  - Added configurable `ThemeMode` (`System`, `Dark`, `Light`) in `AppSettings` with instant, dynamic runtime theme switching without application restarts.
  - Added **Appearance & Theme** card in the General Settings tab with real-time live preview on dropdown selection and rollback on Cancel.
  - Added quick **View > Theme** submenu (`System Default`, `Dark Mode`, `Light Mode`) to both the desktop menu bar and macOS native menus.
  - Created complete semantic `ThemeDictionaries` for Avalonia (`AppBackgroundBrush`, `AppCardBrush`, `AppSurfaceBrush`, `AppBorderBrush`, `AppTextPrimaryBrush`, `AppTextSecondaryBrush`, `AppTextMutedBrush`, `AppInputBackgroundBrush`, `AppAccentBrush`, etc.).
  - Added theme-aware dirty row highlighting with soft pastel mint (`#E6F4EA`) in Light Mode and deep emerald (`#1A3828`) in Dark Mode.
  - Refactored all modal dialogs (`SettingsWindow`, `AboutWindow`, `ScraperMatchWindow`, `BulkScrapeQueueWindow`, `RenamePreviewWindow`, `SeriesSearchWizardWindow`, `PromptWindow`, `ApiKeyRequiredWindow`, `ErrorSummaryWindow`, `ThirdPartyLicensesWindow`) to adapt seamlessly across themes.

### Changed
- **Targeted UI Polish & Visual Harmony**:
  - **Unified Toolbar Actions**: Converted utility tools (`Save All`, `Auto-Tag`, `Bulk Auto-Tag`, `Sync to Komga`, `Refresh`, `Inspector`) to clean neutral outline buttons with subtle borders, reserving solid accent highlights strictly for the primary action (`Open Folder...`).
  - **Segmented Filter Pill Control**: Encapsulated `All`, `Untagged`, and `Modified` filters into a unified segmented pill group with clean active highlight fill and neutral typography.
  - **Soft Neutral Framed Status Bar**: Transformed the bottom status bar from solid saturated blue into a framed soft neutral (`#F8FAFC` in Light Mode with `#64748B` slate text; `#18181B` in Dark Mode with `#94A3B8` text) with slim accent progress tracking.
- **Scraper & Queue DataGrid Fixes**:
  - Re-added dedicated **Status** badge column in Bulk Auto-Tag Queue DataGrid displaying real-time colored status badges (`Matched`, `Review Needed`, `Unmatched`, `Error`).
  - Fixed property bindings for candidate thumbnails, series titles, confidence percentages, and visual match indicators in single and bulk match dialogs.
  - Normalized consecutive whitespace in `ComicVineProvider.CleanString` to ensure high-confidence matching for series titles with non-standard spacing.

---

## [0.11.1] - 2026-08-22

### Added
- **Standardized Tagging Notes Attribution**: Automatically generate standard attribution notes in the `<Notes>` metadata field when scraping from ComicVine (`Tagged with InkTag <version> using info from Comic Vine on YYYY-MM-DD HH:MM:SS. [Issue ID <id>] [Volume ID <volId>]`). Smartly preserves user custom comments and previous notes, with a toggle in **Settings > Scraping**.
- **Interactive DataGrid Column Resizing**: Enabled user column width resizing on the main spreadsheet (`CanUserResizeColumns="True"`) and widened compact date columns (`Month`, `Day`, `Year`) to prevent header clipping.
- **Modern Tabbed Settings Interface**: Re-architected the Settings dialog into 4 focused categories (`⚙️ General`, `🔍 Scraping`, `🌐 Komga Server`, and `🛠️ Diagnostics & Advanced`) using rounded card containers.
- **Session Tab Persistence**: Settings window remembers the active tab across opens during the application session.
- **Diagnostics & Log Utilities**: Added direct "Open Logs Directory" action and "Reset to Defaults" button in the Advanced settings tab.
- **Komga Media Server REST API Integration**: Direct REST API integration with self-hosted Komga servers (`KomgaClient`, `KomgaSyncService`).
- **Targeted Sub-Second Cache Invalidation**: Automatic and manual targeted book/series analysis (`POST /api/v1/books/{id}/analyze`, `POST /api/v1/series/{id}/analyze`) updating web and mobile readers instantly without full-library rescans.
- **StoryArc & SeriesGroup to Komga Collection Sync**: Automatic creation and synchronization of Komga Collections from `<StoryArc>` and `<SeriesGroup>` tags.
- **Smart Path Translation**: Relative hierarchy resolution across Komga library roots with support for optional local-to-server path prefix mapping (`KomgaPathMappings`).
- **Komga Desktop GUI Controls**: Added dedicated Komga server configuration in Settings with live connectivity testing, "Sync to Komga" toolbar button, menu items, and DataGrid context menu integration.
- **Komga MCP Server Tools**: Exposed `CheckKomgaServer`, `SyncKomgaBookOrSeries`, and `AuditKomgaLibrary` for AI agent remote server management.

### Fixed
- **MacOS Application Settings Menu**: Added `Settings...` (`Cmd+,`) to the macOS Application menu (`InkTag Desktop`) and `Tools` NativeMenu.
- **Komga URL & Auth Redirect Resilience**: Automatically normalize server base URLs (stripping `/login`, `/dashboard`), pass `X-Requested-With` headers to prevent HTML redirect loops, and handle API authentication errors gracefully.
- **MacOS Logs Directory Revealer**: Fixed Finder log folder opening on macOS by invoking `/usr/bin/open` with direct file reveal (`-R`) arguments.
- **Series Wizard Issue Pluralization**: Display "1 Issue" (or "1 total issue") instead of "1 Issues" when a series contains only a single issue.

---

## [0.11.0] - 2026-08-21

### Added
- **Legacy ComicBookInfo (CBI) Ingestion**: Support for reading legacy ComicBookInfo JSON embedded in ZIP archive comments and internal `ComicBookInfo.json` files.
- **Automated Upgrade to ComicInfo.xml v2.1**: Ingests CBI metadata and upgrades archives to modern, schema-compliant `ComicInfo.xml v2.1` upon saving.
- **Dual-Format Merging**: Prioritizes `ComicInfo.xml` when present, while backfilling missing/empty fields from legacy CBI comments.
- **CBI Setting Toggle**: Added `ClearLegacyZipCommentsOnUpgrade` in Settings (default `true`) to strip obsolete ZIP comments on upgrade.
- **Hierarchical Path Inference**: Multi-level ancestor folder walking in `ComicFilenameParser` to resolve Series, Year, and Volume from folder structures like `Batman (2016)/Vol 1/001.cbz` with category directory filtering.
- **MCP Path Sandboxing**: Strict security root sandboxing (`AllowedRootPaths` / `INKTAG_ALLOWED_ROOT_PATHS`) confining AI agent tool access to safe workspace directories.
- **Rate-Limit Backoff**: Automatic exponential backoff retry (HTTP 420 / 429) in `RateLimitedHttpClient` with `Retry-After` header support.
- **Scraper Cache Write Debouncing**: 2-second debounced dirty buffer flush in `ScraperCacheService` to prevent O(n²) disk serialization.

### Changed
- **Recursive Scan Default**: "Scan Subfolders Recursively" is now enabled by default on application launch.
- **Filter-Aware Bulk Actions**: Bulk Auto-Tag, Batch Rename, and Inspector bulk edits operate strictly on filtered items (`DisplayedComics`) when filtering by Untagged or search queries.

---

## [0.10.3] - 2026-08-21

### Fixed
- **Windows Path Parsing in MCP**: Fixed path separator normalization and splitting for Windows runners during MCP root validation.
- **Test Isolation**: Isolated unit test temporary directories to prevent filesystem collisions on CI runners.

---

## [0.10.2] - 2026-08-21

### Fixed
- **Scraper Match Window**: Fixed Quick Apply All workflow to properly retrieve and apply selected online metadata.
- **MacOS AppleDouble Filtering**: Ignored macOS `._*` resource fork files and hidden files during directory scanning.
- **Status Bar Layout**: Moved active folder path display from top toolbar to bottom status bar for cleaner UI spacing.

---

## [0.10.1] - 2026-08-21

### Changed
- **Memory & Performance Optimizations**: Applied independent code review improvements across memory allocations, diagnostic logging, and test coverage.
- **Deep Copy Isolation**: Verified deep copy cloning of `PageCollection` and `Page[]` arrays in `ComicInfo.Clone()`.

---

## [0.10.0] - 2026-08-21

### Added
- **Bulk Auto-Tag Queue Pipeline**: Streaming parallel identification queue with cover visual hashing (`dHash`), series volume clustering, and duplicate protection.
- **Template-Based File Renamer**: `ComicFileRenamer` engine supporting tokenized renaming templates (`{Series} #{Number:3} ({Year})`) with collision prevention across Core, GUI, CLI, and MCP.

---

## [0.9.1] - 2026-08-20

### Fixed
- **Malformed XML Auto-Recovery**: Resilient handling of malformed, unordered, or corrupted `ComicInfo.xml` during metadata edit operations, guaranteeing XML schema compliance upon repack.

---

## [0.9.0] - 2026-08-19

### Added
- **Network Share Resilience**: Real-time detection of slow virtual remote shares (GVFS, FTP, SSHFS, SMB).
- **Sequential Streaming Fallback**: Non-seeking forward-only stream fallback for network mounts.
- **Live Scan Diagnostics & Instant Cancellation**: Real-time file processing feedback with sub-10ms cancellation response.

---

## [0.8.0] - 2026-08-16

### Added
- **Perceptual Cover Hashing**: 64-bit `dHash` image fingerprinting with Hamming distance visual similarity scoring.
- **Visual Match Indicators**: Visual match confidence badges (`👁 XX% Match`) in Scraper search results.
- **Series Search Wizard**: Two-step volume and issue search wizard with natural numerical ordering (`1, 2... 10, 11`).

---

## [0.7.0] - 2026-08-15

### Added
- **ComicVine Scraper Integration**: Primary online scraper provider with response caching, rate limiting, and field-by-field diff comparison.
- **Scraper Merge Policies**: Selective merge modes (`FillMissingOnly`, `OverwriteAll`).

---

## [0.6.0] - 2026-08-08

### Changed
- **Official MCP C# SDK**: Migrated MCP server implementation to the official `ModelContextProtocol` C# SDK (`v2.1.0`).
- **Security Dependency Update**: Updated `Tmds.DBus.Protocol` dependency.

---

## [0.5.4] - 2026-08-04

### Added
- **Full 35-Column DataGrid**: Comprehensive ComicInfo v2.1 field representation with column reordering, resizing, and sorting.
- **GitHub CLI Workflow Integration**: Established agent rules for issue tracking via `gh` CLI.

---

## [0.5.3] - 2026-08-04

### Added
- **Resizable Inspector Panel**: Interactive splitter for the right-hand Inspector pane.
### Fixed
- **Linux AppImage Auto-Update**: Resolved Velopack updater path resolution on Linux AppImage environments.

---

## [0.5.2] - 2026-08-03

### Fixed
- **Inspector Panel Layout**: Correctly collapse inspector panel on toggle and expand DataGrid table to fill available workspace width.

---

## [0.5.1] - 2026-08-03

### Fixed
- **Linux AppImage Startup**: Resolved `TypeLoadException` in DBus communication during AppImage initialization.

---

## [0.5.0] - 2026-08-03

### Added
- **Context-Sensitive Inspector Panel**: Dynamic right-hand inspector panel for single and multi-file metadata auditing.

---

## [0.4.4] - 2026-08-02

### Fixed
- **MacOS Velopack Version Tracking**: Write `sq.version` to `MacOS` directory and check `manager.IsInstalled` for in-place updates.

---

## [0.4.3] - 2026-08-02

### Changed
- Minor packaging and updater stability improvements.

---

## [0.4.2] - 2026-08-02

### Changed
- Minor UI performance enhancements.

---

## [0.4.1] - 2026-08-02

### Fixed
- **MacOS Silent Auto-Update**: Fixed background update application within macOS `.app` bundles.

---

## [0.4.0] - 2026-08-01

### Added
- **Cross-Platform MenuBar**: Avalonia MenuBar and macOS NativeMenu integration with standard hotkeys.

---

## [0.3.1] - 2026-08-01

### Added
- **GitHub API Fallback**: Direct GitHub API release checking for portable builds and Linux AppImages.

---

## [0.3.0] - 2026-08-01

### Changed
- **Major Code Review Refactor**: Addressed 15 architectural, performance, and type-safety findings across Core and GUI.

---

## [0.2.3] - 2026-08-01

### Added
- GitHub Releases fallback update checking on macOS and standalone builds.

---

## [0.2.2] - 2026-08-01

### Fixed
- Velopack metadata bundling for macOS `.dmg` and Linux AppImage.

---

## [0.2.1] - 2026-08-01

### Added
- **Application Logging (`AppLogger`)**: Rolling file logging system and in-app diagnostics log viewer.

---

## [0.2.0] - 2026-08-01

### Added
- **Native macOS Menu Integration**: Custom macOS application menu with 'About InkTag Desktop' dialog.

---

## [0.1.8] - 2026-08-01

### Fixed
- Passed `.icns` icon file to macOS Velopack pack step.

---

## [0.1.7] - 2026-07-31

### Fixed
- Updated `GithubSource` parameters in `UpdateService`.

---

## [0.1.5] - 2026-07-31

### Fixed
- Refactored archive parsing from streaming reader to random-access `ArchiveFactory.OpenArchive` for RAR/CBR reliability.

---

## [0.1.4] - 2026-07-31

### Fixed
- Updated desktop application display name to InkTag Desktop across all platforms.

---

## [0.1.3] - 2026-07-31

### Fixed
- Cleaned git submodules to resolve CI checkout issues.

---

## [0.1.2] - 2026-07-31

### Added
- Standalone self-contained `InkTag.Mcp` binary packages for Windows, macOS, and Linux.

---

## [0.1.1] - 2026-07-31

### Fixed
- Resolved git shallow clone errors on CI runners.

---

## [0.1.0] - 2026-07-31

### Added
- Initial release of InkTag (.NET Core library, Avalonia GUI, CLI, and MCP Server).
- Core ComicInfo v2.1 XML serialization and in-memory archive processing.

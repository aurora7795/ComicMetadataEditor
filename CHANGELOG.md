# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Modern Tabbed Settings Interface**: Re-architected the Settings dialog into 4 focused categories (`⚙️ General`, `🔍 Scraping`, `🌐 Komga Server`, and `🛠️ Diagnostics & Advanced`) using rounded card containers.
- **Session Tab Persistence**: Settings window remembers the active tab across opens during the application session.
- **Diagnostics & Log Utilities**: Added direct "Open Logs Directory" action and "Reset to Defaults" button in the Advanced settings tab.
- **Komga Media Server REST API Integration**: Direct REST API integration with self-hosted Komga servers (`KomgaClient`, `KomgaSyncService`).
- **Targeted Sub-Second Cache Invalidation**: Automatic and manual targeted book/series analysis (`POST /api/v1/books/{id}/analyze`, `POST /api/v1/series/{id}/analyze`) updating web and mobile readers instantly without full-library rescans.
- **StoryArc to Komga Collection Sync**: Automatic creation and synchronization of Komga Collections from `<StoryArc>` tags.
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

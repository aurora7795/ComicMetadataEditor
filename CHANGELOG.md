# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Fixed
- **MacOS Development Dock Icon**: Dynamically apply high-res application icon to the macOS Dock via Cocoa `NSApplication` runtime when launched via `dotnet run` (unbundled CLI processes).

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

## [0.6.0] - 2026-08-14

### Changed
- **Official MCP C# SDK**: Migrated MCP server implementation to the official `ModelContextProtocol` C# SDK (`v2.1.0`).

---

## [0.5.0] - 2026-08-11

### Added
- **Context-Sensitive Inspector Panel**: Dynamic right-hand inspector panel for single and multi-file metadata auditing.
- **35-Column DataGrid**: Full ComicInfo v2.1 field representation with column reordering and sorting.

---

## [0.4.0] - 2026-08-10

### Added
- **Cross-Platform MenuBar**: Avalonia MenuBar and macOS NativeMenu integration with standard hotkeys.

---

## [0.3.0] - 2026-08-09

### Changed
- **Code Review Refactor**: Major architectural refactor addressing performance, type safety, and async I/O.

---

## [0.2.0] - 2026-08-08

### Added
- **Application Logging (`AppLogger`)**: Rolling file logging system and in-app diagnostics log viewer.
- **Velopack Auto-Updater**: Cross-platform automatic updating support for Windows, macOS, and Linux.

---

## [0.1.0] - 2026-08-07

### Added
- Initial release of InkTag (.NET Core library, Avalonia GUI, CLI, and MCP Server).
- Core ComicInfo v2.1 XML serialization and in-memory archive processing.

# InkTag Code Review Report

**Project:** InkTag  
**Repository:** `aurora7795/InkTag`  
**Review Date:** August 23, 2026  
**Branch:** `docs/code-review-2026-08-23`  
**Overall Status:** **HEALTHY / HIGH QUALITY** (Solid foundation, robust archive safety, thorough test suite across Core/CLI/MCP/GUI; clear opportunities for refactoring and async modernization)

---

## 1. Executive Summary

InkTag is a mature, well-engineered .NET 10 cross-platform comic library metadata and organization tool (supporting `.cbz` and `.cbr` archives). The solution is logically decomposed into four distinct projects:
- **`InkTag.Core`**: Archive manipulation, ComicInfo XML serialization/sanitization, filename & ComicBookInfo parsing, Komga API integration, and scraper engines (ComicVine, Metron).
- **`InkTag.Cli`**: Terminal client for bulk operations, inspections, and batch processing.
- **`InkTag.Gui`**: Avalonia-based cross-platform UI with MVVM patterns, theme support, scraping queues, and perceptual cover matching.
- **`InkTag.Mcp`**: Model Context Protocol (MCP) server exposing tools for LLM agent integration with strict read-only modes, dry-run guarantees, and provenance tracking.
- **`InkTag.Tests`**: Comprehensive unit and integration test suite covering parsing, serialization, backup/restore rollbacks, MCP security/sandboxing, and scraper workflows.

---

## 2. Architectural Assessment & Project Layout

| Component | Responsibility | Assessment |
| :--- | :--- | :--- |
| **`InkTag.Core`** | Metadata read/write, schema validation, network-optimized stream reads, backup provenance. | **Strong.** Clear domain isolation from UI. High reliability around Linux FUSE/GVFS network mounts. |
| **`InkTag.Cli`** | Console commands and scripting interface. | **Clean.** Direct consumption of Core abstractions. |
| **`InkTag.Gui`** | Avalonia UI layer with reactive ViewModels. | **Good.** Effective use of CommunityToolkit.Mvvm source generators. |
| **`InkTag.Mcp`** | MCP Tool definitions, security boundaries, and JSON-RPC endpoints. | **Excellent.** Safe-by-default dry-run guarantees and strict read-only enforcement. |
| **`InkTag.Tests`** | Test harness across all subsystems. | **Thorough.** 17+ comprehensive test suites with real archive mocking and rollback tests. |

---

## 3. Detailed Code Quality Review

### 3.1 `MetadataEditor.cs` (`InkTag.Core`)
* **Strengths:**
  - Multi-tiered reading strategy: Fast-path zip seek $\rightarrow$ forward-only `NonSeekableStream` (for streaming/FUSE) $\rightarrow$ SharpCompress fallback.
  - Safe extraction with zip-slip / canonical directory boundary validation.
  - Pre-write metadata snapshots and automated backup tracking with forensic provenance.
  - Robust XML sanitization handling malformed numeric elements, boolean conversions (`Manga`, `BlackAndWhite`), and XML 1.0 control characters.
* **Areas for Improvement:**
  - **Class Size:** At ~1,400 lines, `MetadataEditor` combines archive extraction, stream management, XML tree sanitization, regex fallback parsing, and atomic file swapping. Splitting into `ArchiveReaderWriter`, `ComicInfoXmlSanitizer`, and `ArchiveSwapService` would improve maintainability.
  - **Async Overloads:** Core operations are predominantly synchronous (`EditMetadata`, `BulkEditMetadata`). Adding Task-based asynchronous APIs (`EditMetadataAsync`, `BulkEditMetadataAsync`) will improve scalability under high UI concurrency or batch network operations.
  - **Serializer Caching:** Cache `XmlSerializer` instances (`static readonly XmlSerializer`) rather than instantiating them on every serialization/deserialization pass to eliminate reflection and dynamic code generation overhead.

### 3.2 `ComicInfo.cs` (`InkTag.Core`)
* **Strengths:**
  - Full adherence to the ComicRack / Anansi schema specifications with conditional serialization (`ShouldSerialize*`).
  - Deep-cloning capability (`Clone()`) supporting full page collection copies.
  - Computed property helpers (`HasEssentialMetadata`, `HasAnyMetadata`).
* **Areas for Improvement:**
  - Consider adding standard XML doc comments to all schema properties for improved IntelliSense discovery.
  - Encapsulate legacy metadata state where appropriate.

### 3.3 Parsing Engines (`ComicFilenameParser.cs` & `ComicBookInfoParser.cs`)
* **Strengths:**
  - High resilience against exotic release naming conventions (volume tokens, edition tokens, year/month markers, publisher tags).
  - Legacy ComicBookInfo JSON parsing support with merge fallback to ComicInfo.xml.

---

## 4. Performance & Resource Management

1. **Lazy File Enumeration in Bulk Operations:**
   - In `BulkEditMetadata`, use `Directory.EnumerateFiles` instead of `Directory.GetFiles` to stream matches lazily and conserve memory during large library scans.
2. **Buffer Allocation & Stream Re-use:**
   - `OpenReadOptimized` defaults to a 64KB buffer with `FileShare.ReadWrite`. Exposing custom buffer tuning for high-throughput local SSDs vs high-latency FUSE/SMB shares is recommended.
3. **Temp Directory Lifecycle:**
   - Temporary archive repack directories are properly guarded, but ensuring guaranteed cleanup in nested `finally` blocks across unexpected OS aborts prevents accumulation in `/tmp`.

---

## 5. Error Handling, Safety & Security

* **Path Traversal / Zip-Slip Protection:** Extracted files are explicitly checked against canonical extraction target paths (`canonicalFile.StartsWith(canonicalTempDir)`).
* **XXE / DTD Injection Prevention:** `XmlReaderSettings` explicitly enforces `DtdProcessing = DtdProcessing.Ignore`, preventing external entity expansion.
* **Atomic-Like Swapping:** Writes to temporary archives, verifies archive integrity via `ArchiveFactory.OpenArchive`, and maintains `.bak` rollback files until replacement succeeds.
* **Domain Exception Types:** Introducing specific exception classes (e.g. `ComicArchiveCorruptException`, `MetadataSchemaValidationException`) will allow calling layers (GUI/CLI/MCP) to present contextual diagnostic error messages.

---

## 6. Prioritized Recommendations & Resolution Status

### 🟢 Priority 1: High Value / Low Effort
1. **Cache `XmlSerializer` Instances:** ✅ **Completed** — Defined static cached serializers for `ComicInfo` in `ComicInfoXmlSanitizer.cs`.
2. **Switch to `EnumerateFiles`:** ✅ **Completed** — Replaced `Directory.GetFiles` with `Directory.EnumerateFiles` in `MetadataEditor`, `AgentOperations`, and `ComicScannerService`.
3. **XML Doc Comments:** ✅ **Completed** — Added standard XML docstrings across all public schema properties in `ComicInfo.cs`.

### 🟡 Priority 2: Architecture & Scalability
1. **Modularize `MetadataEditor.cs`:** ✅ **Completed** — Extracted `ComicInfoXmlSanitizer.cs`, `ArchiveSwapService.cs`, and `ComicArchiveHandler.cs`, keeping `MetadataEditor.cs` as a unified façade.
2. **Async API Overloads:** ✅ **Completed** — Exposed `ReadMetadataAsync`, `EditMetadataAsync`, `BulkEditMetadataAsync`, `ExtractCoverImageBytesAsync`, and `GetCoverHashAsync` with `CancellationToken` and progress reporting.
3. **Custom Domain Exceptions:** ✅ **Completed** — Standardized error types (`InkTagException`, `ComicArchiveException`, `ComicArchiveCorruptException`, `MetadataXmlSanitizationException`, `UnsafeArchiveEntryException`) under `InkTag.Core.Exceptions`.

---

## 7. Reference Links

- Core Metadata Editor: [`MetadataEditor.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/MetadataEditor.cs)
- Modular XML Sanitizer: [`ComicInfoXmlSanitizer.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/ComicInfoXmlSanitizer.cs)
- Modular Archive Swap Service: [`ArchiveSwapService.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/ArchiveSwapService.cs)
- Modular Archive Handler: [`ComicArchiveHandler.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/ComicArchiveHandler.cs)
- Domain Exceptions: [`InkTagException.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/Exceptions/InkTagException.cs)
- ComicInfo Schema Model: [`ComicInfo.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Core/ComicInfo.cs)
- CLI Entry Point: [`Program.cs`](file:///home/aurora7795/AntiGravProjects/InkTag/src/InkTag.Cli/Program.cs)
- Test Suite: [`InkTag.Tests`](file:///home/aurora7795/AntiGravProjects/InkTag/tests/InkTag.Tests)


# Code Review Report

**Project:** ComicMetadataEditor  
**Repository:** `aurora7795/ComicMetadataEditor`  
**Review Date:** May 31, 2026  
**Status:** **NOT READY FOR PRODUCTION** (Risk Level: **High** due to potential data loss)

---

## 1. Executive Summary

`ComicMetadataEditor` is a C# utility designed to bulk-edit `ComicInfo.xml` metadata embedded inside comic book archive files. While the codebase is structured cleanly with a clear separation between the library (`ComicMetadataEditor`) and the console runner (`ComicEditorConsole`), it contains **severe architectural and safety flaws** that must be resolved before it is used on real, non-backed-up comic collections.

### Major Risks Identified:
1. **Catastrophic Data Loss Risk:** The tool deletes the original `.cbr` file *before* verifying that the new `.cbz` file has been safely written, moved, and validated. If an exception occurs during the repack, write, or move phase, the user’s comic is deleted permanently.
2. **Known Dependency Vulnerability:** `SharpCompress` version `0.37.2` has a known vulnerability ([GHSA-6c8g-7p36-r338](https://github.com/advisories/GHSA-6c8g-7p36-r338)) that could expose the application to path traversal or archive-extraction-based exploits.
3. **Format Omission & Ambiguity:** The tool searches exclusively for `.cbr` (typically RAR-based) archives, converts them silently to ZIP-based `.cbz` archives, but completely ignores existing `.cbz` files.
4. **Fragile Serialization & Error Handling:** There is no try-catch resilience in bulk processing. A single malformed XML or corrupt archive will crash the entire run, leaving partial state and temporary folders behind.

---

## 2. Detailed Findings by Severity

### 🔴 High Severity

#### A. Destructive File Handling & Data Loss Risk
In `MetadataEditor.cs` (lines 80-96):
```csharp
// Replace original CBR with new CBZ
File.Delete(cbrFilePath);
File.Move(tempCbzPath, newCbzPath);
```
* **The Problem:** `File.Delete` occurs *before* `File.Move`. If `File.Move` fails (e.g., if a file named `newCbzPath` already exists and is locked/read-only, the disk runs out of space, permissions are restricted, or the process is interrupted), the original `cbrFilePath` is already deleted, resulting in total data loss.
* **Impact:** High probability of file loss under common file-system constraints.
* **Remediation:**
  1. Write the new ZIP to a temporary file.
  2. Perform validation checks on the newly written ZIP (e.g., ensure it opens and is non-empty).
  3. Instead of deleting first, move the original file to a backup path (e.g. `comic.cbr.bak`).
  4. Perform the move of the new `.cbz` to its final path.
  5. Delete the `.bak` file only when the entire operation is verified successful.

#### B. Security Vulnerability in `SharpCompress` (v0.37.2)
* **The Problem:** The solution uses version `0.37.2` of `SharpCompress`. During build, NuGet warns of a known moderate vulnerability [GHSA-6c8g-7p36-r338](https://github.com/advisories/GHSA-6c8g-7p36-r338) that can allow path traversal or archive manipulation attacks.
* **Impact:** If users process untrusted or maliciously crafted comic archives, it could lead to arbitrary file extraction outside the intended directory or denial of service.
* **Remediation:** Upgrade `SharpCompress` to a patched version (such as `0.38.0` or newer).

---

### 🟡 Medium Severity

#### A. CBR vs. CBZ Formatting Discrepancies
* **The Problem:** 
  - `MetadataEditor.BulkEditMetadata` looks specifically for `*.cbr` files.
  - It extracts them (regardless of whether they are Zip, Rar, or Tar format, thanks to SharpCompress's flexible reader).
  - It then bundles them into a ZIP archive and saves them with a `.cbz` extension.
  - Existing `.cbz` files are completely ignored. If a user runs this on a folder of `.cbz` files, the tool will do nothing.
* **Impact:** Confusing behavior. CBR implies RAR format, and CBZ implies ZIP. Silently converting RAR to ZIP might be desirable, but doing so while ignoring existing `.cbz` files restricts utility.
* **Remediation:** 
  1. Add support to process both `.cbr` and `.cbz` extensions.
  2. If the file is already a `.cbz`, preserve its extension and write it back as a standard ZIP-based `.cbz` without deleting/renaming the extension.
  3. If it is a `.cbr` file, provide an option to either keep it as `.cbr` (not possible to write RAR via free libraries easily, but can be repackaged as CBZ with clear console logging) or explicitly warn the user about conversion.

#### B. Brittle XML Serialization & Implicit Defaults
In `ComicInfo.cs`:
```csharp
[XmlElement("Count")]
public int Count { get; set; }
```
* **The Problem:** Several integer and boolean fields (e.g. `Count`, `Volume`, `Year`, `Month`, `Day`, `PageCount`, `DoublePage`) are declared as primitive `int` and `bool` instead of nullable types (`int?`, `bool?`).
* **Impact:** 
  1. If an incoming `ComicInfo.xml` does not contain a `<Count>` or `<Volume>` node, the deserialized `ComicInfo` object will set these fields to `0`. When saved back, they will write `<Count>0</Count>` and `<Volume>0</Volume>`, altering the metadata file destructively.
  2. If the user only wants to change a single field (like setting `Manga = "No"`), other uninitialized numeric or boolean fields will be saved as `0` or `false` in the XML, overwriting empty values.
* **Remediation:** Change all optional primitive properties to nullable types (e.g., `public int? Count { get; set; }` and `public bool? DoublePage { get; set; }`).

#### C. Total Absence of Exception Handling in Bulk Mode
In `MetadataEditor.cs` (lines 25-28):
```csharp
foreach (var cbrFile in cbrFiles)
{
    EditSingleFileMetadata(cbrFile, editAction);
}
```
* **The Problem:** There is no try-catch surrounding `EditSingleFileMetadata` inside the loop. If a single comic archive is corrupted, password-protected, or has a malformed XML file, the execution halts immediately.
* **Impact:** The bulk operation crashes midway, leaving some files processed, some skipped, and temporary folders in the temp directory.
* **Remediation:** Add a try-catch block inside the loop. Log the error for the failing file, keep track of failed paths, and continue processing the remaining files in the list.

---

### 🟢 Low Severity / Polish

#### A. Missing Directory Validation in Console
In `Program.cs`:
```csharp
string directoryPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
```
* **The Problem:** There is no validation to ensure that `directoryPath` actually exists or is accessible.
* **Impact:** The console app crashes with a raw `DirectoryNotFoundException` if the user types an invalid path.
* **Remediation:** Check `Directory.Exists(directoryPath)` before calling `BulkEditMetadata`. If it doesn't exist, display a clean, user-friendly error message and exit gracefully.

#### B. Console Feedback & Reporting
* **The Problem:** The program always prints `"Metadata updated for CBR files in " + directoryPath` at the end, regardless of whether any files were actually found or if some of them failed.
* **Impact:** Zero visibility into bulk operations. The user has no idea how many files were updated or if any errors occurred.
* **Remediation:** Modify `BulkEditMetadata` to return a operation report containing:
  - Total files discovered
  - Count of successfully processed files
  - Count of failed files with details
  - List of skipped files

#### C. Build Targets & SDKs
* **The Problem:** `ComicEditorConsole` targets `net10.0`. While the environment SDK supports it, targeting a bleeding-edge/preview standard (`net10.0`) instead of a stable LTS like `net8.0` restricts compatibility and makes compilation on older or production-ready developer machines difficult.
* **Remediation:** Standardize the console project target to `net8.0` unless preview features of `net10.0` are explicitly required.

#### D. Tracked IDE & Output Artifacts in Repository
* **The Problem:** The git repository tracks the `.idea/` (JetBrains Rider) settings directory, as well as build directories `bin/` and `obj/`.
* **Impact:** Bloats the repository size, causes merge conflicts on generated build artifacts, and pollutes git history.
* **Remediation:** 
  1. Add a `.gitignore` file to the root of the project.
  2. Remove the tracked `bin/`, `obj/`, and `.idea/` directories from git using `git rm -r --cached`.

---

## 3. Recommended Code Improvements

### Step 1: Add a robust `.gitignore`
Create a standard .NET `.gitignore` at the root of the project:
```gitignore
[Db]in/
[Oo]bj/
.idea/
*.user
*.suo
*.tmp
```

### Step 2: Refactor `ComicInfo.cs` to use Nullable Primitives
```csharp
// Example changes in ComicInfo.cs
[XmlElement("Count")]
public int? Count { get; set; }

[XmlElement("Volume")]
public int? Volume { get; set; }

[XmlElement("Year")]
public int? Year { get; set; }

[XmlElement("Month")]
public int? Month { get; set; }

[XmlElement("Day")]
public int? Day { get; set; }

[XmlElement("PageCount")]
public int? PageCount { get; set; }

[XmlAttribute("DoublePage")]
public bool? DoublePage { get; set; }
```

### Step 3: Implement Safe File Replacement, Dual CBZ/CBR Support, & Error Reporting
Here is a conceptual improvement for `MetadataEditor.cs`:

```csharp
public class EditReport
{
    public int TotalFound { get; set; }
    public List<string> Successes { get; } = new();
    public List<(string Path, Exception Exception)> Failures { get; } = new();
}

public EditReport BulkEditMetadata(string directoryPath, Action<ComicInfo> editAction)
{
    var report = new EditReport();
    
    if (!Directory.Exists(directoryPath))
    {
        throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
    }

    // Search for both cbr and cbz files
    var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
        .Where(f => f.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase) || 
                    f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
        .ToList();

    report.TotalFound = files.Count;

    foreach (var file in files)
    {
        try
        {
            EditSingleFileMetadata(file, editAction);
            report.Successes.Add(file);
        }
        catch (Exception ex)
        {
            report.Failures.Add((file, ex));
        }
    }

    return report;
}

private void EditSingleFileMetadata(string filePath, Action<ComicInfo> editAction)
{
    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

    try
    {
        // Extract archive contents
        using (Stream stream = File.OpenRead(filePath))
        using (var reader = ReaderFactory.Open(stream, new ReaderOptions()))
        {
            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(tempDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
            }
        }

        // Edit ComicInfo.xml
        string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
        ComicInfo comicInfo;

        if (File.Exists(xmlPath))
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
            using (FileStream fs = new FileStream(xmlPath, FileMode.Open, FileAccess.Read))
            {
                comicInfo = (ComicInfo)serializer.Deserialize(fs)!;
            }
        }
        else
        {
            comicInfo = new ComicInfo();
        }

        editAction(comicInfo);

        using (FileStream fs = new FileStream(xmlPath, FileMode.Create, FileAccess.Write))
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ComicInfo));
            serializer.Serialize(fs, comicInfo);
        }

        // Safe Write Strategy: Write to a separate temp CBZ archive
        string tempCbzPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cbz");
        
        using (Stream stream = File.OpenWrite(tempCbzPath))
        using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
        {
            foreach (var file in Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories))
            {
                string entryName = GetRelativePath(tempDir, file).Replace('\\', '/');
                writer.Write(entryName, file);
            }
        }

        // Calculate targets
        string originalExtension = Path.GetExtension(filePath);
        string targetPath = originalExtension.Equals(".cbr", StringComparison.OrdinalIgnoreCase) 
            ? Path.ChangeExtension(filePath, ".cbz") 
            : filePath;

        string backupPath = filePath + ".bak";

        // Step 1: Backup original file
        File.Move(filePath, backupPath);

        try
        {
            // Step 2: Copy new archive to destination
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath); // Overwrite if Target CBZ exists already
            }
            File.Move(tempCbzPath, targetPath);

            // Step 3: Remove backup on success
            File.Delete(backupPath);
        }
        catch
        {
            // Rollback if replacement failed
            if (File.Exists(backupPath))
            {
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(backupPath, filePath);
            }
            throw;
        }
    }
    finally
    {
        Directory.Delete(tempDir, true);
    }
}
```

---

## 4. Conclusion & Next Steps

This utility is a **great base framework** with clean separation of concerns, but it is currently highly risky for users due to data loss during write failure, vulnerability in `SharpCompress`, and missing error-resilience. 

Addressing the findings in the priority listed below will turn this project into a robust, high-quality production tool:
1. **Critical:** Implement Safe File Replacement logic (temp write -> backup -> swap -> cleanup).
2. **Critical:** Update the `SharpCompress` package to the latest version.
3. **High:** Support both `.cbz` and `.cbr` inputs, preserving CBZ extensions properly.
4. **Medium:** Convert `ComicInfo.cs` integer and boolean properties to nullable types.
5. **Low:** Add proper command line feedback, error handling, and file statistics.

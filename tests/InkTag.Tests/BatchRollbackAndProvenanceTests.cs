using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Core;
using InkTag.Core.Backup;
using InkTag.Mcp;
using Xunit;

namespace InkTag.Tests;

[Collection("BackupTests")]
public class BatchRollbackAndProvenanceTests : IDisposable
{
    private readonly string _testBackupDir;

    public BatchRollbackAndProvenanceTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), "InkTagBatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBackupDir);
        MetadataBackupService.SetGlobalCustomBackupDirectory(_testBackupDir);
        ComicTools.ReadOnlyOverride = null;
    }

    public void Dispose()
    {
        ComicTools.ReadOnlyOverride = null;
        MetadataBackupService.SetGlobalCustomBackupDirectory(null);
        if (Directory.Exists(_testBackupDir))
        {
            try { Directory.Delete(_testBackupDir, true); } catch { }
        }
    }

    private string CreateTestCbz(string title, string series)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        string imagePath = Path.Combine(tempDir, "001.jpg");
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });

        string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
        File.WriteAllText(xmlPath, $"<ComicInfo><Title>{title}</Title><Series>{series}</Series></ComicInfo>");

        string cbzPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cbz");
        ZipFile.CreateFromDirectory(tempDir, cbzPath);
        Directory.Delete(tempDir, true);

        return cbzPath;
    }

    [Fact]
    public void CreateBackup_RecordsCompleteProvenanceFields()
    {
        string cbz = CreateTestCbz("Original Issue 1", "Original Series");
        try
        {
            var editor = new MetadataEditor();
            editor.EditMetadata(
                cbz,
                comic =>
                {
                    comic.Title = "Updated Issue 1";
                    comic.Writer = "Alan Moore";
                },
                batchJobId: "batch_test_001",
                changeReason: "Auto-Scrape ComicVine #1234",
                coverDHash: "A1B2C3D4E5F60718",
                matchedThumbnailUrl: "https://comicvine.gamespot.com/a/uploads/scale_small/1234.jpg",
                matchConfidence: 0.98,
                visualSimilarity: 0.965);

            var backupService = new MetadataBackupService(_testBackupDir);
            var backups = backupService.ListBackups(cbz);
            Assert.NotEmpty(backups);

            var entry = backupService.GetBackupEntry(backups[0].Id);
            Assert.NotNull(entry);
            Assert.Equal("batch_test_001", entry.BatchJobId);
            Assert.Equal("Auto-Scrape ComicVine #1234", entry.ChangeReason);
            Assert.Equal("A1B2C3D4E5F60718", entry.CoverDHash);
            Assert.Equal("https://comicvine.gamespot.com/a/uploads/scale_small/1234.jpg", entry.MatchedThumbnailUrl);
            Assert.Equal(0.98, entry.MatchConfidence);
            Assert.Equal(0.965, entry.VisualSimilarity);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void RestoreBatchJob_RevertsMultipleFilesAtomically()
    {
        string cbz1 = CreateTestCbz("Book 1 Original", "Series A");
        string cbz2 = CreateTestCbz("Book 2 Original", "Series A");
        string cbz3 = CreateTestCbz("Book 3 Original", "Series A");

        try
        {
            var editor = new MetadataEditor();
            string batchId = "batch_unit_test_999";

            // Edit all 3 books in a single batch
            editor.EditMetadata(cbz1, c => c.Title = "Book 1 Corrupted", batchJobId: batchId);
            editor.EditMetadata(cbz2, c => c.Title = "Book 2 Corrupted", batchJobId: batchId);
            editor.EditMetadata(cbz3, c => c.Title = "Book 3 Corrupted", batchJobId: batchId);

            // Verify they were modified
            Assert.Equal("Book 1 Corrupted", editor.ReadMetadata(cbz1).Title);
            Assert.Equal("Book 2 Corrupted", editor.ReadMetadata(cbz2).Title);
            Assert.Equal("Book 3 Corrupted", editor.ReadMetadata(cbz3).Title);

            var backupService = new MetadataBackupService(_testBackupDir);

            // Check ListBatchJobs
            var batchSummaries = backupService.ListBatchJobs();
            Assert.Contains(batchSummaries, b => b.BatchJobId == batchId && b.TotalBackups == 3);

            // Rollback the entire batch
            var report = backupService.RestoreBatchJob(batchId);
            Assert.Equal(3, report.Total);
            Assert.Equal(3, report.Restored);
            Assert.Equal(0, report.Failed);

            // Verify all 3 restored to original
            Assert.Equal("Book 1 Original", editor.ReadMetadata(cbz1).Title);
            Assert.Equal("Book 2 Original", editor.ReadMetadata(cbz2).Title);
            Assert.Equal("Book 3 Original", editor.ReadMetadata(cbz3).Title);
        }
        finally
        {
            if (File.Exists(cbz1)) File.Delete(cbz1);
            if (File.Exists(cbz2)) File.Delete(cbz2);
            if (File.Exists(cbz3)) File.Delete(cbz3);
        }
    }

    [Fact]
    public void McpTools_BatchAndProvenanceTools_ExecuteSuccessfully()
    {
        string cbz = CreateTestCbz("Issue 1 Initial", "Spider-Man");
        try
        {
            var editor = new MetadataEditor();
            string batchId = "batch_mcp_test_555";
            editor.EditMetadata(
                cbz,
                c => c.Title = "Issue 1 Injected",
                batchJobId: batchId,
                changeReason: "Bulk Tagging",
                coverDHash: "1234567890ABCDEF",
                matchedThumbnailUrl: "https://example.com/cover.jpg",
                matchConfidence: 0.95,
                visualSimilarity: 0.92);

            // 1. Test ListBatchJobs
            string listBatchJson = ComicTools.ListBatchJobs();
            Assert.Contains("batchCount", listBatchJson);
            Assert.Contains(batchId, listBatchJson);

            // 2. Test GetBackupProvenance
            var backupService = new MetadataBackupService(_testBackupDir);
            var backups = backupService.ListBackups(cbz);
            string provenanceJson = ComicTools.GetBackupProvenance(backups[0].Id);
            Assert.Contains("1234567890ABCDEF", provenanceJson);
            Assert.Contains("https://example.com/cover.jpg", provenanceJson);
            Assert.Contains("Bulk Tagging", provenanceJson);

            // 3. Test RestoreBatchJob via MCP
            string restoreJson = ComicTools.RestoreBatchJob(batchId);
            Assert.Contains("\"success\": true", restoreJson);
            Assert.Contains("\"restoredCount\": 1", restoreJson);

            // Verify restored
            var restored = editor.ReadMetadata(cbz);
            Assert.Equal("Issue 1 Initial", restored.Title);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }
}

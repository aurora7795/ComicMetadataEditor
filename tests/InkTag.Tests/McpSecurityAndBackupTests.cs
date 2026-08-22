using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Core;
using InkTag.Core.Backup;
using InkTag.Mcp;
using Xunit;

namespace InkTag.Tests;

[Collection("BackupTests")]
public class McpSecurityAndBackupTests : IDisposable
{
    private readonly string _testBackupDir;

    public McpSecurityAndBackupTests()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), "InkTagBackupTests_" + Guid.NewGuid().ToString("N"));
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

    private string CreateTestCbz(string comicInfoXml = "<ComicInfo><Title>Initial Title</Title><Series>Initial Series</Series></ComicInfo>")
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        string imagePath = Path.Combine(tempDir, "001.jpg");
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 });

        string xmlPath = Path.Combine(tempDir, "ComicInfo.xml");
        File.WriteAllText(xmlPath, comicInfoXml);

        string cbzPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cbz");
        ZipFile.CreateFromDirectory(tempDir, cbzPath);
        Directory.Delete(tempDir, true);

        return cbzPath;
    }

    [Fact]
    public void ReadOnlyMode_ThrowsUnauthorizedAccessException_OnMutatingWrites()
    {
        string cbz = CreateTestCbz();
        try
        {
            ComicTools.ReadOnlyOverride = true;

            using var doc = JsonDocument.Parse("{\"Writer\": \"Hacker\"}");
            
            // Should throw when dryRun is false
            var ex1 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: false));
            Assert.Contains("strict READ-ONLY mode", ex1.Message);

            var ex2 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.RenameComicFiles(cbz, dryRun: false));
            Assert.Contains("strict READ-ONLY mode", ex2.Message);

            var ex3 = Assert.Throws<UnauthorizedAccessException>(() =>
                ComicTools.RestoreComicBackup(cbz));
            Assert.Contains("strict READ-ONLY mode", ex3.Message);

            // But dry-run should still be allowed
            string preview = ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: true);
            Assert.Contains("Writer", preview);
        }
        finally
        {
            ComicTools.ReadOnlyOverride = null;
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void UpdateComicMetadata_DefaultsToDryRun_WithoutModifyingFile()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Untouched Title</Title></ComicInfo>");
        try
        {
            using var doc = JsonDocument.Parse("{\"Title\": \"Changed Title\"}");
            
            // Invoke without specifying dryRun parameter (should default to dryRun: true)
            string result = ComicTools.UpdateComicMetadata(cbz, doc.RootElement);
            Assert.Contains("Changed Title", result);

            // Verify file was NOT modified
            var editor = new MetadataEditor();
            var metadata = editor.ReadMetadata(cbz);
            Assert.Equal("Untouched Title", metadata.Title);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void PreWriteBackup_CreatesSnapshotAndAllowsRestore()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Original Title</Title><Writer>Original Writer</Writer></ComicInfo>");
        try
        {
            // 1. Perform a write edit
            var editor = new MetadataEditor();
            editor.EditMetadata(cbz, info =>
            {
                info.Title = "Overwritten Title";
                info.Writer = "Overwritten Writer";
            });

            // Verify edit applied
            var edited = editor.ReadMetadata(cbz);
            Assert.Equal("Overwritten Title", edited.Title);

            // 2. Query backup service
            var backupService = new MetadataBackupService(_testBackupDir);
            var backups = backupService.ListBackups(cbz);
            Assert.NotEmpty(backups);

            // 3. Restore backup
            bool restored = backupService.RestoreBackup(cbz, backups[0].Id);
            Assert.True(restored);

            // 4. Verify original metadata restored
            var reverted = editor.ReadMetadata(cbz);
            Assert.Equal("Original Title", reverted.Title);
            Assert.Equal("Original Writer", reverted.Writer);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void RestoreComicBackup_McpTool_RestoresMetadataSuccessfully()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Initial Title</Title><Series>Initial Series</Series></ComicInfo>");
        try
        {
            // Edit via MCP tool with explicit dryRun: false
            using var doc = JsonDocument.Parse("{\"Title\": \"Malicious Injected Title\"}");
            ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: false);

            var editor = new MetadataEditor();
            var modified = editor.ReadMetadata(cbz);
            Assert.Equal("Malicious Injected Title", modified.Title);

            // List backups via MCP tool
            string listJson = ComicTools.ListMetadataBackups(cbz);
            Assert.Contains("backupCount", listJson);

            // Restore via MCP tool
            string restoreJson = ComicTools.RestoreComicBackup(cbz);
            Assert.Contains("\"success\": true", restoreJson);

            // Verify restored
            var restored = editor.ReadMetadata(cbz);
            Assert.Equal("Initial Title", restored.Title);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }
}

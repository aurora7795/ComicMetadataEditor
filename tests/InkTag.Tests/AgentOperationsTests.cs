using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using InkTag.Core;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using Xunit;

namespace InkTag.Tests;

public class AgentOperationsTests
{
    private string CreateSampleCbz(string directory, string filename, string? title = null, string? series = null)
    {
        string filePath = Path.Combine(directory, filename);
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            string xmlContent = $"<ComicInfo><Title>{title ?? "Test Title"}</Title><Series>{series ?? "Test Series"}</Series></ComicInfo>";
            File.WriteAllText(tempXml, xmlContent);

            using (var stream = File.OpenWrite(filePath))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
        }
        return filePath;
    }

    [Fact]
    public void ScanDirectory_TopLevel_FindsOnlyRootFiles()
    {
        var editor = new MetadataEditor();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string subDir = Path.Combine(tempDir, "IssueSubfolder");
        Directory.CreateDirectory(subDir);

        try
        {
            CreateSampleCbz(tempDir, "issue1.cbz", "Issue 1");
            CreateSampleCbz(subDir, "issue2.cbz", "Issue 2");

            var result = AgentOperations.ScanDirectory(editor, tempDir, new[] { "Writer" }, recursive: false);

            Assert.Equal(1, result.TotalFound);
            Assert.Single(result.Items);
            Assert.Contains(result.Items[0].MissingFields, f => f.Equals("Writer", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScanDirectory_Recursive_FindsSubfolderFiles()
    {
        var editor = new MetadataEditor();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string subDir = Path.Combine(tempDir, "IssueSubfolder");
        Directory.CreateDirectory(subDir);

        try
        {
            CreateSampleCbz(tempDir, "issue1.cbz", "Issue 1");
            CreateSampleCbz(subDir, "issue2.cbz", "Issue 2");

            var result = AgentOperations.ScanDirectory(editor, tempDir, new[] { "Writer" }, recursive: true);

            Assert.Equal(2, result.TotalFound);
            Assert.Equal(2, result.Items.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UpdatePath_SingleFile_DryRunVsLive()
    {
        var editor = new MetadataEditor();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            string file = CreateSampleCbz(tempDir, "issue1.cbz", "Old Title");
            string patch = JsonSerializer.Serialize(new { Title = "New Title", Writer = "Stan Lee" });

            // Dry Run
            var dryResult = AgentOperations.UpdatePath(editor, file, patch, dryRun: true);
            Assert.False(dryResult.IsDirectory);
            Assert.True(dryResult.DryRun);
            Assert.NotNull(dryResult.Diffs);
            Assert.Contains(dryResult.Diffs, d => d.PropertyName == "Title" && d.NewValue?.ToString() == "New Title");

            var metaBefore = editor.ReadMetadata(file);
            Assert.Equal("Old Title", metaBefore.Title);

            // Live Update
            var liveResult = AgentOperations.UpdatePath(editor, file, patch, dryRun: false);
            Assert.False(liveResult.DryRun);

            var metaAfter = editor.ReadMetadata(file);
            Assert.Equal("New Title", metaAfter.Title);
            Assert.Equal("Stan Lee", metaAfter.Writer);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UpdatePath_DirectoryRecursive_UpdatesSubfolderFiles()
    {
        var editor = new MetadataEditor();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string subDir = Path.Combine(tempDir, "Sub");
        Directory.CreateDirectory(subDir);

        try
        {
            string rootFile = CreateSampleCbz(tempDir, "root.cbz", "Root Title");
            string subFile = CreateSampleCbz(subDir, "sub.cbz", "Sub Title");
            string patch = JsonSerializer.Serialize(new { Publisher = "Marvel" });

            var liveResult = AgentOperations.UpdatePath(editor, tempDir, patch, dryRun: false, recursive: true);

            Assert.True(liveResult.IsDirectory);
            Assert.NotNull(liveResult.Report);
            Assert.Equal(2, liveResult.Report.Successes.Count);

            Assert.Equal("Marvel", editor.ReadMetadata(rootFile).Publisher);
            Assert.Equal("Marvel", editor.ReadMetadata(subFile).Publisher);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

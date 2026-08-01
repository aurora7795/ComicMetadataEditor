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

public class MetadataEditorTests
{
    [Fact]
    public void ApplyJsonPatch_UpdatesPropertiesCorrectly()
    {
        var comic = new ComicInfo
        {
            Title = "Original Title",
            Year = 2020
        };

        string jsonPatch = JsonSerializer.Serialize(new
        {
            Title = "Updated Title",
            Writer = "Test Writer",
            Year = 2026,
            Count = 10,
            Manga = "Yes"
        });

        MetadataEditor.ApplyJsonPatch(comic, jsonPatch);

        Assert.Equal("Updated Title", comic.Title);
        Assert.Equal("Test Writer", comic.Writer);
        Assert.Equal(2026, comic.Year);
        Assert.Equal(10, comic.Count);
        Assert.Equal(MangaDirection.Yes, comic.Manga);
    }

    [Fact]
    public void ExportJsonSchema_ReturnsValidJsonSchema()
    {
        string schemaJson = MetadataEditor.ExportJsonSchema();

        Assert.NotNull(schemaJson);
        Assert.Contains("\"title\": \"ComicInfo\"", schemaJson);
        Assert.Contains("\"Title\"", schemaJson);
        Assert.Contains("\"Writer\"", schemaJson);
    }

    [Fact]
    public void GetMetadataDiff_IdentifiesModifiedFields()
    {
        var editor = new MetadataEditor();
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");

        try
        {
            File.WriteAllText(tempXml, "<ComicInfo><Title>Old Title</Title></ComicInfo>");
            using (var stream = File.OpenWrite(tempFile))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }

            string jsonPatch = JsonSerializer.Serialize(new { Title = "New Title", Writer = "Stan Lee" });
            var diffs = editor.GetMetadataDiff(tempFile, jsonPatch);

            Assert.Contains(diffs, d => d.PropertyName == "Title" && d.OldValue?.ToString() == "Old Title" && d.NewValue?.ToString() == "New Title");
            Assert.Contains(diffs, d => d.PropertyName == "Writer" && d.OldValue == null && d.NewValue?.ToString() == "Stan Lee");
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EditMetadata_RoundTripCBZ_WritesAndReadsMetadataCorrectly()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");

        try
        {
            // Create initial minimal CBZ
            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                string tempDummy = Path.GetTempFileName();
                File.WriteAllText(tempDummy, "dummy content");
                writer.Write("01.png", tempDummy);
                File.Delete(tempDummy);
            }

            editor.EditMetadata(tempCbz, comic =>
            {
                comic.Title = "Amazing Spider-Man";
                comic.Series = "Spider-Man";
                comic.Number = "1";
                comic.Writer = "Stan Lee";
                comic.Manga = MangaDirection.Yes;
            });

            var read = editor.ReadMetadata(tempCbz);
            Assert.Equal("Amazing Spider-Man", read.Title);
            Assert.Equal("Spider-Man", read.Series);
            Assert.Equal("1", read.Number);
            Assert.Equal("Stan Lee", read.Writer);
            Assert.Equal(MangaDirection.Yes, read.Manga);
        }
        finally
        {
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void ExtractCoverImage_ExtractsCoverEntryDirectly()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempOutputCover = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_cover.jpg");
        string tempImage = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(tempImage, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Minimal JPEG header
            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("cover.jpg", tempImage);
                writer.Write("page2.jpg", tempImage);
            }

            string? extractedPath = editor.ExtractCoverImage(tempCbz, tempOutputCover);
            Assert.NotNull(extractedPath);
            Assert.True(File.Exists(tempOutputCover));
            Assert.True(new FileInfo(tempOutputCover).Length > 0);
        }
        finally
        {
            if (File.Exists(tempImage)) File.Delete(tempImage);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
            if (File.Exists(tempOutputCover)) File.Delete(tempOutputCover);
        }
    }

    [Fact]
    public void BulkEditMetadata_UpdatesMultipleArchivesInDirectory()
    {
        var editor = new MetadataEditor();
        string tempDir = Path.Combine(Path.GetTempPath(), "BulkEditTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string cbz1 = Path.Combine(tempDir, "issue1.cbz");
        string cbz2 = Path.Combine(tempDir, "issue2.cbz");

        try
        {
            string tempDummy = Path.GetTempFileName();
            File.WriteAllText(tempDummy, "dummy");

            foreach (var file in new[] { cbz1, cbz2 })
            {
                using var stream = File.OpenWrite(file);
                using var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate));
                writer.Write("01.png", tempDummy);
            }
            File.Delete(tempDummy);

            var report = editor.BulkEditMetadata(tempDir, comic =>
            {
                comic.Publisher = "Marvel Comics";
            });

            Assert.Equal(2, report.TotalFound);
            Assert.Equal(2, report.Successes.Count);
            Assert.Empty(report.Failures);

            Assert.Equal("Marvel Comics", editor.ReadMetadata(cbz1).Publisher);
            Assert.Equal("Marvel Comics", editor.ReadMetadata(cbz2).Publisher);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void ComicInfo_Clone_CreatesExactDeepCopy()
    {
        var original = new ComicInfo
        {
            Title = "Cloned Title",
            Writer = "Writer A",
            Manga = MangaDirection.YesAndRightToLeft,
            Pages = new PageCollection
            {
                Page = new[]
                {
                    new Page { Image = 0, Type = "FrontCover", Key = "cover.jpg" }
                }
            }
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.Writer, clone.Writer);
        Assert.Equal(original.Manga, clone.Manga);
        Assert.NotSame(original.Pages, clone.Pages);
        Assert.NotNull(clone.Pages?.Page);
        Assert.Single(clone.Pages!.Page);
        Assert.Equal("FrontCover", clone.Pages.Page[0].Type);
    }
}

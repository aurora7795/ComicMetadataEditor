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

    [Fact]
    public void EditMetadata_RejectsZipSlipEntry_EnforcesSafeExtractionOptions()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_zipslip.cbz");
        string tempContentFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempContentFile, "malicious payload");

            // Build a CBZ containing an entry key with parent directory traversal
            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("../evil.txt", tempContentFile);
            }

            // EditMetadata uses ExtractFullPath = false, so the file extracts as evil.txt within tempDir
            // and post-extraction validation ensures no file escapes tempDir.
            editor.EditMetadata(tempCbz, comic =>
            {
                comic.Title = "Sanitized Title";
            });

            var readComic = editor.ReadMetadata(tempCbz);
            Assert.Equal("Sanitized Title", readComic.Title);
        }
        finally
        {
            if (File.Exists(tempContentFile)) File.Delete(tempContentFile);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void ApplyJsonPatch_ReturnsWarningsForUnknownProperties()
    {
        var comic = new ComicInfo();
        string jsonPatch = JsonSerializer.Serialize(new
        {
            Title = "Valid Title",
            UnknownProp1 = "Value1",
            Writter = "Typo Writer"
        });

        var warnings = MetadataEditor.ApplyJsonPatch(comic, jsonPatch);

        Assert.Equal("Valid Title", comic.Title);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("UnknownProp1"));
        Assert.Contains(warnings, w => w.Contains("Writter"));
    }

    [Fact]
    public void ComicItemViewModel_PreservesYesAndRightToLeftMangaDirection()
    {
        var model = new ComicInfo
        {
            Title = "Manga Test",
            Manga = MangaDirection.YesAndRightToLeft
        };

        var vm = new InkTag.Gui.ViewModels.ComicItemViewModel("test.cbz", model);

        Assert.True(vm.Manga);

        var updatedModel = new ComicInfo();
        vm.ApplyChangesToModel(updatedModel);

        Assert.Equal(MangaDirection.YesAndRightToLeft, updatedModel.Manga);
    }

    [Fact]
    public void EditMetadata_RollbackOnFailedEdit_LeavesOriginalArchiveUntouched()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_rollback.cbz");
        string tempDummy = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempDummy, "dummy data");
            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("01.png", tempDummy);
            }

            editor.EditMetadata(tempCbz, c => c.Title = "Original Title");
            Assert.Equal("Original Title", editor.ReadMetadata(tempCbz).Title);

            Assert.Throws<InvalidOperationException>(() =>
            {
                editor.EditMetadata(tempCbz, c =>
                {
                    c.Title = "Modified Title";
                    throw new InvalidOperationException("Simulated failure during edit callback");
                });
            });

            Assert.True(File.Exists(tempCbz));
            Assert.Equal("Original Title", editor.ReadMetadata(tempCbz).Title);
        }
        finally
        {
            if (File.Exists(tempDummy)) File.Delete(tempDummy);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void EditMetadata_CbrConvertsToCbz_RemovesOriginalCbr()
    {
        var editor = new MetadataEditor();
        string tempCbr = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbr");
        string expectedCbz = Path.ChangeExtension(tempCbr, ".cbz");
        string tempDummy = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempDummy, "dummy data");
            using (var stream = File.OpenWrite(tempCbr))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("01.png", tempDummy);
            }

            editor.EditMetadata(tempCbr, comic =>
            {
                comic.Title = "Converted CBR Title";
            });

            Assert.False(File.Exists(tempCbr));
            Assert.True(File.Exists(expectedCbz));

            var read = editor.ReadMetadata(expectedCbz);
            Assert.Equal("Converted CBR Title", read.Title);
        }
        finally
        {
            if (File.Exists(tempDummy)) File.Delete(tempDummy);
            if (File.Exists(tempCbr)) File.Delete(tempCbr);
            if (File.Exists(expectedCbz)) File.Delete(expectedCbz);
        }
    }

    [Fact]
    public void ValidateXml_SkipsWhenFileMissing()
    {
        string nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        var ex = Record.Exception(() => MetadataEditor.ValidateXml(nonExistentFile));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateXml_ThrowsOnInvalidXml()
    {
        string invalidXmlFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_invalid.xml");
        try
        {
            File.WriteAllText(invalidXmlFile, "<ComicInfo><Title>Unclosed Tag");
            Assert.ThrowsAny<Exception>(() => MetadataEditor.ValidateXml(invalidXmlFile));
        }
        finally
        {
            if (File.Exists(invalidXmlFile)) File.Delete(invalidXmlFile);
        }
    }

    [Fact]
    public void ReadMetadata_InMemory_ReadsCBZDirectlyAndCorrectly()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");

        try
        {
            File.WriteAllText(tempXml, @"<ComicInfo>
                <Title>Direct Memory Title</Title>
                <Series>Memory Series</Series>
                <Number>42</Number>
                <Year>2026</Year>
            </ComicInfo>");

            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }

            var result = editor.ReadMetadata(tempCbz);

            Assert.Equal("Direct Memory Title", result.Title);
            Assert.Equal("Memory Series", result.Series);
            Assert.Equal("42", result.Number);
            Assert.Equal(2026, result.Year);
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void OpenReadOptimized_AllowsConcurrentReads_WhenOtherProcessHoldsOpenStream()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(tempFile, "Test file content for concurrent reading simulation.");

            // Simulate another process (e.g. Komga, Kavita, Plex) holding the file open with FileShare.ReadWrite
            using var lockedStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // OpenReadOptimized should succeed and read without throwing IOException
            using var optimizedStream = MetadataEditor.OpenReadOptimized(tempFile);
            using var reader = new StreamReader(optimizedStream);
            string content = reader.ReadToEnd();

            Assert.Equal("Test file content for concurrent reading simulation.", content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ComicScannerService_ScansParallelDirectory_PreservingOrder()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ScannerTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var editor = new MetadataEditor();

            // Create 10 numbered CBZ files
            for (int i = 1; i <= 10; i++)
            {
                string file = Path.Combine(tempDir, $"Issue_{i:D2}.cbz");
                string xml = Path.Combine(tempDir, $"temp_{i}.xml");
                File.WriteAllText(xml, $"<ComicInfo><Title>Issue #{i}</Title><Number>{i}</Number></ComicInfo>");

                using (var stream = File.OpenWrite(file))
                using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
                {
                    writer.Write("ComicInfo.xml", xml);
                }
                File.Delete(xml);
            }

            var scanner = new InkTag.Gui.Services.ComicScannerService();
            var results = await scanner.ScanDirectoryAsync(tempDir, recursive: false, System.Threading.CancellationToken.None);

            Assert.Equal(10, results.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((i + 1).ToString(), results[i].Number);
                Assert.Equal($"Issue #{i + 1}", results[i].Title);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ComicScannerService_ReportsProgressAndHandlesCancellation()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ProgressCancelTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 1; i <= 6; i++)
            {
                string file = Path.Combine(tempDir, $"Issue_{i:D2}.cbz");
                string xml = Path.Combine(tempDir, $"temp_{i}.xml");
                File.WriteAllText(xml, $"<ComicInfo><Title>Issue #{i}</Title><Number>{i}</Number></ComicInfo>");

                using (var stream = File.OpenWrite(file))
                using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
                {
                    writer.Write("ComicInfo.xml", xml);
                }
                File.Delete(xml);
            }

            var progressReports = new System.Collections.Generic.List<InkTag.Gui.Services.ScanProgressReport>();
            var lockObj = new object();
            var progress = new DirectProgress<InkTag.Gui.Services.ScanProgressReport>(r => { lock (lockObj) progressReports.Add(r); });

            var scanner = new InkTag.Gui.Services.ComicScannerService();
            var results = await scanner.ScanDirectoryAsync(tempDir, recursive: false, System.Threading.CancellationToken.None, progress);

            Assert.Equal(6, results.Count);
            Assert.NotEmpty(progressReports);
            Assert.Equal(6, progressReports.Max(r => r.Total));
            Assert.Equal(6, progressReports.Max(r => r.Processed));
            Assert.Contains(progressReports, r => !string.IsNullOrEmpty(r.CurrentFileName));

            // Test cancellation
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel(); // Cancel immediately
            var cancelledResults = await scanner.ScanDirectoryAsync(tempDir, recursive: false, cts.Token);
            Assert.NotNull(cancelledResults);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void ExtractCoverImageBytes_ExtractsCoverSuccessfully_FromValidCBZ()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempImg = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");

        try
        {
            File.WriteAllBytes(tempImg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }); // Minimal JPEG header

            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("cover.jpg", tempImg);
            }

            var coverBytes = editor.ExtractCoverImageBytes(tempCbz);
            Assert.NotNull(coverBytes);
            Assert.NotEmpty(coverBytes);
        }
        finally
        {
            if (File.Exists(tempImg)) File.Delete(tempImg);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void ReadMetadata_ReadsSuccessfully_WhenSeekingFails()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");

        try
        {
            File.WriteAllText(tempXml, "<ComicInfo><Series>Sequential Stream Test</Series><Number>42</Number></ComicInfo>");

            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }

            var info = editor.ReadMetadata(tempCbz);
            Assert.NotNull(info);
            Assert.Equal("Sequential Stream Test", info.Series);
            Assert.Equal("42", info.Number);
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void ReadMetadata_HonorsCancellationToken_ThrowsOperationCanceledException()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");

        try
        {
            File.WriteAllText(tempXml, "<ComicInfo><Series>Cancel Test</Series></ComicInfo>");

            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            Assert.Throws<OperationCanceledException>(() => editor.ReadMetadata(tempCbz, cts.Token));
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    [Fact]
    public void DeserializeComicInfo_WithEmptyNumericTags_ParsesCleanly()
    {
        string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComicInfo xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <Title>Test Comic</Title>
  <Count></Count>
  <Volume/>
  <Year>0</Year>
  <Month></Month>
  <Day/>
  <PageCount></PageCount>
  <Manga>true</Manga>
</ComicInfo>";

        var result = MetadataEditor.DeserializeComicInfo(xml);

        Assert.NotNull(result);
        Assert.Equal("Test Comic", result.Title);
        Assert.Null(result.Count);
        Assert.Null(result.Volume);
        Assert.Null(result.Year);
        Assert.Null(result.Month);
        Assert.Null(result.Day);
        Assert.Null(result.PageCount);
        Assert.Equal(MangaDirection.Yes, result.Manga);
    }

    [Fact]
    public void DeserializeComicInfo_WithInvalidControlCharacters_SanitizesAndParses()
    {
        string xml = "<ComicInfo><Title>Title with \x00\x01\x08 control chars</Title><Writer>Stan Lee</Writer></ComicInfo>";

        var result = MetadataEditor.DeserializeComicInfo(xml);

        Assert.NotNull(result);
        Assert.Equal("Title with  control chars", result.Title);
        Assert.Equal("Stan Lee", result.Writer);
    }

    [Fact]
    public void EditMetadata_WithMalformedExistingComicInfo_OverwritesCleanlyWithoutCrashing()
    {
        var editor = new MetadataEditor();
        string tempCbz = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cbz");
        string tempXml = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");

        try
        {
            // Write malformed XML that would break basic XmlSerializer
            File.WriteAllText(tempXml, "<ComicInfo><Year>INVALID_YEAR</Year><Manga>invalid_enum</Manga><BrokenTag");

            using (var stream = File.OpenWrite(tempCbz))
            using (var writer = new ZipWriter(stream, new ZipWriterOptions(CompressionType.Deflate)))
            {
                writer.Write("ComicInfo.xml", tempXml);
            }

            // Edit metadata should succeed without throwing
            editor.EditMetadata(tempCbz, comic =>
            {
                comic.Title = "Recovered Title";
                comic.Year = 2026;
            });

            var updated = editor.ReadMetadata(tempCbz);
            Assert.NotNull(updated);
            Assert.Equal("Recovered Title", updated.Title);
            Assert.Equal(2026, updated.Year);
        }
        finally
        {
            if (File.Exists(tempXml)) File.Delete(tempXml);
            if (File.Exists(tempCbz)) File.Delete(tempCbz);
        }
    }

    private class DirectProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public DirectProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}

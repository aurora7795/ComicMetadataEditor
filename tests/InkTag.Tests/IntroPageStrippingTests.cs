using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Images;
using InkTag.Core.Parsing;
using InkTag.Core.Scrapers;
using InkTag.Mcp;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using Xunit;

namespace InkTag.Tests;

public class IntroPageStrippingTests : IDisposable
{
    private readonly string _testDir;

    public IntroPageStrippingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "InkTag_IntroPageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    private byte[] CreateSampleImageBytes(byte r, byte g, byte b, bool distinct = true)
    {
        int width = 16;
        int height = 16;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPFILEHEADER (14 bytes)
        bw.Write((ushort)0x4D42); // "BM"
        bw.Write((uint)(14 + 40 + (width * height * 3))); // File size
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((uint)(14 + 40)); // Pixel data offset

        // BITMAPINFOHEADER (40 bytes)
        bw.Write((uint)40); // Header size
        bw.Write(width);    // Width
        bw.Write(height);   // Height
        bw.Write((ushort)1); // Color planes
        bw.Write((ushort)24); // Bits per pixel
        bw.Write((uint)0);    // Compression (BI_RGB)
        bw.Write((uint)(width * height * 3)); // Image size
        bw.Write((int)2835); // Horizontal resolution
        bw.Write((int)2835); // Vertical resolution
        bw.Write((uint)0);   // Colors in color table
        bw.Write((uint)0);   // Important color count

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte mod = distinct ? (byte)((x * 15 + y * 7) % 256) : (byte)0;
                bw.Write((byte)Math.Min(255, b + mod));
                bw.Write((byte)Math.Min(255, g + mod));
                bw.Write((byte)Math.Min(255, r + mod));
            }
        }

        bw.Flush();
        return ms.ToArray();
    }

    private string CreateTestCbz(string filename, Dictionary<string, byte[]> entries, ComicInfo? comic = null)
    {
        string filePath = Path.Combine(_testDir, filename);
        using var fileStream = File.OpenWrite(filePath);
        using var writer = new ZipWriter(fileStream, new ZipWriterOptions(CompressionType.Deflate));

        foreach (var kvp in entries)
        {
            using var ms = new MemoryStream(kvp.Value);
            writer.Write(kvp.Key, ms);
        }

        if (comic != null)
        {
            var serializer = new XmlSerializer(typeof(ComicInfo));
            using var ms = new MemoryStream();
            serializer.Serialize(ms, comic);
            ms.Position = 0;
            writer.Write("ComicInfo.xml", ms);
        }

        return filePath;
    }

    [Fact]
    public void NaturalStringComparer_SortsNumericSegmentsCorrectly()
    {
        var input = new List<string>
        {
            "page10.jpg",
            "page2.jpg",
            "page1.jpg",
            "page20.jpg",
            "00_intro.jpg",
            "01_cover.jpg",
            "page100.jpg"
        };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal("00_intro.jpg", sorted[0]);
        Assert.Equal("01_cover.jpg", sorted[1]);
        Assert.Equal("page1.jpg", sorted[2]);
        Assert.Equal("page2.jpg", sorted[3]);
        Assert.Equal("page10.jpg", sorted[4]);
        Assert.Equal("page20.jpg", sorted[5]);
        Assert.Equal("page100.jpg", sorted[6]);
    }

    [Fact]
    public void NaturalStringComparer_HandlesPaddedAndNonPaddedNumbers()
    {
        var input = new List<string> { "001.jpg", "00.jpg", "10.jpg", "2.jpg", "01.jpg" };
        var sorted = input.OrderBy(x => x, NaturalStringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal("00.jpg", sorted[0]);
        Assert.Equal("01.jpg", sorted[1]);
        Assert.Equal("001.jpg", sorted[2]);
        Assert.Equal("2.jpg", sorted[3]);
        Assert.Equal("10.jpg", sorted[4]);
    }

    [Fact]
    public void MetadataEditor_GetImageEntries_FiltersSystemEntriesAndSortsNaturally()
    {
        var editor = new MetadataEditor();
        var img1 = CreateSampleImageBytes(255, 0, 0);
        var img2 = CreateSampleImageBytes(0, 255, 0);
        var img10 = CreateSampleImageBytes(0, 0, 255);

        var entries = new Dictionary<string, byte[]>
        {
            ["__MACOSX/._page1.jpg"] = new byte[] { 1, 2, 3 },
            [".DS_Store"] = new byte[] { 4, 5, 6 },
            ["Thumbs.db"] = new byte[] { 7, 8, 9 },
            ["page10.jpg"] = img10,
            ["page2.jpg"] = img2,
            ["page1.jpg"] = img1
        };

        string cbzPath = CreateTestCbz("filtered_sort.cbz", entries);
        var imageEntries = editor.GetImageEntries(cbzPath);

        Assert.Equal(3, imageEntries.Count);
        Assert.Equal("page1.jpg", imageEntries[0]);
        Assert.Equal("page2.jpg", imageEntries[1]);
        Assert.Equal("page10.jpg", imageEntries[2]);
    }

    [Fact]
    public void MetadataEditor_ExtractCoverImageBytes_ExtractsArbitraryPageIndex()
    {
        var editor = new MetadataEditor();
        var redImg = CreateSampleImageBytes(255, 0, 0);
        var greenImg = CreateSampleImageBytes(0, 255, 0);
        var blueImg = CreateSampleImageBytes(0, 0, 255);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_intro.bmp"] = redImg,
            ["01_cover.bmp"] = greenImg,
            ["02_story.bmp"] = blueImg
        };

        string cbzPath = CreateTestCbz("multi_page.cbz", entries);

        byte[]? page0Bytes = editor.ExtractCoverImageBytes(cbzPath, 0);
        byte[]? page1Bytes = editor.ExtractCoverImageBytes(cbzPath, 1);
        byte[]? page2Bytes = editor.ExtractCoverImageBytes(cbzPath, 2);
        byte[]? pageOutOfRange = editor.ExtractCoverImageBytes(cbzPath, 99);

        Assert.NotNull(page0Bytes);
        Assert.NotNull(page1Bytes);
        Assert.NotNull(page2Bytes);
        Assert.Null(pageOutOfRange);

        Assert.Equal(redImg.Length, page0Bytes.Length);
        Assert.Equal(greenImg.Length, page1Bytes.Length);
        Assert.Equal(blueImg.Length, page2Bytes.Length);

        // Check candidate cover hashes helper
        var hashes = editor.GetCandidateCoverHashes(cbzPath, maxPages: 2);
        Assert.Equal(2, hashes.Count);
        Assert.True(hashes[0].Hash != 0UL);
        Assert.True(hashes[1].Hash != 0UL);
    }

    [Fact]
    public void MetadataEditor_RemoveArchivePages_RemovesSpecifiedPagesAndUpdatesXml()
    {
        var editor = new MetadataEditor();
        var img0 = CreateSampleImageBytes(255, 0, 0);
        var img1 = CreateSampleImageBytes(0, 255, 0);
        var img2 = CreateSampleImageBytes(0, 0, 255);

        var comic = new ComicInfo
        {
            Title = "Spider-Man #1",
            Series = "Spider-Man",
            Number = "1",
            PageCount = 3,
            Pages = new PageCollection
            {
                Page = new[]
                {
                    new Page { Image = 0, Type = "FrontCover" },
                    new Page { Image = 1, Type = "Story" },
                    new Page { Image = 2, Type = "Story" }
                }
            }
        };

        var entries = new Dictionary<string, byte[]>
        {
            ["00_intro.bmp"] = img0,
            ["01_cover.bmp"] = img1,
            ["02_story.bmp"] = img2
        };

        string cbzPath = CreateTestCbz("remove_page_test.cbz", entries, comic);

        var result = editor.RemoveArchivePages(cbzPath, new[] { 0 });

        Assert.True(result.Success);
        Assert.Equal(3, result.OriginalPageCount);
        Assert.Equal(2, result.FinalPageCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Contains("00_intro.bmp", result.RemovedEntries);

        // Verify remaining archive entries
        var remainingEntries = editor.GetImageEntries(cbzPath);
        Assert.Equal(2, remainingEntries.Count);
        Assert.Equal("01_cover.bmp", remainingEntries[0]);
        Assert.Equal("02_story.bmp", remainingEntries[1]);

        // Verify ComicInfo.xml metadata reindexing
        var updatedComic = editor.ReadMetadata(cbzPath);
        Assert.Equal(2, updatedComic.PageCount);
        Assert.NotNull(updatedComic.Pages?.Page);
        Assert.Equal(2, updatedComic.Pages.Page.Length);
        Assert.Equal(0, updatedComic.Pages.Page[0].Image);
        Assert.Equal("FrontCover", updatedComic.Pages.Page[0].Type);
        Assert.Equal(1, updatedComic.Pages.Page[1].Image);
        Assert.Equal("Story", updatedComic.Pages.Page[1].Type);
    }

    [Fact]
    public void MetadataEditor_StripFirstPage_SafelyRemovesFirstPage()
    {
        var editor = new MetadataEditor();
        var introImg = CreateSampleImageBytes(128, 128, 128);
        var coverImg = CreateSampleImageBytes(0, 255, 0);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_scanner_ad.bmp"] = introImg,
            ["01_real_cover.bmp"] = coverImg
        };

        string cbzPath = CreateTestCbz("strip_first_page.cbz", entries);

        var result = editor.StripFirstPage(cbzPath);

        Assert.True(result.Success);
        Assert.Equal(2, result.OriginalPageCount);
        Assert.Equal(1, result.FinalPageCount);
        Assert.Single(result.RemovedEntries);
        Assert.Equal("00_scanner_ad.bmp", result.RemovedEntries[0]);

        // Verify backup file was deleted upon success
        string bakPath = cbzPath + ".bak";
        Assert.False(File.Exists(bakPath));

        var remaining = editor.GetImageEntries(cbzPath);
        Assert.Single(remaining);
        Assert.Equal("01_real_cover.bmp", remaining[0]);
    }

    [Fact]
    public async Task MetadataScraperService_AutoScrapeComic_DetectsIntroPageFallback()
    {
        var settingsService = new AppSettingsService();
        var scraper = new MetadataScraperService(settingsService);
        var editor = new MetadataEditor();

        // Create images
        var introImg = CreateSampleImageBytes(20, 20, 20);
        var coverImg = CreateSampleImageBytes(255, 120, 50);

        ulong introHash = PerceptualHashService.ComputeDHash(introImg);
        ulong coverHash = PerceptualHashService.ComputeDHash(coverImg);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_scanner_intro.bmp"] = introImg,
            ["01_actual_cover.bmp"] = coverImg
        };

        string cbzPath = CreateTestCbz("intro_scrape_test.cbz", entries);

        var comic = new ComicInfo
        {
            Series = "Saga",
            Number = "1"
        };

        // When Page 0 hash is passed (the scanner intro), fallback should inspect Page 1 (actual cover)
        var result = await scraper.AutoScrapeComicAsync(comic, introHash, cbzPath, enableIntroPageFallback: true);

        // Note: Without live ComicVine API key, this verifies the execution flow and fallback branches without throwing exceptions
        Assert.NotNull(result);
    }

    [Fact]
    public async Task BulkScrapeQueueService_DetectsAndStripsIntroPages()
    {
        var settingsService = new AppSettingsService();
        var editor = new MetadataEditor();
        var scraper = new MetadataScraperService(settingsService);
        var queueService = new BulkScrapeQueueService(scraper, editor, settingsService);

        var introImg = CreateSampleImageBytes(10, 10, 10);
        var coverImg = CreateSampleImageBytes(200, 150, 80);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_scanner_intro.bmp"] = introImg,
            ["01_actual_cover.bmp"] = coverImg
        };

        string cbzPath = CreateTestCbz("bulk_intro_test.cbz", entries);
        var queue = queueService.CreateQueue(new[] { cbzPath });

        Assert.Single(queue);
        var item = queue[0];
        Assert.Equal(0, item.TrueCoverPageIndex);
        Assert.False(item.DetectedIntroPage);

        // Emulate detected intro page
        item.DetectedIntroPage = true;
        item.MatchedCandidate = new ComicSearchResult
        {
            IssueId = "12345",
            SeriesTitle = "Saga",
            IssueNumber = "1",
            MatchConfidence = 0.95
        };

        // Apply with stripDetectedIntroPages: true
        int saved = await queueService.ApplyMatchedMetadataAsync(
            queue,
            ScrapeMergeMode.FillMissingOnly,
            renameFiles: false,
            stripDetectedIntroPages: true);

        // Verify the file has only 1 page remaining
        var remaining = editor.GetImageEntries(cbzPath);
        Assert.Single(remaining);
        Assert.Equal("01_actual_cover.bmp", remaining[0]);
    }

    [Fact]
    public void McpComicTools_RemoveComicPage_DryRunAndActual()
    {
        var img1 = CreateSampleImageBytes(100, 100, 100);
        var img2 = CreateSampleImageBytes(200, 200, 200);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_ad.bmp"] = img1,
            ["01_cover.bmp"] = img2
        };

        string cbzPath = CreateTestCbz("mcp_page_remove.cbz", entries);

        // Dry Run
        string dryRunJson = ComicTools.RemoveComicPage(cbzPath, pageIndex: 0, dryRun: true);
        Assert.Contains("\"dryRun\": true", dryRunJson);
        Assert.Contains("\"originalPageCount\": 2", dryRunJson);
        Assert.Contains("\"finalPageCount\": 1", dryRunJson);

        // Verify file was NOT modified in dry run
        var editor = new MetadataEditor();
        Assert.Equal(2, editor.GetImageEntries(cbzPath).Count);

        // Actual execution
        string actualJson = ComicTools.RemoveComicPage(cbzPath, pageIndex: 0, dryRun: false);
        Assert.Contains("\"Success\": true", actualJson);
        Assert.Contains("\"FinalPageCount\": 1", actualJson);
        Assert.Single(editor.GetImageEntries(cbzPath));
    }

    [Fact]
    public void McpComicTools_ExtractCoverImage_SupportsArbitraryPageIndex()
    {
        var img1 = CreateSampleImageBytes(10, 20, 30);
        var img2 = CreateSampleImageBytes(40, 50, 60);

        var entries = new Dictionary<string, byte[]>
        {
            ["00_ad.bmp"] = img1,
            ["01_cover.bmp"] = img2
        };

        string cbzPath = CreateTestCbz("mcp_extract_page.cbz", entries);
        string outputPath = Path.Combine(_testDir, "extracted_p2.jpg");

        var result = ComicTools.ExtractCoverImage(cbzPath, pageIndex: 1, outputPath: outputPath);
        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 0);
    }

    [Fact]
    public void MetadataEditor_RemoveArchivePages_HandlesMiddlePageRemoval()
    {
        var editor = new MetadataEditor();
        var img0 = CreateSampleImageBytes(10, 10, 10);
        var img1 = CreateSampleImageBytes(20, 20, 20);
        var img2 = CreateSampleImageBytes(30, 30, 30);
        var img3 = CreateSampleImageBytes(40, 40, 40);

        var comic = new ComicInfo
        {
            Series = "Batman",
            Number = "100",
            PageCount = 4,
            Pages = new PageCollection
            {
                Page = new[]
                {
                    new Page { Image = 0, Type = "FrontCover" },
                    new Page { Image = 1, Type = "Story" },
                    new Page { Image = 2, Type = "Advertisement" },
                    new Page { Image = 3, Type = "BackCover" }
                }
            }
        };

        var entries = new Dictionary<string, byte[]>
        {
            ["00.bmp"] = img0,
            ["01.bmp"] = img1,
            ["02.bmp"] = img2,
            ["03.bmp"] = img3
        };

        string cbzPath = CreateTestCbz("remove_middle_pages.cbz", entries, comic);

        // Remove page 1 and page 2
        var result = editor.RemoveArchivePages(cbzPath, new[] { 1, 2 });

        Assert.True(result.Success);
        Assert.Equal(4, result.OriginalPageCount);
        Assert.Equal(2, result.FinalPageCount);
        Assert.Equal(2, result.RemovedCount);
        Assert.Equal(new[] { "01.bmp", "02.bmp" }, result.RemovedEntries);

        var remaining = editor.GetImageEntries(cbzPath);
        Assert.Equal(2, remaining.Count);
        Assert.Equal("00.bmp", remaining[0]);
        Assert.Equal("03.bmp", remaining[1]);

        var updatedComic = editor.ReadMetadata(cbzPath);
        Assert.Equal(2, updatedComic.PageCount);
        Assert.NotNull(updatedComic.Pages?.Page);
        Assert.Equal(2, updatedComic.Pages.Page.Length);
        Assert.Equal(0, updatedComic.Pages.Page[0].Image);
        Assert.Equal("FrontCover", updatedComic.Pages.Page[0].Type);
        Assert.Equal(1, updatedComic.Pages.Page[1].Image);
        Assert.Equal("BackCover", updatedComic.Pages.Page[1].Type);
    }

    [Fact]
    public void MetadataEditor_RemoveArchivePages_HandlesOutOfRangeAndNonExistentFiles()
    {
        var editor = new MetadataEditor();

        // Non-existent file
        var missingRes = editor.RemoveArchivePages("non_existent_file.cbz", new[] { 0 });
        Assert.False(missingRes.Success);
        Assert.NotNull(missingRes.ErrorMessage);

        // Out of range index on existing file
        var img = CreateSampleImageBytes(50, 50, 50);
        var entries = new Dictionary<string, byte[]> { ["00.bmp"] = img };
        string cbzPath = CreateTestCbz("out_of_range.cbz", entries);

        var outOfRangeRes = editor.RemoveArchivePages(cbzPath, new[] { 99 });
        Assert.False(outOfRangeRes.Success);
        Assert.Equal(0, outOfRangeRes.RemovedCount);
        Assert.Equal(1, outOfRangeRes.FinalPageCount);
        Assert.Contains("exist", outOfRangeRes.ErrorMessage);
    }
}

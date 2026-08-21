using System;
using System.IO;
using System.Linq;
using InkTag.Core;
using InkTag.Core.Renaming;
using Xunit;

namespace InkTag.Tests;

public class RenamingTests
{
    [Fact]
    public void GenerateFilename_StandardDefaultTemplate_FormatsCorrectly()
    {
        var comic = new ComicInfo
        {
            Series = "The Avengers",
            Number = "13",
            Year = 1965
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "/comics/old_avengers.cbz");

        Assert.Equal("The Avengers #013 (1965).cbz", filename);
    }

    [Fact]
    public void GenerateFilename_WithStoryTitle_FormatsCorrectly()
    {
        var comic = new ComicInfo
        {
            Series = "The Avengers",
            Number = "13",
            Year = 1965,
            Title = "The Castle of Count Nefaria"
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "/comics/old.cbz", ComicFileRenamer.TemplateWithTitle);

        Assert.Equal("The Avengers #013 - The Castle of Count Nefaria (1965).cbz", filename);
    }

    [Fact]
    public void GenerateFilename_NumberlessTemplate_FormatsCorrectly()
    {
        var comic = new ComicInfo
        {
            Series = "The Avengers",
            Number = "1",
            Year = 1963
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "/comics/old.cbz", ComicFileRenamer.TemplateNumberless);

        Assert.Equal("The Avengers 001 (1963).cbz", filename);
    }

    [Fact]
    public void GenerateFilename_SpecialIssueNumbers_PreservesDecimalsAndFractions()
    {
        var comic1 = new ComicInfo
        {
            Series = "Deadpool",
            Number = "1.5",
            Year = 2014
        };

        var comic2 = new ComicInfo
        {
            Series = "Avengers",
            Number = "1½",
            Year = 1999
        };

        string file1 = ComicFileRenamer.GenerateFilename(comic1, "deadpool.cbz", "{Series} #{Number:3} ({Year})");
        string file2 = ComicFileRenamer.GenerateFilename(comic2, "avengers.cbz", "{Series} #{Number:3} ({Year})");

        Assert.Equal("Deadpool #001.5 (2014).cbz", file1);
        Assert.Equal("Avengers #001½ (1999).cbz", file2);
    }

    [Fact]
    public void GenerateFilename_PreservesScanInfo_AppendsTrailingTags()
    {
        var comic = new ComicInfo
        {
            Series = "Saga",
            Number = "1",
            Year = 2012,
            ScanInformation = "digital"
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "saga.cbz", preserveScanInfo: true);

        Assert.Equal("Saga #001 (2012) (digital).cbz", filename);
    }

    [Fact]
    public void GenerateFilename_ClearsScanInfo_ByDefault()
    {
        var comic = new ComicInfo
        {
            Series = "The Avengers",
            Number = "34",
            Year = 1966,
            ScanInformation = "digital"
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "The Avengers 034 (1966) (digital).cbz");

        Assert.Equal("The Avengers #034 (1966).cbz", filename);
    }

    [Fact]
    public void GenerateFilename_GracefulCollapse_RemovesEmptyParenthesesWhenYearMissing()
    {
        var comic = new ComicInfo
        {
            Series = "Invincible",
            Number = "5"
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "invincible.cbz", "{Series} #{Number:3} ({Year})");

        Assert.Equal("Invincible #005.cbz", filename);
    }

    [Fact]
    public void GenerateFilename_SanitizesIllegalCharacters()
    {
        var comic = new ComicInfo
        {
            Series = "Spider-Man: Life Story",
            Number = "1",
            Year = 2019,
            Title = "The '60s: All-Out War?"
        };

        string filename = ComicFileRenamer.GenerateFilename(comic, "spidey.cbz", "{Series} #{Number} - {Title} ({Year})");

        Assert.DoesNotContain(":", filename);
        Assert.DoesNotContain("?", filename);
        Assert.Equal("Spider-Man - Life Story #1 - The '60s - All-Out War (2019).cbz", filename);
    }

    [Fact]
    public void PreviewBatchRename_DetectsCollisionsCorrectly()
    {
        string dir = Path.GetTempPath();
        string file1 = Path.Combine(dir, "f1.cbz");
        string file2 = Path.Combine(dir, "f2.cbz");

        File.WriteAllText(file1, "dummy");
        File.WriteAllText(file2, "dummy");

        try
        {
            var comic1 = new ComicInfo { Series = "Avengers", Number = "1", Year = 1963 };
            var comic2 = new ComicInfo { Series = "Avengers", Number = "1", Year = 1963 };

            var previews = ComicFileRenamer.PreviewBatchRename(new[]
            {
                (file1, comic1),
                (file2, comic2)
            });

            Assert.Equal(2, previews.Count);
            Assert.False(previews[0].HasCollision);
            Assert.True(previews[1].HasCollision);
        }
        finally
        {
            if (File.Exists(file1)) File.Delete(file1);
            if (File.Exists(file2)) File.Delete(file2);
        }
    }

    [Fact]
    public void RenameFile_PerformsPhysicalFileMove()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"inktag_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string oldPath = Path.Combine(tempDir, "old_name.cbz");
        File.WriteAllText(oldPath, "comic content");

        try
        {
            string newPath = ComicFileRenamer.RenameFile(oldPath, "new_name.cbz");

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
            Assert.Equal("new_name.cbz", Path.GetFileName(newPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

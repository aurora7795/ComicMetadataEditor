using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Core;
using InkTag.Core.Parsing;
using Xunit;

namespace InkTag.Tests;

public class ComicBookInfoParserTests
{
    [Fact]
    public void ComicBookInfoParser_ParsesStandardXCbiJson()
    {
        string cbiJson = """
        {
          "appID": "ComicBookLover/1.0",
          "x-cbi": {
            "series": "The Amazing Spider-Man",
            "title": "The Night Gwen Stacy Died",
            "issue": "121",
            "volume": 1,
            "numberOfVolumes": 1,
            "numberOfIssues": 441,
            "publisher": "Marvel",
            "imprint": "Marvel Comics",
            "publicationYear": 1973,
            "publicationMonth": 6,
            "genre": "Superhero",
            "tags": ["Spider-Man", "Green Goblin", "Death"],
            "rating": 5,
            "comments": "Iconic issue where Gwen Stacy falls from the George Washington Bridge.",
            "credits": [
              { "person": "Gerry Conway", "role": "Writer" },
              { "person": "Gil Kane", "role": "Penciller" },
              { "person": "John Romita Sr.", "role": "Inker" },
              { "person": "Dave Hunt", "role": "Colorist" },
              { "person": "Artie Simek", "role": "Letterer" },
              { "person": "Roy Thomas", "role": "Editor" }
            ]
          }
        }
        """;

        bool success = ComicBookInfoParser.TryParse(cbiJson, out var comic);

        Assert.True(success);
        Assert.NotNull(comic);
        Assert.True(comic!.HasLegacyMetadata);
        Assert.Equal("The Amazing Spider-Man", comic.Series);
        Assert.Equal("The Night Gwen Stacy Died", comic.Title);
        Assert.Equal("121", comic.Number);
        Assert.Equal(1, comic.Volume);
        Assert.Equal(441, comic.Count);
        Assert.Equal("Marvel", comic.Publisher);
        Assert.Equal("Marvel Comics", comic.Imprint);
        Assert.Equal(1973, comic.Year);
        Assert.Equal(6, comic.Month);
        Assert.Equal("Superhero", comic.Genre);
        Assert.Contains("Spider-Man", comic.Tags);
        Assert.Equal(5m, comic.CommunityRating);
        Assert.Contains("George Washington Bridge", comic.Summary);
        Assert.Equal("Gerry Conway", comic.Writer);
        Assert.Equal("Gil Kane", comic.Penciller);
        Assert.Equal("John Romita Sr.", comic.Inker);
        Assert.Equal("Dave Hunt", comic.Colorist);
        Assert.Equal("Artie Simek", comic.Letterer);
        Assert.Equal("Roy Thomas", comic.Editor);
    }

    [Fact]
    public void ComicBookInfoParser_ParsesMultipleCreatorsPerRole()
    {
        string cbiJson = """
        {
          "x-cbi": {
            "series": "Batman",
            "credits": [
              { "person": "Scott Snyder", "role": "Writer" },
              { "person": "James Tynion IV", "role": "Script" },
              { "person": "Greg Capullo", "role": "Pencils" },
              { "person": "Danny Miki", "role": "Inks" }
            ]
          }
        }
        """;

        bool success = ComicBookInfoParser.TryParse(cbiJson, out var comic);

        Assert.True(success);
        Assert.NotNull(comic);
        Assert.Equal("Scott Snyder, James Tynion IV", comic!.Writer);
        Assert.Equal("Greg Capullo", comic.Penciller);
        Assert.Equal("Danny Miki", comic.Inker);
    }

    [Fact]
    public void ComicBookInfoParser_MergesMissingFieldsWithoutOverwriting()
    {
        var existing = new ComicInfo
        {
            Series = "Existing Series",
            Number = "5",
            Writer = "Existing Writer"
        };

        string cbiJson = """
        {
          "x-cbi": {
            "series": "Legacy Series",
            "issue": "99",
            "publicationYear": 1985,
            "publisher": "DC Comics",
            "credits": [
              { "person": "Legacy Writer", "role": "Writer" },
              { "person": "Legacy Artist", "role": "Artist" }
            ]
          }
        }
        """;

        bool merged = ComicBookInfoParser.TryMergeFromLegacyJson(existing, cbiJson);

        Assert.True(merged);
        // Existing values must NOT be overwritten
        Assert.Equal("Existing Series", existing.Series);
        Assert.Equal("5", existing.Number);
        Assert.Equal("Existing Writer", existing.Writer);

        // Missing values must be backfilled
        Assert.Equal(1985, existing.Year);
        Assert.Equal("DC Comics", existing.Publisher);
        Assert.Equal("Legacy Artist", existing.Penciller);
    }

    [Fact]
    public void ComicBookInfoParser_HandlesInvalidOrPlainTextGracefully()
    {
        Assert.False(ComicBookInfoParser.TryParse("", out _));
        Assert.False(ComicBookInfoParser.TryParse("Scanned by Minutemen 2012", out _));
        Assert.False(ComicBookInfoParser.TryParse("{ not valid json }", out _));
        Assert.False(ComicBookInfoParser.TryParse("[]", out _));
    }

    [Fact]
    public void MetadataEditor_ReadsMetadataFromZipComment_AndUpgradesToComicInfoXml()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "InkTagCbiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string cbzPath = Path.Combine(tempDir, "test_cbi.cbz");

        string cbiComment = """
        {
          "appID": "ComicTagger/1.0",
          "x-cbi": {
            "series": "Daredevil",
            "issue": "181",
            "publicationYear": 1982,
            "publisher": "Marvel",
            "credits": [
              { "person": "Frank Miller", "role": "Writer" },
              { "person": "Klaus Janson", "role": "Penciller" }
            ]
          }
        }
        """;

        // 1. Create a zip archive with a zip comment containing ComicBookInfo JSON and NO ComicInfo.xml
        using (var fileStream = File.Create(cbzPath))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            zip.Comment = cbiComment;
            var entry = zip.CreateEntry("page_001.jpg");
            using var entryStream = entry.Open();
            entryStream.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 });
        }

        try
        {
            var editor = new MetadataEditor();

            // 2. Read metadata: should ingest from CBI comment
            var readInfo = editor.ReadMetadata(cbzPath, out bool hasXml, out bool usedSequential);
            Assert.False(hasXml); // No ComicInfo.xml yet
            Assert.True(readInfo.HasLegacyMetadata);
            Assert.Equal("Daredevil", readInfo.Series);
            Assert.Equal("181", readInfo.Number);
            Assert.Equal(1982, readInfo.Year);
            Assert.Equal("Marvel", readInfo.Publisher);
            Assert.Equal("Frank Miller", readInfo.Writer);
            Assert.Equal("Klaus Janson", readInfo.Penciller);

            // 3. Save / Edit metadata: should write standard ComicInfo.xml
            editor.EditMetadata(cbzPath, info =>
            {
                info.Notes = "Upgraded to ComicInfo.xml standard";
            });

            // 4. Re-read metadata: should now have true embedded ComicInfo.xml
            var upgradedInfo = editor.ReadMetadata(cbzPath, out bool hasUpgradedXml, out _);
            Assert.True(hasUpgradedXml);
            Assert.Equal("Daredevil", upgradedInfo.Series);
            Assert.Equal("181", upgradedInfo.Number);
            Assert.Equal(1982, upgradedInfo.Year);
            Assert.Equal("Frank Miller", upgradedInfo.Writer);
            Assert.Equal("Upgraded to ComicInfo.xml standard", upgradedInfo.Notes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

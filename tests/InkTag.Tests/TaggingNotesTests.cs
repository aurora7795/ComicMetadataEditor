using System;
using System.IO;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using Xunit;

namespace InkTag.Tests;

public class TaggingNotesTests
{
    [Fact]
    public void GenerateTaggingNote_WithVolumeId_GeneratesExtendedAttributionFormat()
    {
        string note = ComicVineProvider.GenerateTaggingNote("120048", "18234");

        Assert.StartsWith("Tagged with InkTag ", note);
        Assert.Contains("using info from Comic Vine on ", note);
        Assert.Contains("[Issue ID 120048]", note);
        Assert.EndsWith("[Volume ID 18234]", note);
    }

    [Fact]
    public void GenerateTaggingNote_WithoutVolumeId_GeneratesStandardAttributionFormat()
    {
        string note = ComicVineProvider.GenerateTaggingNote("120048", null);

        Assert.StartsWith("Tagged with InkTag ", note);
        Assert.Contains("using info from Comic Vine on ", note);
        Assert.EndsWith("[Issue ID 120048]", note);
        Assert.DoesNotContain("[Volume ID", note);
    }

    [Fact]
    public void GenerateTaggingNote_WithVisualMatch_IncludesCoverMatchTag()
    {
        string note = ComicVineProvider.GenerateTaggingNote("120048", "18234", visualSimilarity: 0.965);

        Assert.StartsWith("Tagged with InkTag ", note);
        Assert.Contains("[Issue ID 120048]", note);
        Assert.Contains("[Volume ID 18234]", note);
        Assert.EndsWith("[Cover Match 97%]", note);
    }

    [Fact]
    public void MergeNotes_WhenExistingNotesIsEmpty_ReturnsNewAttributionNote()
    {
        string newNote = "Tagged with InkTag 0.11.0 using info from Comic Vine on 2026-08-22 08:30:00. [Issue ID 120048]";
        string merged = MetadataScraperService.MergeNotes(null, newNote);

        Assert.Equal(newNote, merged);
    }

    [Fact]
    public void MergeNotes_WhenExistingNotesHasPreviousTaggingNote_ReplacesPreviousTaggingNote()
    {
        string existing = "My custom personal note.\nTagged with ComicTagger 1.5.5 using info from Comic Vine on 2026-02-13 23:14:05. [Issue ID 9995]\nAnother note line.";
        string newNote = "Tagged with InkTag 0.11.0 using info from Comic Vine on 2026-08-22 08:30:00. [Issue ID 120048] [Volume ID 18234]";

        string merged = MetadataScraperService.MergeNotes(existing, newNote);

        Assert.Contains("My custom personal note.", merged);
        Assert.Contains("Another note line.", merged);
        Assert.Contains(newNote, merged);
        Assert.DoesNotContain("ComicTagger", merged);
    }

    [Fact]
    public void MergeNotes_WhenExistingNotesHasOnlyCustomComments_AppendsNewAttributionNote()
    {
        string existing = "First print copy signed by Stan Lee.";
        string newNote = "Tagged with InkTag 0.11.0 using info from Comic Vine on 2026-08-22 08:30:00. [Issue ID 120048]";

        string merged = MetadataScraperService.MergeNotes(existing, newNote);

        Assert.Contains("First print copy signed by Stan Lee.", merged);
        Assert.Contains(newNote, merged);
    }

    [Fact]
    public void ApplyMetadata_WhenWriteTaggingAttributionEnabled_AppliesMergedNotes()
    {
        string tempSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempSettingsFile);
            settingsService.Settings.WriteTaggingAttributionToNotes = true;
            var scraperService = new MetadataScraperService(settingsService);

            var target = new ComicInfo
            {
                Title = "Original Title",
                Notes = "User personal comment"
            };

            var fetched = new ComicInfo
            {
                Title = "Scraped Title",
                Notes = "Tagged with InkTag 0.11.0 using info from Comic Vine on 2026-08-22 08:30:00. [Issue ID 100]"
            };

            scraperService.ApplyMetadata(target, fetched, ScrapeMergeMode.OverwriteAll);

            Assert.Contains("User personal comment", target.Notes);
            Assert.Contains("Tagged with InkTag", target.Notes);
            Assert.Contains("[Issue ID 100]", target.Notes);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }

    [Fact]
    public void ApplyMetadata_WhenWriteTaggingAttributionDisabled_DoesNotOverwriteNotesWhenPreserved()
    {
        string tempSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
        try
        {
            var settingsService = new AppSettingsService(tempSettingsFile);
            settingsService.Settings.WriteTaggingAttributionToNotes = false;
            var scraperService = new MetadataScraperService(settingsService);

            var target = new ComicInfo
            {
                Title = "Original Title",
                Notes = "User personal comment"
            };

            var fetched = new ComicInfo
            {
                Title = "Scraped Title",
                Notes = "Tagged with InkTag 0.11.0 using info from Comic Vine on 2026-08-22 08:30:00. [Issue ID 100]"
            };

            scraperService.ApplyMetadata(target, fetched, ScrapeMergeMode.FillMissingOnly);

            Assert.Equal("User personal comment", target.Notes);
            Assert.DoesNotContain("Tagged with InkTag", target.Notes);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }
}

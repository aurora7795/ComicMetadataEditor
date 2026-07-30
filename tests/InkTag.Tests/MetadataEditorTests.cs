using System;
using System.IO;
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
            Manga = "No"
        });

        MetadataEditor.ApplyJsonPatch(comic, jsonPatch);

        Assert.Equal("Updated Title", comic.Title);
        Assert.Equal("Test Writer", comic.Writer);
        Assert.Equal(2026, comic.Year);
        Assert.Equal(10, comic.Count);
        Assert.Equal("No", comic.Manga);
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
}

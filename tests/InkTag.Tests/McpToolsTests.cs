using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using InkTag.Mcp;
using Xunit;

namespace InkTag.Tests;

[Collection("ProcessStateTests")]
public class McpToolsTests
{
    private string CreateTestCbz(string comicInfoXml = "<ComicInfo><Title>MCP Test</Title></ComicInfo>")
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
    public void ReadComicMetadata_ReturnsFormattedMetadata()
    {
        string cbz = CreateTestCbz("<ComicInfo><Title>Read Test</Title></ComicInfo>");
        try
        {
            string result = ComicTools.ReadComicMetadata(cbz);
            Assert.Contains("Read Test", result);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void GetComicSchema_ReturnsValidJsonSchema()
    {
        string schema = ComicTools.GetComicSchema();
        Assert.Contains("ComicInfo", schema);
        Assert.Contains("properties", schema);
    }

    [Fact]
    public void UpdateComicMetadata_AppliesPatchSuccessfully()
    {
        string cbz = CreateTestCbz();
        try
        {
            using var doc = JsonDocument.Parse("{\"Writer\": \"MCP Author\"}");
            string result = ComicTools.UpdateComicMetadata(cbz, doc.RootElement, dryRun: false, recursive: false);
            Assert.Contains("Writer", result);

            string readBack = ComicTools.ReadComicMetadata(cbz);
            Assert.Contains("MCP Author", readBack);
        }
        finally
        {
            if (File.Exists(cbz)) File.Delete(cbz);
        }
    }

    [Fact]
    public void ScanComics_FindsComicsInDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string cbz = CreateTestCbz("<ComicInfo><Series>Scan Series</Series></ComicInfo>");
        string destCbz = Path.Combine(tempDir, "comic1.cbz");
        File.Move(cbz, destCbz);

        try
        {
            string result = ComicTools.ScanComics(tempDir, new[] { "Writer" }, recursive: false);
            Assert.Contains("comic1.cbz", result);
            Assert.Contains("Scan Series", result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ValidatePathAccess_AllowsPathsInsideDefaultRoots()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "allowed_test.cbz");
        // Should not throw
        ComicTools.ValidatePathAccess(tempFile);
    }

    [Fact]
    public void ValidatePathAccess_ThrowsUnauthorizedAccessException_ForPathsOutsideAllowedRoots()
    {
        string? originalEnv = Environment.GetEnvironmentVariable("INKTAG_ALLOWED_ROOT_PATHS");
        string tempAllowedDir = Path.Combine(Path.GetTempPath(), "mcp_allowed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempAllowedDir);

        try
        {
            Environment.SetEnvironmentVariable("INKTAG_ALLOWED_ROOT_PATHS", tempAllowedDir);

            // Access inside allowed root succeeds
            string allowedFile = Path.Combine(tempAllowedDir, "allowed.cbz");
            ComicTools.ValidatePathAccess(allowedFile);

            // Access outside allowed root throws UnauthorizedAccessException
            string forbiddenFile = "/etc/shadow";
            Assert.Throws<UnauthorizedAccessException>(() => ComicTools.ValidatePathAccess(forbiddenFile));
        }
        finally
        {
            Environment.SetEnvironmentVariable("INKTAG_ALLOWED_ROOT_PATHS", originalEnv);
            if (Directory.Exists(tempAllowedDir)) Directory.Delete(tempAllowedDir, true);
        }
    }
}

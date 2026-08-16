using System;
using InkTag.Core.Parsing;
using Xunit;

namespace InkTag.Tests;

public class FilenameParserTests
{
    [Theory]
    [InlineData("Blankets 03 (2003) (Miracle Man-LXC).cbz", "Blankets", "3", 2003, null, "Miracle Man-LXC")]
    [InlineData("Blankets 02 (2003) (Miracle Man-LXC).cbz", "Blankets", "2", 2003, null, "Miracle Man-LXC")]
    [InlineData("Blankets #001 (2003).cbz", "Blankets", "1", 2003, null, "")]
    [InlineData("The Amazing Spider-Man 300 (1988) (Digital) (Minutemen-Zone).cbr", "The Amazing Spider-Man", "300", 1988, null, "Digital")]
    [InlineData("Batman - Year One (1987) #01.cbz", "Batman - Year One", "1", 1987, null, "")]
    [InlineData("Watchmen 01 of 12 (1986).cbz", "Watchmen", "1", 1986, null, "")]
    [InlineData("Watchmen #01 (of 12) (1986).cbz", "Watchmen", "1", 1986, null, "")]
    [InlineData("Invincible v01 #02 (2003).cbr", "Invincible", "2", 2003, 1, "")]
    [InlineData("X-Men #1.5 (1998).cbz", "X-Men", "1.5", 1998, null, "")]
    [InlineData("Saga 054 (2018) (Digital) (Empire).cbz", "Saga", "54", 2018, null, "Digital")]
    [InlineData("The Walking Dead 01 (2003) (c2c).cbz", "The Walking Dead", "1", 2003, null, "c2c")]
    [InlineData("/mnt/comics/Eden - It's an Endless World! 02 (2006).cbz", "Eden - It's an Endless World!", "2", 2006, null, "")]
    public void ComicFilenameParser_ParsesStandardConventions(
        string filename, string expectedSeries, string expectedIssue, int? expectedYear, int? expectedVolume, string expectedScanInfo)
    {
        var result = ComicFilenameParser.Parse(filename);

        Assert.Equal(expectedSeries, result.Series);
        Assert.Equal(expectedIssue, result.IssueNumber);
        Assert.Equal(expectedYear, result.Year);
        Assert.Equal(expectedVolume, result.Volume);
        if (!string.IsNullOrEmpty(expectedScanInfo))
        {
            Assert.Equal(expectedScanInfo, result.ScanInformation);
        }
    }

    [Fact]
    public void ComicFilenameParser_HandlesEmptyOrInvalidInput()
    {
        var emptyResult = ComicFilenameParser.Parse("");
        Assert.Empty(emptyResult.Series);
        Assert.Empty(emptyResult.IssueNumber);
        Assert.Null(emptyResult.Year);

        var nullResult = ComicFilenameParser.Parse(null!);
        Assert.Empty(nullResult.Series);
    }
}

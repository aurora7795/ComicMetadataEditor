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
    [InlineData("IM015.cbz", "IM", "15", null, null, "")]
    [InlineData("ASM300 (1988).cbz", "ASM", "300", 1988, null, "")]
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

    [Theory]
    [InlineData("/Volumes/Comics/Western/The Avengers (1963)/048.cbz", "The Avengers", "48", 1963, null)]
    [InlineData("/mnt/storage/Invincible (2003)/#01.cbz", "Invincible", "1", 2003, null)]
    [InlineData("/media/comics/Saga (2012)/Issue 03 (digital).cbz", "Saga", "3", 2012, null)]
    [InlineData("/Comics/Batman (2016)/Vol 1/001.cbz", "Batman", "1", 2016, 1)]
    [InlineData("/Comics/Batman (2016)/v02/012.cbr", "Batman", "12", 2016, 2)]
    [InlineData("/Comics/Batman (2016)/Book 3/025.cbz", "Batman", "25", 2016, 3)]
    [InlineData("/Comics/The Avengers (1963)/The Avengers #048.cbz", "The Avengers", "48", 1963, null)]
    [InlineData("/Comics/The Avengers (1963)/The Avengers 048 (1968) (digital).cbz", "The Avengers", "48", 1968, null)]
    [InlineData("/comics/iron man/IM015.cbz", "Iron Man", "15", null, null)]
    [InlineData("/comics/Iron Man/IM_015.cbz", "Iron Man", "15", null, null)]
    [InlineData("/comics/Iron Man/IM-015.cbz", "Iron Man", "15", null, null)]
    [InlineData("/comics/The Amazing Spider-Man/ASM300.cbz", "The Amazing Spider-Man", "300", null, null)]
    [InlineData("/comics/Uncanny X-Men/UXM_001.cbz", "Uncanny X-Men", "1", null, null)]
    public void ComicFilenameParser_InfersMetadataFromParentDirectoryHierarchy(
        string fullPath, string expectedSeries, string expectedIssue, int? expectedYear, int? expectedVolume)
    {
        var result = ComicFilenameParser.Parse(fullPath);

        Assert.Equal(expectedSeries, result.Series);
        Assert.Equal(expectedIssue, result.IssueNumber);
        Assert.Equal(expectedYear, result.Year);
        Assert.Equal(expectedVolume, result.Volume);
    }

    [Theory]
    [InlineData("/Volumes/General/Comics/Western/01.cbz", "", "1")]
    [InlineData("/home/user/Downloads/01.cbz", "", "1")]
    [InlineData("/mnt/comics/Manga/001.cbz", "", "1")]
    [InlineData("/mnt/comics/2024/01.cbz", "", "1")]
    [InlineData("/mnt/comics/1990s/01.cbz", "", "1")]
    [InlineData("/mnt/comics/Trades/01.cbz", "", "1")]
    [InlineData("/mnt/comics/A/01.cbz", "", "1")]
    public void ComicFilenameParser_IgnoresGenericDirectoryNames(
        string fullPath, string expectedSeries, string expectedIssue)
    {
        var result = ComicFilenameParser.Parse(fullPath);

        Assert.Equal(expectedSeries, result.Series);
        Assert.Equal(expectedIssue, result.IssueNumber);
    }
}

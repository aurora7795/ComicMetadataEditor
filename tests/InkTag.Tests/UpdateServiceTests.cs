using System;
using InkTag.Gui.Services;
using Xunit;

namespace InkTag.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.3.1", 0, 3, 1)]
    [InlineData("v1.0.0-beta.1", 1, 0, 0)]
    [InlineData("0.2.4", 0, 2, 4)]
    public void TryParseVersion_ParsesValidVersionTags(string tag, int expectedMajor, int expectedMinor, int expectedBuild)
    {
        bool success = UpdateService.TryParseVersion(tag, out var ver);

        Assert.True(success);
        Assert.Equal(expectedMajor, ver.Major);
        Assert.Equal(expectedMinor, ver.Minor);
        Assert.Equal(expectedBuild, ver.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid-tag")]
    public void TryParseVersion_HandlesInvalidTagsGracefully(string invalidTag)
    {
        bool success = UpdateService.TryParseVersion(invalidTag, out var ver);

        Assert.False(success);
        Assert.True(ver == null || ver == new Version(0, 0, 0));
    }

    [Fact]
    public void IsInstalledMode_ReturnsFalseForUninstalledEnvironment()
    {
        bool isInstalled = UpdateService.IsInstalledMode(null);
        Assert.False(isInstalled);
    }

    [Fact]
    public void CurrentAppVersion_ReturnsNonNullValidVersion()
    {
        var ver = UpdateService.CurrentAppVersion;
        Assert.NotNull(ver);
        Assert.True(ver.Major >= 0);
    }
}

using System.Text.Json;
using InkTag.Core.Configuration;
using Xunit;

namespace InkTag.Tests;

public class ThemeSettingsTests
{
    [Fact]
    public void AppSettings_DefaultThemeMode_IsSystem()
    {
        var settings = new AppSettings();
        Assert.Equal(AppThemeMode.System, settings.ThemeMode);
    }

    [Theory]
    [InlineData(AppThemeMode.System, "0")]
    [InlineData(AppThemeMode.Dark, "1")]
    [InlineData(AppThemeMode.Light, "2")]
    public void AppSettings_JsonSerialization_PreservesThemeMode(AppThemeMode mode, string expectedEnumValue)
    {
        var original = new AppSettings { ThemeMode = mode };
        string json = JsonSerializer.Serialize(original);
        
        Assert.Contains($"\"ThemeMode\":{expectedEnumValue}", json);

        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(mode, deserialized.ThemeMode);
    }

    [Fact]
    public void AppSettings_DeserializeWithoutThemeMode_DefaultsToSystem()
    {
        string legacyJson = "{\"ComicVineApiKey\":\"test_key\",\"EnableDebugLogging\":true}";
        var deserialized = JsonSerializer.Deserialize<AppSettings>(legacyJson);

        Assert.NotNull(deserialized);
        Assert.Equal(AppThemeMode.System, deserialized.ThemeMode);
    }
}

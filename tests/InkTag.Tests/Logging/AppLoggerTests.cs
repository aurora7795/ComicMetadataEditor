using System;
using System.IO;
using InkTag.Core.Logging;
using Xunit;

namespace InkTag.Tests.Logging;

public class AppLoggerTests
{
    [Fact]
    public void GetDefaultLogFilePath_ReturnsValidCrossPlatformPath()
    {
        string logPath = AppLogger.GetDefaultLogFilePath();
        Assert.False(string.IsNullOrWhiteSpace(logPath));
        Assert.EndsWith("InkTag.log", logPath);
    }

    [Fact]
    public void AppLogger_WritesLogEntriesToCustomPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "InkTagTests_" + Guid.NewGuid().ToString("N"));
        string customLogPath = Path.Combine(tempDir, "test.log");

        try
        {
            AppLogger.Initialize(customLogPath);
            AppLogger.LogInfo("Test Info Message");
            AppLogger.LogWarning("Test Warning Message");
            AppLogger.LogError("Test Error Message", new InvalidOperationException("Test exception detail"));

            Assert.True(File.Exists(customLogPath));
            string logContent = File.ReadAllText(customLogPath);

            Assert.Contains("[INFO] Test Info Message", logContent);
            Assert.Contains("[WARN] Test Warning Message", logContent);
            Assert.Contains("[ERROR] Test Error Message", logContent);
            Assert.Contains("InvalidOperationException: Test exception detail", logContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}

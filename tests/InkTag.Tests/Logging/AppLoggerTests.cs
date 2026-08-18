using System;
using System.IO;
using InkTag.Core.Logging;
using Xunit;

namespace InkTag.Tests.Logging;

[Collection("AppLogger")]
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

    private static readonly object TestSync = new();

    [Fact]
    public void AppLogger_LogDebug_WritesOnlyWhenEnabled()
    {
        lock (TestSync)
        {
            string tempLog = Path.Combine(Path.GetTempPath(), $"inktag_test_log_{Guid.NewGuid()}.log");
            try
            {
                AppLogger.Initialize(tempLog);

                // Debug disabled
                AppLogger.IsDebugEnabled = false;
                AppLogger.LogDebug("SecretDebugDisabledMessage");

                string logContent = File.ReadAllText(tempLog);
                Assert.DoesNotContain("SecretDebugDisabledMessage", logContent);

                // Debug enabled
                AppLogger.IsDebugEnabled = true;
                AppLogger.LogDebug("SecretDebugEnabledMessage");

                logContent = File.ReadAllText(tempLog);
                Assert.Contains("SecretDebugEnabledMessage", logContent);
                Assert.Contains("[DEBUG]", logContent);
            }
            finally
            {
                AppLogger.IsDebugEnabled = false;
                if (File.Exists(tempLog)) File.Delete(tempLog);
            }
        }
    }

    [Fact]
    public void AppSettings_EnableDebugLogging_SynchronizesWithAppLogger()
    {
        lock (TestSync)
        {
            string tempConfig = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid()}.json");
            try
            {
                var service = new InkTag.Core.Configuration.AppSettingsService(tempConfig);
                Assert.False(service.Settings.EnableDebugLogging);
                Assert.False(AppLogger.IsDebugEnabled);

                service.Settings.EnableDebugLogging = true;
                service.SaveSettings(service.Settings);

                Assert.True(AppLogger.IsDebugEnabled);

                // Reload
                var reloadedService = new InkTag.Core.Configuration.AppSettingsService(tempConfig);
                Assert.True(reloadedService.Settings.EnableDebugLogging);
                Assert.True(AppLogger.IsDebugEnabled);
            }
            finally
            {
                AppLogger.IsDebugEnabled = false;
                if (File.Exists(tempConfig)) File.Delete(tempConfig);
            }
        }
    }
}

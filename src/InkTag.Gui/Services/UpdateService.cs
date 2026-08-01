using System;
using System.Threading.Tasks;
using InkTag.Core.Logging;
using Velopack;
using Velopack.Sources;

namespace InkTag.Gui.Services;

public enum UpdateStatusKind
{
    UpdateAvailable,
    UpToDate,
    UninstalledDevBuild,
    Failed
}

public record UpdateCheckResult(
    UpdateStatusKind Kind,
    UpdateInfo? UpdateInfo = null,
    string Message = ""
);

/// <summary>
/// Static service managing application update checking and Velopack deployment.
/// </summary>
public static class UpdateService
{
    private const string GithubRepoUrl = "https://github.com/aurora7795/InkTag";
    private static UpdateInfo? _cachedUpdateInfo;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Checks for available application updates via Velopack and GitHub Releases.
    /// Queries GitHub Releases API across all platforms and logs diagnostic details.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceCheck = false)
    {
        if (!forceCheck && _cachedUpdateInfo != null && (DateTime.UtcNow - _lastCheckTime) < MinCheckInterval)
        {
            AppLogger.LogInfo("Update check requested within check interval; returning cached update info.");
            return new UpdateCheckResult(UpdateStatusKind.UpdateAvailable, _cachedUpdateInfo, $"New update available! ({_cachedUpdateInfo.TargetFullRelease.Version})");
        }

        try
        {
            AppLogger.LogInfo($"Checking for updates via GitHub Release source: {GithubRepoUrl}");
            var source = new GithubSource(GithubRepoUrl, null, false);
            var manager = new UpdateManager(source);

            AppLogger.LogInfo($"Querying Velopack for latest release (IsInstalled: {manager.IsInstalled})...");
            _cachedUpdateInfo = await manager.CheckForUpdatesAsync();
            _lastCheckTime = DateTime.UtcNow;

            if (_cachedUpdateInfo != null)
            {
                string targetVer = _cachedUpdateInfo.TargetFullRelease.Version.ToString();
                AppLogger.LogInfo($"Update check completed successfully: New version found ({targetVer}).");
                return new UpdateCheckResult(UpdateStatusKind.UpdateAvailable, _cachedUpdateInfo, $"New update available! ({targetVer})");
            }
            else
            {
                AppLogger.LogInfo("Update check completed successfully: Application is up to date.");
                return new UpdateCheckResult(UpdateStatusKind.UpToDate, null, "InkTag Desktop is up to date.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Update check failed due to exception.", ex);
            return new UpdateCheckResult(UpdateStatusKind.Failed, null, $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads pending update delta packages and restarts the app with the new version.
    /// </summary>
    public static async Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo, Action<int>? progress = null)
    {
        try
        {
            AppLogger.LogInfo($"Starting update download and installation for version: {updateInfo.TargetFullRelease.Version}");
            var source = new GithubSource(GithubRepoUrl, null, false);
            var manager = new UpdateManager(source);

            await manager.DownloadUpdatesAsync(updateInfo, progress);
            AppLogger.LogInfo("Updates downloaded successfully. Applying update and restarting...");
            manager.ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to download or apply application update.", ex);
            throw;
        }
    }
}

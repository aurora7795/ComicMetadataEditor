using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace InkTag.Gui.Services;

public class UpdateService
{
    private const string GithubRepoUrl = "https://github.com/aurora7795/InkTag";
    private static UpdateInfo? _cachedUpdateInfo;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Checks for available application updates via Velopack and GitHub Releases.
    /// Handles local dev runs and GitHub API rate-limiting gracefully.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false)
    {
        // Rate-limiting safeguard: skip check if checked recently unless forced by user button
        if (!forceCheck && _cachedUpdateInfo != null && (DateTime.UtcNow - _lastCheckTime) < MinCheckInterval)
        {
            return _cachedUpdateInfo;
        }

        try
        {
            var source = new GithubSource(GithubRepoUrl);
            var manager = new UpdateManager(source);

            if (!manager.IsInstalled)
            {
                // Running uninstalled (e.g. dotnet run or local debug build)
                return null;
            }

            _cachedUpdateInfo = await manager.CheckForUpdatesAsync();
            _lastCheckTime = DateTime.UtcNow;
            return _cachedUpdateInfo;
        }
        catch (Exception ex)
        {
            // Silently swallow network / API rate limit exceptions in dev/offline mode
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads pending update delta packages and restarts the app with the new version.
    /// </summary>
    public async Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo, Action<int>? progress = null)
    {
        try
        {
            var source = new GithubSource(GithubRepoUrl);
            var manager = new UpdateManager(source);

            if (!manager.IsInstalled) return;

            await manager.DownloadUpdatesAsync(updateInfo, progress);
            manager.ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply update: {ex.Message}");
            throw;
        }
    }
}

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using InkTag.Core.Logging;
using Velopack;
using Velopack.Sources;

using System.Reflection;

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
    string Message = "",
    string? ReleaseUrl = null
);

/// <summary>
/// Static service managing application update checking, Velopack deployment, and GitHub Releases fallback for portable environments.
/// </summary>
public static class UpdateService
{
    private const string GithubRepoUrl = "https://github.com/aurora7795/InkTag";
    private const string GithubApiLatestReleaseUrl = "https://api.github.com/repos/aurora7795/InkTag/releases/latest";
    private static readonly object SyncLock = new();
    private static UpdateInfo? _cachedUpdateInfo;
    private static UpdateCheckResult? _cachedPortableResult;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromMinutes(15);
    private static Version? _resolvedAppVersion;

    public static Version CurrentAppVersion
    {
        get
        {
            if (_resolvedAppVersion != null) return _resolvedAppVersion;
            lock (SyncLock)
            {
                return _resolvedAppVersion ??= GetCurrentAppVersion();
            }
        }
    }

    private static Version GetCurrentAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        if (ver != null && ver != new Version(0, 0, 0, 0))
        {
            int build = ver.Build < 0 ? 0 : ver.Build;
            return new Version(ver.Major, ver.Minor, build);
        }

        var infoVerAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoVerAttr != null && TryParseVersion(infoVerAttr.InformationalVersion, out var parsedVer))
        {
            return parsedVer;
        }

        return new Version(0, 10, 1);
    }

    /// <summary>
    /// Checks if the application is running in installed mode (Velopack installed, Linux AppImage, or macOS .app bundle).
    /// </summary>
    public static bool IsInstalledMode(UpdateManager? manager = null)
    {
        try
        {
            if (manager != null && manager.IsInstalled) return true;
        }
        catch { }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sqPath = System.IO.Path.Combine(baseDir, "sq.version");
        if (System.IO.File.Exists(sqPath))
        {
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            string resourcesSq = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "Resources", "sq.version"));
            if (System.IO.File.Exists(resourcesSq))
            {
                return true;
            }
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE")))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks for available application updates. Uses Velopack when installed, or queries GitHub Releases API directly in portable mode.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceCheck = false)
    {
        if (!forceCheck)
        {
            lock (SyncLock)
            {
                if ((DateTime.UtcNow - _lastCheckTime) < MinCheckInterval)
                {
                    if (_cachedUpdateInfo != null)
                    {
                        return new UpdateCheckResult(UpdateStatusKind.UpdateAvailable, _cachedUpdateInfo, $"New update available! ({_cachedUpdateInfo.TargetFullRelease.Version})");
                    }
                    if (_cachedPortableResult != null)
                    {
                        return _cachedPortableResult;
                    }
                }
            }
        }

        try
        {
            AppLogger.LogInfo($"Checking for updates (Current app version: {CurrentAppVersion})");

            UpdateManager? manager = null;
            try
            {
                var source = new GithubSource(GithubRepoUrl, null, false);
                manager = new UpdateManager(source);
            }
            catch (Exception ex)
            {
                AppLogger.LogInfo($"Velopack initialization notice ({ex.Message}). Operating in portable mode.");
            }

            if (manager != null && manager.IsInstalled)
            {
                try
                {
                    AppLogger.LogInfo("Application is running in installed / .app mode. Querying Velopack manager...");
                    var updateInfo = await manager.CheckForUpdatesAsync();
                    lock (SyncLock)
                    {
                        _cachedUpdateInfo = updateInfo;
                        _lastCheckTime = DateTime.UtcNow;
                    }

                    if (updateInfo != null)
                    {
                        string targetVer = updateInfo.TargetFullRelease.Version.ToString();
                        AppLogger.LogInfo($"Velopack update check completed successfully: New version found ({targetVer}).");
                        return new UpdateCheckResult(UpdateStatusKind.UpdateAvailable, updateInfo, $"New update available! ({targetVer})");
                    }
                    else
                    {
                        AppLogger.LogInfo("Velopack update check completed: Application is up to date.");
                        return new UpdateCheckResult(UpdateStatusKind.UpToDate, null, "InkTag Desktop is up to date.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning($"Velopack reported error ({ex.Message}). Falling back to GitHub Releases API...");
                    return await CheckGitHubReleasesFallbackAsync();
                }
            }
            else
            {
                AppLogger.LogInfo("Application is running in portable/uninstalled mode. Fallback: Querying GitHub Releases API directly...");
                return await CheckGitHubReleasesFallbackAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Update check failed due to exception.", ex);
            return new UpdateCheckResult(UpdateStatusKind.Failed, null, $"Update check failed: {ex.Message}");
        }
    }

    private static async Task<UpdateCheckResult> CheckGitHubReleasesFallbackAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("InkTagDesktop", CurrentAppVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync(GithubApiLatestReleaseUrl);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.LogWarning($"GitHub Releases API returned status code: {response.StatusCode}");
            return new UpdateCheckResult(UpdateStatusKind.UpToDate, null, "InkTag Desktop is up to date.");
        }

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? GithubRepoUrl : GithubRepoUrl;

        lock (SyncLock)
        {
            _lastCheckTime = DateTime.UtcNow;

            if (TryParseVersion(tagName, out var latestVersion) && latestVersion > CurrentAppVersion)
            {
                AppLogger.LogInfo($"GitHub API fallback found newer release: {tagName} (Current: {CurrentAppVersion})");
                _cachedPortableResult = new UpdateCheckResult(UpdateStatusKind.UpdateAvailable, null, $"New update available! ({tagName})", htmlUrl);
                return _cachedPortableResult;
            }

            AppLogger.LogInfo($"GitHub API fallback check complete: InkTag Desktop is up to date. Latest release on GitHub: {tagName}");
            _cachedPortableResult = new UpdateCheckResult(UpdateStatusKind.UpToDate, null, "InkTag Desktop is up to date.", htmlUrl);
            return _cachedPortableResult;
        }
    }

    public static bool TryParseVersion(string versionTag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionTag)) return false;

        string cleanTag = versionTag.TrimStart('v', 'V').Trim();
        int dashIdx = cleanTag.IndexOf('-');
        if (dashIdx > 0)
        {
            cleanTag = cleanTag.Substring(0, dashIdx);
        }

        return Version.TryParse(cleanTag, out version!);
    }

    /// <summary>
    /// Downloads pending updates in Velopack mode or opens GitHub Release URL in system browser for portable mode.
    /// </summary>
    public static async Task DownloadAndApplyUpdateAsync(UpdateInfo? updateInfo, string? releaseUrl = null, Action<int>? progress = null)
    {
        try
        {
            UpdateManager? manager = null;
            try
            {
                var source = new GithubSource(GithubRepoUrl, null, false);
                manager = new UpdateManager(source);
            }
            catch { }

            if (updateInfo != null && manager != null && IsInstalledMode(manager))
            {
                try
                {
                    AppLogger.LogInfo($"Starting Velopack update download for version: {updateInfo.TargetFullRelease.Version}");
                    await manager.DownloadUpdatesAsync(updateInfo, progress);
                    AppLogger.LogInfo("Updates downloaded successfully. Applying update and restarting...");
                    manager.ApplyUpdatesAndRestart(updateInfo);
                    return;
                }
                catch (Exception velopackEx)
                {
                    AppLogger.LogWarning($"Velopack in-place update failed ({velopackEx.Message}). Falling back to browser release URL...");
                }
            }

            string url = releaseUrl ?? $"{GithubRepoUrl}/releases/latest";
            AppLogger.LogInfo($"Opening GitHub Release page in browser: {url}");
            OpenUrlInBrowser(url);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to process application update.", ex);
            throw;
        }
    }

    public static void OpenUrlInBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"\"{url}\"", UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = $"\"{url}\"", UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to open URL in browser: {url}", ex);
        }
    }
}

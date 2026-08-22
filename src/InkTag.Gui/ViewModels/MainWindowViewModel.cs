using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using InkTag.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InkTag.Gui.Services;
using InkTag.Core.Logging;

namespace InkTag.Gui.ViewModels;

public enum ComicFilterMode
{
    All,
    Untagged,
    Modified
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ComicScannerService _scannerService = new();
    private readonly ArchiveCoverService _coverService = new();
    private readonly List<ComicItemViewModel> _selectedComics = new();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _saveCts;
    private Velopack.UpdateInfo? _pendingUpdateInfo;
    private string? _pendingReleaseUrl;

    [ObservableProperty]
    private string _appVersionText = $"InkTag Desktop v{UpdateService.CurrentAppVersion.ToString(3)}";

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _selectedDirectory = string.Empty;

    public bool HasSelectedDirectory => !string.IsNullOrWhiteSpace(SelectedDirectory);

    partial void OnSelectedDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedDirectory));
    }

    [ObservableProperty]
    private bool _isRecursive = true;

    [ObservableProperty]
    private bool _isInspectorVisible = true;

    [ObservableProperty]
    private InkTag.Core.Configuration.AppThemeMode _currentThemeMode = InkTag.Core.Configuration.AppThemeMode.System;

    public bool IsThemeSystem => CurrentThemeMode == InkTag.Core.Configuration.AppThemeMode.System;
    public bool IsThemeDark => CurrentThemeMode == InkTag.Core.Configuration.AppThemeMode.Dark;
    public bool IsThemeLight => CurrentThemeMode == InkTag.Core.Configuration.AppThemeMode.Light;

    public void SetTheme(InkTag.Core.Configuration.AppThemeMode mode)
    {
        CurrentThemeMode = mode;
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));

        App.ApplyTheme(mode);

        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        var settings = settingsService.Settings;
        if (settings.ThemeMode != mode)
        {
            settings.ThemeMode = mode;
            settingsService.SaveSettings(settings);
        }
    }

    public void RefreshThemeFromSettings()
    {
        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        CurrentThemeMode = settingsService.Settings.ThemeMode;
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
    }

    [ObservableProperty]
    private ObservableCollection<ComicItemViewModel> _comics = new();

    public ObservableCollection<ComicItemViewModel> DisplayedComics { get; } = new();

    [ObservableProperty]
    private ComicFilterMode _filterMode = ComicFilterMode.All;

    [ObservableProperty]
    private string _filterSearchText = string.Empty;

    [ObservableProperty]
    private string _filterStatusText = "Ready";

    [ObservableProperty]
    private int _untaggedCount;

    [ObservableProperty]
    private int _modifiedCount;

    public string AllFilterLabel => $"All ({Comics.Count})";
    public string UntaggedFilterLabel => $"Untagged ({UntaggedCount})";
    public string ModifiedFilterLabel => $"Modified ({ModifiedCount})";

    public bool IsFilterAll
    {
        get => FilterMode == ComicFilterMode.All;
        set { if (value) FilterMode = ComicFilterMode.All; }
    }

    public bool IsFilterUntagged
    {
        get => FilterMode == ComicFilterMode.Untagged;
        set { if (value) FilterMode = ComicFilterMode.Untagged; }
    }

    public bool IsFilterModified
    {
        get => FilterMode == ComicFilterMode.Modified;
        set { if (value) FilterMode = ComicFilterMode.Modified; }
    }

    partial void OnFilterModeChanged(ComicFilterMode value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterUntagged));
        OnPropertyChanged(nameof(IsFilterModified));
        ApplyFilter();
    }

    partial void OnFilterSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private ComicItemViewModel? _activeComic;
    public ComicItemViewModel? ActiveComic
    {
        get => _activeComic;
        set
        {
            if (SetProperty(ref _activeComic, value))
            {
                TriggerActiveComicCoverLoad();
            }
        }
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "Ready";

    // Dynamic Bulk Edit Rules
    public ObservableCollection<BulkEditRuleViewModel> BulkEditRules { get; } = new();

    // Legacy Bulk Edit Fields (maintained for compatibility)
    [ObservableProperty] private string? _bulkSeries;
    [ObservableProperty] private bool _bulkSeriesEnabled;
    [ObservableProperty] private string? _bulkPublisher;
    [ObservableProperty] private bool _bulkPublisherEnabled;
    [ObservableProperty] private int? _bulkYear;
    [ObservableProperty] private bool _bulkYearEnabled;
    [ObservableProperty] private string? _bulkGenre;
    [ObservableProperty] private bool _bulkGenreEnabled;
    [ObservableProperty] private bool _bulkManga;
    [ObservableProperty] private bool _bulkMangaEnabled;

    // Find & Replace Fields
    [ObservableProperty] private string _findText = string.Empty;
    [ObservableProperty] private string _replaceText = string.Empty;
    [ObservableProperty] private string _selectedReplaceColumn = "Title";

    public List<string> ReplaceColumns { get; } = new()
    {
        "Title", "Series", "Publisher", "Writer", "Genre", "Tags", "LanguageISO"
    };

    public bool CanSave => Comics.Any(c => c.IsDirty && !c.HasReadError) && !Comics.Any(c => c.HasErrors);
    public bool HasDirtyItems => Comics.Any(c => c.IsDirty);

    // Save report list
    public List<(string Path, Exception Exception)> SaveFailures { get; } = new();

    // Event raised when save completes with errors, to open error window
    public event Action? SaveFinishedWithErrors;

    public MainWindowViewModel()
    {
        Comics.CollectionChanged += OnComicsCollectionChanged;
        BulkEditRules.Add(new BulkEditRuleViewModel());
        RefreshThemeFromSettings();
    }

    public void UpdateCounts()
    {
        UntaggedCount = Comics.Count(c => c.IsUntagged);
        ModifiedCount = Comics.Count(c => c.IsDirty);
        OnPropertyChanged(nameof(AllFilterLabel));
        OnPropertyChanged(nameof(UntaggedFilterLabel));
        OnPropertyChanged(nameof(ModifiedFilterLabel));
        UpdateFilterStatus();
    }

    public void ApplyFilter()
    {
        var search = FilterSearchText?.Trim();
        var filtered = Comics.Where(comic =>
        {
            if (FilterMode == ComicFilterMode.Untagged && !comic.IsUntagged && comic != ActiveComic)
            {
                return false;
            }
            if (FilterMode == ComicFilterMode.Modified && !comic.IsDirty && comic != ActiveComic)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(search))
            {
                bool matches =
                    (!string.IsNullOrEmpty(comic.FileName) && comic.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(comic.Title) && comic.Title.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(comic.Series) && comic.Series.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(comic.Writer) && comic.Writer.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(comic.Publisher) && comic.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase));

                if (!matches) return false;
            }

            return true;
        }).ToList();

        if (!DisplayedComics.SequenceEqual(filtered))
        {
            DisplayedComics.Clear();
            foreach (var item in filtered)
            {
                DisplayedComics.Add(item);
            }
        }

        UpdateFilterStatus();
    }

    private void UpdateFilterStatus()
    {
        if (Comics.Count == 0)
        {
            FilterStatusText = "No comics loaded";
            return;
        }

        if (FilterMode == ComicFilterMode.Untagged)
        {
            FilterStatusText = $"Showing {DisplayedComics.Count} untagged of {Comics.Count} comics";
        }
        else if (FilterMode == ComicFilterMode.Modified)
        {
            FilterStatusText = $"Showing {DisplayedComics.Count} modified of {Comics.Count} comics";
        }
        else
        {
            FilterStatusText = UntaggedCount > 0
                ? $"Showing {DisplayedComics.Count} of {Comics.Count} comics ({UntaggedCount} untagged)"
                : $"Showing {DisplayedComics.Count} of {Comics.Count} comics";
        }
    }

    private void OnComicsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ComicItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnComicItemPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (ComicItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnComicItemPropertyChanged;
            }
        }
        UpdateCounts();
        ApplyFilter();
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnComicItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComicItemViewModel.IsDirty) || 
            e.PropertyName == nameof(ComicItemViewModel.HasErrors) ||
            e.PropertyName == nameof(ComicItemViewModel.IsUntagged) ||
            e.PropertyName == nameof(ComicItemViewModel.Title) ||
            e.PropertyName == nameof(ComicItemViewModel.Series))
        {
            UpdateCounts();
            OnPropertyChanged(nameof(CanSave));
            if (FilterMode != ComicFilterMode.All)
            {
                ApplyFilter();
            }
        }
    }

    private async void TriggerActiveComicCoverLoad()
    {
        if (ActiveComic == null) return;
        
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        
        try
        {
            await ActiveComic.LoadCoverAsync(_coverService, _scanCts.Token);
        }
        catch (OperationCanceledException) { }
    }

    public void UpdateSelection(System.Collections.IList selectedItems)
    {
        _selectedComics.Clear();
        foreach (var item in selectedItems)
        {
            if (item is ComicItemViewModel vm)
            {
                _selectedComics.Add(vm);
            }
        }
    }

    [RelayCommand]
    private void AddBulkRule()
    {
        BulkEditRules.Add(new BulkEditRuleViewModel());
    }

    [RelayCommand]
    private void RemoveBulkRule(BulkEditRuleViewModel? rule)
    {
        if (rule != null && BulkEditRules.Count > 1)
        {
            BulkEditRules.Remove(rule);
        }
    }

    [ObservableProperty]
    private bool _isSlowShareWarningVisible;

    [ObservableProperty]
    private string _slowShareWarningMessage = string.Empty;

    [RelayCommand]
    private void ClearBulkRules()
    {
        BulkEditRules.Clear();
        BulkEditRules.Add(new BulkEditRuleViewModel());
    }

    [RelayCommand]
    private void CancelScan()
    {
        if (IsLoading && _scanCts != null && !_scanCts.IsCancellationRequested)
        {
            _scanCts.Cancel();
            ProgressText = "Cancelling scan...";
        }
    }

    [RelayCommand]
    private async Task LoadDirectoryAsync()
    {
        if (string.IsNullOrEmpty(SelectedDirectory) || !Directory.Exists(SelectedDirectory)) return;

        IsLoading = true;
        IsSlowShareWarningVisible = false;
        SlowShareWarningMessage = string.Empty;
        ProgressText = "Discovering comic files...";
        ProgressValue = 0;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        bool unseekableDetected = false;

        var progress = new Progress<ScanProgressReport>(report =>
        {
            if (report.IsUnseekableStream)
            {
                unseekableDetected = true;
                IsSlowShareWarningVisible = true;
                SlowShareWarningMessage = "Slow network share detected: Virtual FTP/FUSE mounts do not support archive seeking and require streaming entire files. For instant loading, mount your share via SMB/CIFS.";
            }

            if (report.Total > 0)
            {
                ProgressValue = (report.Processed * 100) / report.Total;
                double mb = (report.CurrentFileSizeBytes ?? 0) / (1024.0 * 1024.0);
                string sizeStr = mb > 0 ? $" ({mb:F0} MB)" : "";
                string unseekablePrefix = report.IsUnseekableStream ? "⚠️ Slow Virtual Share: " : "";
                string activeFileStr = !string.IsNullOrEmpty(report.CurrentFileName)
                    ? $" • {unseekablePrefix}Streaming '{report.CurrentFileName}'{sizeStr}..."
                    : "";
                ProgressText = $"Scanning: {report.Processed}/{report.Total} files ({ProgressValue}%){activeFileStr}";
            }
            else
            {
                ProgressText = "Discovering comic files...";
            }
        });

        try
        {
            Comics.Clear();
            ActiveComic = null;

            var items = await _scannerService.ScanDirectoryAsync(SelectedDirectory, IsRecursive, ct, progress);
            foreach (var item in items)
            {
                Comics.Add(item);
            }

            string tip = unseekableDetected ? " (Slow virtual share detected • Tip: Mount via SMB/CIFS for instant loading)" : "";
            if (ct.IsCancellationRequested)
            {
                ProgressText = $"Scan cancelled ({Comics.Count} files loaded{tip}).";
            }
            else
            {
                ProgressText = $"Loaded {Comics.Count} comics successfully{tip}.";
            }
        }
        catch (OperationCanceledException)
        {
            string tip = unseekableDetected ? " (Slow virtual share detected • Tip: Mount via SMB/CIFS for instant loading)" : "";
            ProgressText = $"Scan cancelled ({Comics.Count} files loaded{tip}).";
        }
        catch (Exception ex)
        {
            ProgressText = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            ProgressValue = 100;
        }
    }

    [RelayCommand]
    private void BulkApply()
    {
        var targets = (_selectedComics.Any() ? _selectedComics : DisplayedComics.ToList())
            .Where(c => !c.HasReadError).ToList();
        if (!targets.Any()) return;

        foreach (var item in targets)
        {
            if (BulkSeriesEnabled) item.Series = BulkSeries;
            if (BulkPublisherEnabled) item.Publisher = BulkPublisher;
            if (BulkYearEnabled) item.Year = BulkYear;
            if (BulkGenreEnabled) item.Genre = BulkGenre;
            if (BulkMangaEnabled) item.Manga = BulkManga;
        }
        
        ProgressText = $"Applied bulk modifications to {targets.Count} items.";
    }

    [RelayCommand]
    private void FindReplace()
    {
        if (string.IsNullOrEmpty(FindText)) return;
        var targets = (_selectedComics.Any() ? _selectedComics : DisplayedComics.ToList())
            .Where(c => !c.HasReadError).ToList();
        if (!targets.Any()) return;

        int modifiedCount = 0;
        foreach (var item in targets)
        {
            bool modified = false;
            switch (SelectedReplaceColumn)
            {
                case "Title":
                    if (item.Title != null && item.Title.Contains(FindText)) { item.Title = item.Title.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "Series":
                    if (item.Series != null && item.Series.Contains(FindText)) { item.Series = item.Series.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "Publisher":
                    if (item.Publisher != null && item.Publisher.Contains(FindText)) { item.Publisher = item.Publisher.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "Writer":
                    if (item.Writer != null && item.Writer.Contains(FindText)) { item.Writer = item.Writer.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "Genre":
                    if (item.Genre != null && item.Genre.Contains(FindText)) { item.Genre = item.Genre.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "Tags":
                    if (item.Tags != null && item.Tags.Contains(FindText)) { item.Tags = item.Tags.Replace(FindText, ReplaceText); modified = true; }
                    break;
                case "LanguageISO":
                    if (item.LanguageISO != null && item.LanguageISO.Contains(FindText)) { item.LanguageISO = item.LanguageISO.Replace(FindText, ReplaceText); modified = true; }
                    break;
            }
            if (modified) modifiedCount++;
        }

        ProgressText = $"Replaced text inside {modifiedCount} items.";
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        var dirtyItems = Comics.Where(c => c.IsDirty && !c.HasReadError && !c.HasErrors).ToList();
        if (!dirtyItems.Any())
        {
            var itemsWithErrors = Comics.Where(c => c.IsDirty && c.HasErrors).ToList();
            if (itemsWithErrors.Any())
            {
                ProgressText = $"Cannot save: {itemsWithErrors.Count} comic(s) have validation errors. Please correct highlighted fields.";
            }
            else
            {
                ProgressText = "No unsaved changes detected.";
            }
            return;
        }

        IsSaving = true;
        ProgressValue = 0;
        SaveFailures.Clear();

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();

        var editor = new MetadataEditor();
        int total = dirtyItems.Count;
        int completed = 0;

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in dirtyItems)
                {
                    _saveCts.Token.ThrowIfCancellationRequested();
                    completed++;
                    
                    var currentCompleted = completed;
                    var currentPath = item.FileName;
                    string originalPath = item.FilePath;
                    bool isCbr = Path.GetExtension(originalPath).Equals(".cbr", StringComparison.OrdinalIgnoreCase);

                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressValue = (double)currentCompleted / total * 100;
                        ProgressText = $"Saving ({currentCompleted}/{total}): {currentPath}";
                    });

                    try
                    {
                        editor.EditMetadata(originalPath, comicInfo =>
                        {
                            item.ApplyChangesToModel(comicInfo);
                        });

                        string targetPath = isCbr ? Path.ChangeExtension(originalPath, ".cbz") : originalPath;

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (isCbr && File.Exists(targetPath))
                            {
                                string oldName = item.FileName;
                                item.UpdateFilePath(targetPath);
                                ProgressText = $"Converted {oldName} → {item.FileName}";
                            }
                            item.HasEmbeddedXml = true;
                            item.IsDirty = false;
                        });
                    }
                    catch (Exception ex)
                    {
                        Core.Logging.AppLogger.LogError($"Failed to save metadata to '{originalPath}': {ex.Message}", ex);
                        lock (SaveFailures)
                        {
                            SaveFailures.Add((item.FilePath, ex));
                        }
                    }
                }
            }, _saveCts.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Saving operation cancelled.";
        }
        finally
        {
            IsSaving = false;
            ProgressValue = 100;

            if (SaveFailures.Any())
            {
                ProgressText = $"Completed with {SaveFailures.Count} failures.";
                SaveFinishedWithErrors?.Invoke();
            }
            else
            {
                ProgressText = "All modifications saved successfully.";

                // Check for Komga auto-sync on save
                var settingsService = new InkTag.Core.Configuration.AppSettingsService();
                if (settingsService.Settings.KomgaAutoSyncOnSave && dirtyItems.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        var syncList = dirtyItems.Select(c => (c.FilePath, c.ToModel())).ToList();
                        var syncService = new InkTag.Core.Komga.KomgaSyncService(settingsService);
                        if (syncService.IsConfigured)
                        {
                            var report = await syncService.SyncMultipleComicsAsync(syncList);
                            if (report.IsSuccess && (report.BooksAnalyzed > 0 || report.SeriesAnalyzed > 0))
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    ProgressText = $"Saved & auto-synced with Komga (Refreshed {report.BooksAnalyzed} books).";
                                });
                            }
                        }
                    });
                }
            }

            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    public async Task SyncToKomgaAsync()
    {
        var items = (_selectedComics.Any() ? _selectedComics : DisplayedComics.ToList());
        if (!items.Any())
        {
            ProgressText = "No comics selected or available to sync with Komga.";
            return;
        }

        var settingsService = new InkTag.Core.Configuration.AppSettingsService();
        var syncService = new InkTag.Core.Komga.KomgaSyncService(settingsService);
        if (!syncService.IsConfigured)
        {
            ProgressText = "Komga server is not configured. Go to Settings > Komga Integration to connect.";
            return;
        }

        IsSaving = true;
        ProgressValue = 0;
        ProgressText = $"Syncing {items.Count} comic(s) with Komga server...";

        var syncList = items.Select(c => (c.FilePath, c.ToModel())).ToList();
        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressValue = p * 100;
            });
        });

        try
        {
            var report = await Task.Run(() => syncService.SyncMultipleComicsAsync(syncList, progress));
            if (report.IsSuccess)
            {
                ProgressText = $"Komga Sync: Refreshed {report.BooksAnalyzed} books, {report.SeriesAnalyzed} series, and {report.CollectionsSynced} collections.";
            }
            else
            {
                ProgressText = $"Komga Sync completed with {report.Failures.Count} warnings/failures.";
            }
        }
        catch (Exception ex)
        {
            ProgressText = $"Komga Sync failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            ProgressValue = 100;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("File Path,File Name,Title,Series,Number,Volume,Publisher,Year,Genre,Tags,Writer,LanguageISO,Manga");

            foreach (var item in Comics)
            {
                var line = $"{EscapeCsv(item.FilePath)},{EscapeCsv(item.FileName)},{EscapeCsv(item.Title)},{EscapeCsv(item.Series)},{EscapeCsv(item.Number)},{item.Volume},{EscapeCsv(item.Publisher)},{item.Year},{EscapeCsv(item.Genre)},{EscapeCsv(item.Tags)},{EscapeCsv(item.Writer)},{EscapeCsv(item.LanguageISO)},{(item.Manga ? "Yes" : "No")}";
                csv.AppendLine(line);
            }

            await File.WriteAllTextAsync(savePath, csv.ToString(), Encoding.UTF8);
            ProgressText = $"Exported details to CSV successfully at: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            ProgressText = $"CSV Export failed: {ex.Message}";
        }
    }

    private static string EscapeCsv(string? val)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        if (val.Contains(",") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatusText = "Checking for updates...";
            var result = await UpdateService.CheckForUpdatesAsync(forceCheck: true);
            _pendingUpdateInfo = result.UpdateInfo;
            _pendingReleaseUrl = result.ReleaseUrl;
            IsUpdateAvailable = result.Kind == UpdateStatusKind.UpdateAvailable;
            UpdateStatusText = result.Message;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error in CheckForUpdatesAsync command handler.", ex);
            UpdateStatusText = $"Update check failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (_pendingUpdateInfo == null && string.IsNullOrEmpty(_pendingReleaseUrl)) return;
        try
        {
            if (_pendingUpdateInfo != null)
            {
                UpdateStatusText = "Downloading update...";
            }
            else
            {
                UpdateStatusText = "Opening GitHub Releases page...";
            }

            await UpdateService.DownloadAndApplyUpdateAsync(_pendingUpdateInfo, _pendingReleaseUrl, progress =>
            {
                Dispatcher.UIThread.Post(() => UpdateStatusText = $"Downloading update: {progress}%");
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Error in ApplyUpdateAsync command handler.", ex);
            UpdateStatusText = $"Failed to apply update: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenLogs()
    {
        AppLogger.LogInfo("User requested opening log directory from UI.");
        AppLogger.OpenLogFolder();
    }

    [RelayCommand]
    public async Task RefreshGridAsync()
    {
        if (!string.IsNullOrEmpty(SelectedDirectory) && System.IO.Directory.Exists(SelectedDirectory))
        {
            await LoadDirectoryCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    public void ToggleInspector()
    {
        IsInspectorVisible = !IsInspectorVisible;
    }
}

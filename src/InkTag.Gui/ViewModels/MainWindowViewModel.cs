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

    [ObservableProperty]
    private bool _isRecursive;

    [ObservableProperty]
    private ObservableCollection<ComicItemViewModel> _comics = new();

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

    // Bulk Edit Fields
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

    public bool CanSave => Comics.Any(c => c.IsDirty) && !Comics.Any(c => c.HasErrors);
    public bool HasDirtyItems => Comics.Any(c => c.IsDirty);

    // Save report list
    public List<(string Path, Exception Exception)> SaveFailures { get; } = new();

    // Event raised when save completes with errors, to open error window
    public event Action? SaveFinishedWithErrors;

    public MainWindowViewModel()
    {
        Comics.CollectionChanged += OnComicsCollectionChanged;
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
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnComicItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComicItemViewModel.IsDirty) || 
            e.PropertyName == nameof(ComicItemViewModel.HasErrors))
        {
            OnPropertyChanged(nameof(CanSave));
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
    private async Task LoadDirectoryAsync()
    {
        if (string.IsNullOrEmpty(SelectedDirectory) || !Directory.Exists(SelectedDirectory)) return;

        IsLoading = true;
        ProgressText = "Scanning folder...";
        ProgressValue = 0;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        try
        {
            Comics.Clear();
            ActiveComic = null;

            var items = await _scannerService.ScanDirectoryAsync(SelectedDirectory, IsRecursive, _scanCts.Token);
            foreach (var item in items)
            {
                Comics.Add(item);
            }

            ProgressText = $"Loaded {Comics.Count} comics successfully.";
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
        var targets = _selectedComics.Any() ? _selectedComics : Comics.ToList();
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
        var targets = _selectedComics.Any() ? _selectedComics : Comics.ToList();
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
        var dirtyItems = Comics.Where(c => c.IsDirty).ToList();
        if (!dirtyItems.Any()) return;

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
                    Dispatcher.UIThread.Post(() =>
                    {
                        ProgressValue = (double)currentCompleted / total * 100;
                        ProgressText = $"Saving ({currentCompleted}/{total}): {currentPath}";
                    });

                    try
                    {
                        editor.EditMetadata(item.FilePath, comicInfo =>
                        {
                            item.ApplyChangesToModel(comicInfo);
                        });

                        Dispatcher.UIThread.Post(() =>
                        {
                            item.IsDirty = false;
                        });
                    }
                    catch (Exception ex)
                    {
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
            }

            OnPropertyChanged(nameof(CanSave));
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
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.ViewModels;

public class BulkScrapeQueueViewModel : ObservableObject
{
    private readonly BulkScrapeQueueService _queueService;
    private readonly AppSettingsService _settingsService;
    private CancellationTokenSource? _cts;

    public ObservableCollection<BulkScrapeItemViewModel> Items { get; } = new();

    private BulkScrapeItemViewModel? _selectedItem;
    public BulkScrapeItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanChangeSelection));
            }
        }
    }

    public bool CanStart => !IsRunning && Items.Any(i => i.IsSelected);
    public bool CanApply => !IsRunning && Items.Any(i => i.IsSelected && i.MatchedCandidate != null);
    public bool CanChangeSelection => !IsRunning;

    private double _progressPercentage;
    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    private string _progressStatus = "Ready to start";
    public string ProgressStatus
    {
        get => _progressStatus;
        set => SetProperty(ref _progressStatus, value);
    }

    private int _selectedMergeModeIndex;
    public int SelectedMergeModeIndex
    {
        get => _selectedMergeModeIndex;
        set => SetProperty(ref _selectedMergeModeIndex, value);
    }

    public ScrapeMergeMode EffectiveMergeMode => _selectedMergeModeIndex == 1
        ? ScrapeMergeMode.OverwriteAll
        : ScrapeMergeMode.FillMissingOnly;

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _matchedCount;
    public int MatchedCount
    {
        get => _matchedCount;
        set => SetProperty(ref _matchedCount, value);
    }

    private int _reviewNeededCount;
    public int ReviewNeededCount
    {
        get => _reviewNeededCount;
        set => SetProperty(ref _reviewNeededCount, value);
    }

    private int _unmatchedCount;
    public int UnmatchedCount
    {
        get => _unmatchedCount;
        set => SetProperty(ref _unmatchedCount, value);
    }

    public string[] RenameTemplates { get; } = new[]
    {
        "{Series} #{Number:3} ({Year})",
        "{Series} #{Number:3} - {Title} ({Year})",
        "{Series} {Number:3} ({Year})",
        "{Publisher} - {Series} v{Volume} #{Number:3} ({Year})"
    };

    private int _selectedRenameTemplateIndex;
    public int SelectedRenameTemplateIndex
    {
        get => _selectedRenameTemplateIndex;
        set
        {
            if (SetProperty(ref _selectedRenameTemplateIndex, value))
            {
                if (value >= 0 && value < RenameTemplates.Length)
                {
                    _settingsService.Settings.BulkScrapeRenameTemplate = RenameTemplates[value];
                    _settingsService.SaveSettings();
                }
            }
        }
    }

    private bool _alsoRenameFiles;
    public bool AlsoRenameFiles
    {
        get => _alsoRenameFiles;
        set
        {
            if (SetProperty(ref _alsoRenameFiles, value))
            {
                _settingsService.Settings.BulkScrapeAutoRenameFiles = value;
                _settingsService.SaveSettings();
            }
        }
    }

    private bool _selectAll = true;
    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            if (SetProperty(ref _selectAll, value))
            {
                foreach (var item in Items)
                {
                    item.IsSelected = value;
                }
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public BulkScrapeQueueViewModel(
        IEnumerable<string> filePaths,
        BulkScrapeQueueService? queueService = null,
        AppSettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new AppSettingsService();
        _queueService = queueService ?? new BulkScrapeQueueService(null, null, _settingsService);

        _selectedMergeModeIndex = _settingsService.Settings.DefaultMergeMode == ScrapeMergeMode.OverwriteAll ? 1 : 0;
        _alsoRenameFiles = _settingsService.Settings.BulkScrapeAutoRenameFiles;

        string currentTpl = _settingsService.Settings.BulkScrapeRenameTemplate;
        int tplIndex = Array.IndexOf(RenameTemplates, currentTpl);
        _selectedRenameTemplateIndex = tplIndex >= 0 ? tplIndex : 0;

        var queuedItems = _queueService.CreateQueue(filePaths);
        foreach (var item in queuedItems)
        {
            var vm = new BulkScrapeItemViewModel(item);
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(BulkScrapeItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanApply));
                }
            };
            Items.Add(vm);
        }

        TotalCount = Items.Count;
        UpdateCounts();
    }

    public async Task StartQueueAsync()
    {
        if (IsRunning) return;

        var selectedItems = Items.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            ProgressStatus = "No items selected to scrape. Please check the items you want to scrape.";
            return;
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        ProgressPercentage = 0;
        ProgressStatus = $"Starting bulk scrape for {selectedItems.Count} selected items...";

        foreach (var itemVm in selectedItems)
        {
            itemVm.Status = BulkScrapeItemStatus.Queued;
            itemVm.StatusMessage = "Queued";
        }
        foreach (var itemVm in Items.Where(i => !i.IsSelected))
        {
            if (itemVm.Status == BulkScrapeItemStatus.Ready || itemVm.Status == BulkScrapeItemStatus.Queued)
            {
                itemVm.Status = BulkScrapeItemStatus.Excluded;
                itemVm.StatusMessage = "Excluded from scrape";
            }
        }

        var rawQueue = selectedItems.Select(i => i.Item).ToList();
        var itemMap = selectedItems.ToDictionary(i => i.Item);

        var progress = new Progress<BulkScrapeProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressStatus = report.StatusMessage;

            if (report.CurrentItem != null && itemMap.TryGetValue(report.CurrentItem, out var itemVm))
            {
                itemVm.SyncFromItem();
            }

            UpdateCounts();
        });

        try
        {
            var options = new BulkScrapeOptions
            {
                MergeMode = EffectiveMergeMode,
                ConfidenceThreshold = _settingsService.Settings.AutoMatchConfidenceThreshold,
                EnableSmartSeriesGrouping = true
            };

            await Task.Run(async () =>
            {
                await _queueService.ProcessQueueAsync(rawQueue, options, progress, _cts.Token);
            }, _cts.Token);

            ProgressStatus = $"Queue complete. {MatchedCount} matched, {ReviewNeededCount} review needed.";
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Queue was cancelled.";
        }
        catch (Exception ex)
        {
            ProgressStatus = $"Queue error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            foreach (var itemVm in Items)
            {
                itemVm.SyncFromItem();
            }
            UpdateCounts();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public void CancelQueue()
    {
        _cts?.Cancel();
    }

    public async Task<int> ApplyMatchedAsync()
    {
        if (IsRunning) return 0;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        ProgressPercentage = 0;
        ProgressStatus = "Writing matched metadata to comic files...";

        var rawQueue = Items.Select(i => i.Item).ToList();
        var itemMap = Items.ToDictionary(i => i.Item);

        var progress = new Progress<BulkScrapeProgressReport>(report =>
        {
            ProgressPercentage = report.PercentComplete;
            ProgressStatus = report.StatusMessage;

            if (report.CurrentItem != null && itemMap.TryGetValue(report.CurrentItem, out var itemVm))
            {
                itemVm.SyncFromItem();
            }
        });

        string chosenTemplate = (_selectedRenameTemplateIndex >= 0 && _selectedRenameTemplateIndex < RenameTemplates.Length)
            ? RenameTemplates[_selectedRenameTemplateIndex]
            : "{Series} #{Number:3} ({Year})";

        try
        {
            int count = await Task.Run(async () =>
            {
                return await _queueService.ApplyMatchedMetadataAsync(
                    rawQueue,
                    EffectiveMergeMode,
                    _alsoRenameFiles,
                    chosenTemplate,
                    progress,
                    _cts.Token);
            }, _cts.Token);

            ProgressStatus = $"Successfully saved metadata to {count} files.";
            return count;
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Save operation was cancelled.";
            return 0;
        }
        catch (Exception ex)
        {
            ProgressStatus = $"Save error: {ex.Message}";
            return 0;
        }
        finally
        {
            IsRunning = false;
            foreach (var itemVm in Items)
            {
                itemVm.SyncFromItem();
            }
            UpdateCounts();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public void UpdateCounts()
    {
        MatchedCount = Items.Count(i => i.Status == BulkScrapeItemStatus.Matched || i.Status == BulkScrapeItemStatus.Saved);
        ReviewNeededCount = Items.Count(i => i.Status == BulkScrapeItemStatus.LowConfidence);
        UnmatchedCount = Items.Count(i => i.Status == BulkScrapeItemStatus.Unmatched || i.Status == BulkScrapeItemStatus.Error);
        OnPropertyChanged(nameof(CanApply));
    }
}

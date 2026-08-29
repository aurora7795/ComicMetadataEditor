using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using InkTag.Gui.Services;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public partial class SeriesSearchWizardWindow : Window
{
    private readonly MetadataScraperService _scraperService;
    private readonly ulong? _localCoverHash;
    private readonly string? _filePath;
    private Bitmap? _localCoverBitmap;
    private SeriesSearchResult? _selectedSeries;
    private readonly List<CandidateItemViewModel> _allIssues = new();
    private CancellationTokenSource? _scanCts;
    private string _filterQuery = string.Empty;
    private const int PageSize = 100;
    private const int MaxScanIssues = 500;
    private bool _hasUserManuallySelected;

    public bool WasApplied { get; private set; }
    public ComicSearchResult? SelectedResult { get; private set; }
    public bool RequestCompareDiff { get; private set; }
    public bool ApplySeriesToRemainingUnmatched { get; private set; }
    public SeriesSearchResult? ChosenSeries => _selectedSeries;

    public SeriesSearchWizardWindow() : this(string.Empty, null, null, false)
    {
    }

    public SeriesSearchWizardWindow(string initialSeriesQuery, ulong? localCoverHash = null, string? filePath = null, bool isBulkQueueMode = false)
    {
        InitializeComponent();
        _localCoverHash = localCoverHash;
        _filePath = filePath;
        _scraperService = new MetadataScraperService(new AppSettingsService());

        if (RecheckUnmatchedCheckBox != null)
        {
            RecheckUnmatchedCheckBox.IsVisible = isBulkQueueMode;
        }

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            if (LocalFileNameTooltipText != null)
            {
                LocalFileNameTooltipText.Text = Path.GetFileName(filePath);
            }
            _ = LoadLocalCoverAsync(filePath);
        }

        string query = initialSeriesQuery;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var parsed = InkTag.Core.Parsing.ComicFilenameParser.Parse(filePath, inspectParentHierarchy: true);
            if ((string.IsNullOrWhiteSpace(query) || InkTag.Core.Parsing.ComicFilenameParser.IsTrivialOrAbbreviatedSeriesName(query, parsed.Series)) && !string.IsNullOrWhiteSpace(parsed.Series))
            {
                query = parsed.Series;
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            SeriesTitleTextBox.Text = query;
            _ = PerformSeriesSearchAsync();
        }
    }

    private async Task LoadLocalCoverAsync(string filePath)
    {
        try
        {
            var coverService = new ArchiveCoverService();
            _localCoverBitmap = await coverService.LoadCoverAsync(filePath, CancellationToken.None);
            if (_localCoverBitmap != null)
            {
                if (LocalCoverImageHeader != null) LocalCoverImageHeader.Source = _localCoverBitmap;
                if (LocalCoverImageBottom != null) LocalCoverImageBottom.Source = _localCoverBitmap;
            }
        }
        catch (Exception ex)
        {
            InkTag.Core.Logging.AppLogger.LogWarning($"[SeriesSearchWizard] Could not load local cover image: {ex.Message}");
        }
    }

    private void SetStep(int step)
    {
        if (step == 1)
        {
            Step1Panel.IsVisible = true;
            Step2Panel.IsVisible = false;

            Step1Badge.Background = SolidColorBrush.Parse("#74C7EC");
            Step1Text.Foreground = SolidColorBrush.Parse("#11111B");

            Step2Badge.Background = SolidColorBrush.Parse("#313244");
            Step2Text.Foreground = SolidColorBrush.Parse("#A6ADC8");
        }
        else
        {
            Step1Panel.IsVisible = false;
            Step2Panel.IsVisible = true;

            Step1Badge.Background = SolidColorBrush.Parse("#313244");
            Step1Text.Foreground = SolidColorBrush.Parse("#A6ADC8");

            Step2Badge.Background = SolidColorBrush.Parse("#74C7EC");
            Step2Text.Foreground = SolidColorBrush.Parse("#11111B");
        }
    }

    private async void SearchSeries_Click(object? sender, RoutedEventArgs e)
    {
        await PerformSeriesSearchAsync();
    }

    private async void SeriesTitleTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformSeriesSearchAsync();
        }
    }

    private async Task PerformSeriesSearchAsync()
    {
        if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
        {
            Step1StatusText.Text = "ComicVine API key is required. Acquire a free key at https://comicvine.gamespot.com/api/";
            Step1StatusText.IsVisible = true;
            return;
        }

        string query = SeriesTitleTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            Step1StatusText.Text = "Please enter a series title to search.";
            Step1StatusText.IsVisible = true;
            return;
        }

        Step1StatusText.Text = "Searching for series...";
        Step1StatusText.IsVisible = true;
        SeriesListBox.ItemsSource = null;

        try
        {
            var results = (await _scraperService.SearchSeriesAsync(query)).ToList();
            if (results.Any())
            {
                var viewModels = results.Select(r => new SeriesItemViewModel(r)).ToList();
                SeriesListBox.ItemsSource = viewModels;
                Step1StatusText.IsVisible = false;
            }
            else
            {
                Step1StatusText.Text = $"No series found matching '{query}'.";
                Step1StatusText.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            Step1StatusText.Text = $"Search failed: {ex.Message}";
            Step1StatusText.IsVisible = true;
        }
    }

    private void SeriesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SeriesListBox.SelectedItem is SeriesItemViewModel vm)
        {
            _ = TransitionToStep2Async(vm.Result);
        }
    }

    private void SelectSeriesItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SeriesItemViewModel vm)
        {
            _ = TransitionToStep2Async(vm.Result);
        }
    }

    private async Task TransitionToStep2Async(SeriesSearchResult series)
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        _selectedSeries = series;
        _hasUserManuallySelected = false;
        _filterQuery = string.Empty;
        if (IssueFilterTextBox != null)
        {
            IssueFilterTextBox.Text = string.Empty;
        }

        lock (_allIssues)
        {
            _allIssues.Clear();
        }

        string totalIssuesStr = series.CountOfIssues.HasValue
            ? (series.CountOfIssues.Value == 1 ? "1 total issue" : $"{series.CountOfIssues.Value} total issues")
            : "? total issues";
        SelectedSeriesSubtitleText.Text = $"{series.Publisher} • Start Year: {series.StartYear?.ToString() ?? "Unknown"} • {totalIssuesStr}";

        SetStep(2);
        await LoadAndScanSeriesIssuesAsync(series, ct);
    }

    private async Task LoadAndScanSeriesIssuesAsync(SeriesSearchResult series, CancellationToken ct)
    {
        Step2StatusText.Text = "Loading issues for series...";
        Step2StatusText.IsVisible = true;
        IssuesListBox.ItemsSource = null;
        CompareApplyButton.IsEnabled = false;
        QuickApplyButton.IsEnabled = false;
        SelectedIssueSummaryText.Text = "No issue selected. Click an issue from the list above.";
        IssuesCountStatusText.Text = "Loading...";

        try
        {
            // 1. Fetch initial batch (Page 1, up to 100 issues)
            var initialIssues = (await _scraperService.FetchSeriesIssuesAsync(series.VolumeId, 1, PageSize, null, ct)).ToList();
            if (!initialIssues.Any())
            {
                Step2StatusText.Text = "No issues found for this series.";
                Step2StatusText.IsVisible = true;
                IssuesCountStatusText.Text = "0 issues";
                return;
            }

            var initialVms = initialIssues
                .Select(i =>
                {
                    var vm = new CandidateItemViewModel(i, _localCoverHash);
                    vm.OnCoverHashComputed += OnCandidateCoverHashComputed;
                    return vm;
                })
                .ToList();

            lock (_allIssues)
            {
                _allIssues.AddRange(initialVms);
            }

            Step2StatusText.IsVisible = false;
            IssuesCountStatusText.Text = $"{_allIssues.Count} issues loaded";
            EvaluateAndSortIssues();

            // 2. If series has more than PageSize issues, scan subsequent pages in background
            int totalKnownIssues = series.CountOfIssues ?? 0;
            int totalPages = totalKnownIssues > 0 
                ? (int)Math.Ceiling((double)totalKnownIssues / PageSize) 
                : (initialIssues.Count >= PageSize ? 5 : 1);
            totalPages = Math.Min(totalPages, (int)Math.Ceiling((double)MaxScanIssues / PageSize));

            if (totalPages > 1 && initialIssues.Count >= PageSize)
            {
                _ = Task.Run(async () =>
                {
                    for (int page = 2; page <= totalPages; page++)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Check if we already have a top-confidence visual match (>= 90%)
                        lock (_allIssues)
                        {
                            if (_allIssues.Any(i => i.VisualSimilarity.HasValue && i.VisualSimilarity.Value >= 0.90))
                            {
                                break; // Early exit! Confident match found.
                            }
                        }

                        try
                        {
                            var pageIssues = (await _scraperService.FetchSeriesIssuesAsync(series.VolumeId, page, PageSize, null, ct)).ToList();
                            if (!pageIssues.Any() || ct.IsCancellationRequested) break;

                            var pageVms = pageIssues
                                .Select(i =>
                                {
                                    var vm = new CandidateItemViewModel(i, _localCoverHash);
                                    vm.OnCoverHashComputed += OnCandidateCoverHashComputed;
                                    return vm;
                                })
                                .ToList();

                            lock (_allIssues)
                            {
                                _allIssues.AddRange(pageVms);
                            }

                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (!ct.IsCancellationRequested)
                                {
                                    int count;
                                    lock (_allIssues) { count = _allIssues.Count; }
                                    string totalStr = series.CountOfIssues?.ToString() ?? "series";
                                    IssuesCountStatusText.Text = $"Scanning {count} of {totalStr} issues...";
                                    EvaluateAndSortIssues();
                                }
                            });
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            InkTag.Core.Logging.AppLogger.LogWarning($"[SeriesSearchWizard] Background page fetch error: {ex.Message}");
                            break;
                        }
                    }

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            int finalCount;
                            lock (_allIssues) { finalCount = _allIssues.Count; }
                            IssuesCountStatusText.Text = $"{finalCount} issues loaded";
                        }
                    });
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Canceled by user navigation
        }
        catch (Exception ex)
        {
            Step2StatusText.Text = $"Failed to load issues: {ex.Message}";
            Step2StatusText.IsVisible = true;
            IssuesCountStatusText.Text = "Error";
        }
    }

    private void OnCandidateCoverHashComputed(CandidateItemViewModel vm)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => EvaluateAndSortIssues());
    }

    private void EvaluateAndSortIssues()
    {
        List<CandidateItemViewModel> snapshot;
        lock (_allIssues)
        {
            snapshot = _allIssues.ToList();
        }

        if (snapshot.Count == 0) return;

        // Filter issues if query is entered
        IEnumerable<CandidateItemViewModel> filtered = snapshot;
        if (!string.IsNullOrWhiteSpace(_filterQuery))
        {
            string q = _filterQuery.Trim();
            filtered = filtered.Where(i =>
                (i.Result.IssueNumber != null && i.Result.IssueNumber.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (i.Result.IssueTitle != null && i.Result.IssueTitle.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (i.DisplayTitle != null && i.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var filteredList = filtered.ToList();

        CandidateItemViewModel? topMatch = null;
        double bestSim = 0.0;

        if (_localCoverHash.HasValue && _localCoverHash.Value != 0)
        {
            // Evaluate global best similarity across all loaded issues
            foreach (var item in snapshot)
            {
                if (item.VisualSimilarity.HasValue && item.VisualSimilarity.Value > bestSim)
                {
                    bestSim = item.VisualSimilarity.Value;
                    topMatch = item;
                }
            }

            foreach (var item in filteredList)
            {
                item.IsTopVisualMatch = topMatch != null && item == topMatch && bestSim >= 0.70;
            }
        }

        // Sort: Top visual matches (>= 70%) placed at the very top by descending confidence, followed by natural issue order
        var sorted = filteredList
            .OrderByDescending(c => (c.VisualSimilarity.HasValue && c.VisualSimilarity.Value >= 0.70) ? c.VisualSimilarity.Value : -1.0)
            .ThenBy(i => GetNumericIssueNumber(i.IssueNumber))
            .ThenBy(i => i.IssueNumber)
            .ToList();

        var selected = IssuesListBox.SelectedItem as CandidateItemViewModel;
        IssuesListBox.ItemsSource = sorted;

        if (!_hasUserManuallySelected && topMatch != null && bestSim >= 0.85 && sorted.Contains(topMatch))
        {
            IssuesListBox.SelectedItem = topMatch;
        }
        else if (selected != null && sorted.Contains(selected))
        {
            IssuesListBox.SelectedItem = selected;
        }
    }

    private void IssueFilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _filterQuery = IssueFilterTextBox.Text?.Trim() ?? string.Empty;
        EvaluateAndSortIssues();
    }

    private void BackToStep1_Click(object? sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        SetStep(1);
    }

    private void IssuesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            _hasUserManuallySelected = true;
        }

        if (IssuesListBox.SelectedItem is CandidateItemViewModel vm)
        {
            SelectedResult = vm.Result;
            if (CandidateCoverImageBottom != null)
            {
                CandidateCoverImageBottom.Source = vm.Thumbnail;
            }
            string matchNote = vm.VisualSimilarity.HasValue && vm.VisualSimilarity.Value > 0 ? $" [Cover Match: {(int)Math.Round(vm.VisualSimilarity.Value * 100)}%]" : "";
            SelectedIssueSummaryText.Text = $"Selected: {vm.Result.SeriesTitle} #{vm.Result.IssueNumber} ({vm.Result.IssueTitle}){matchNote}";
            CompareApplyButton.IsEnabled = true;
            QuickApplyButton.IsEnabled = true;
        }
        else
        {
            SelectedResult = null;
            if (CandidateCoverImageBottom != null)
            {
                CandidateCoverImageBottom.Source = null;
            }
            SelectedIssueSummaryText.Text = "No issue selected. Click an issue from the list above.";
            CompareApplyButton.IsEnabled = false;
            QuickApplyButton.IsEnabled = false;
        }
    }

    private void CompareApply_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedResult != null)
        {
            ApplySeriesToRemainingUnmatched = RecheckUnmatchedCheckBox?.IsChecked == true;
            WasApplied = true;
            RequestCompareDiff = true;
            Close();
        }
    }

    private void QuickApply_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedResult != null)
        {
            ApplySeriesToRemainingUnmatched = RecheckUnmatchedCheckBox?.IsChecked == true;
            WasApplied = true;
            RequestCompareDiff = false;
            Close();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasApplied = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        base.OnClosed(e);
    }

    private static double GetNumericIssueNumber(string issueNum)
    {
        if (string.IsNullOrWhiteSpace(issueNum)) return double.MaxValue;
        var match = System.Text.RegularExpressions.Regex.Match(issueNum, @"\d+(\.\d+)?");
        if (match.Success && double.TryParse(match.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }
        return double.MaxValue;
    }
}

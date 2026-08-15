using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public partial class SeriesSearchWizardWindow : Window
{
    private readonly MetadataScraperService _scraperService;
    private readonly ulong? _localCoverHash;
    private SeriesSearchResult? _selectedSeries;
    private int _currentPage = 1;
    private const int PageSize = 50;
    private bool _hasUserManuallySelected;

    public bool WasApplied { get; private set; }
    public ComicSearchResult? SelectedResult { get; private set; }
    public bool RequestCompareDiff { get; private set; }

    public SeriesSearchWizardWindow() : this(string.Empty, null)
    {
    }

    public SeriesSearchWizardWindow(string initialSeriesQuery, ulong? localCoverHash = null)
    {
        InitializeComponent();
        _localCoverHash = localCoverHash;
        _scraperService = new MetadataScraperService(new AppSettingsService());

        if (!string.IsNullOrWhiteSpace(initialSeriesQuery))
        {
            SeriesTitleTextBox.Text = initialSeriesQuery;
            _ = PerformSeriesSearchAsync();
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
        _selectedSeries = series;
        _currentPage = 1;

        SelectedSeriesTitleText.Text = series.SeriesTitle;
        SelectedSeriesSubtitleText.Text = $"{series.Publisher} • Start Year: {series.StartYear?.ToString() ?? "Unknown"} • {series.CountOfIssues?.ToString() ?? "?"} total issues";

        SetStep(2);
        await LoadSeriesIssuesAsync();
    }

    private async Task LoadSeriesIssuesAsync()
    {
        if (_selectedSeries == null) return;

        Step2StatusText.Text = $"Loading issues (Page {_currentPage})...";
        Step2StatusText.IsVisible = true;
        IssuesListBox.ItemsSource = null;
        CompareApplyButton.IsEnabled = false;
        QuickApplyButton.IsEnabled = false;
        SelectedIssueSummaryText.Text = "No issue selected. Click an issue from the list above.";
        _hasUserManuallySelected = false;

        try
        {
            var issues = (await _scraperService.FetchSeriesIssuesAsync(_selectedSeries.VolumeId, _currentPage, PageSize)).ToList();
            if (issues.Any())
            {
                var viewModels = issues
                    .OrderBy(i => GetNumericIssueNumber(i.IssueNumber))
                    .ThenBy(i => i.IssueNumber)
                    .Select(i =>
                    {
                        var vm = new CandidateItemViewModel(i, _localCoverHash);
                        vm.OnCoverHashComputed += OnCandidateCoverHashComputed;
                        return vm;
                    })
                    .ToList();

                IssuesListBox.ItemsSource = viewModels;
                Step2StatusText.IsVisible = false;

                EvaluateTopVisualMatch(viewModels);
            }
            else
            {
                Step2StatusText.Text = "No issues found on this page.";
                Step2StatusText.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            Step2StatusText.Text = $"Failed to load issues: {ex.Message}";
            Step2StatusText.IsVisible = true;
        }

        UpdatePaginationControls();
    }

    private void OnCandidateCoverHashComputed(CandidateItemViewModel vm)
    {
        if (IssuesListBox.ItemsSource is List<CandidateItemViewModel> list)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => EvaluateTopVisualMatch(list));
        }
    }

    private void EvaluateTopVisualMatch(List<CandidateItemViewModel> list)
    {
        if (!_localCoverHash.HasValue || _localCoverHash.Value == 0 || list.Count == 0) return;

        CandidateItemViewModel? topMatch = null;
        double bestSim = 0.0;

        foreach (var item in list)
        {
            if (item.VisualSimilarity.HasValue && item.VisualSimilarity.Value > bestSim)
            {
                bestSim = item.VisualSimilarity.Value;
                topMatch = item;
            }
        }

        foreach (var item in list)
        {
            item.IsTopVisualMatch = topMatch != null && item == topMatch && bestSim >= 0.70;
        }

        // Auto-select if top match is high confidence (>= 85%) and user hasn't made a manual pick yet
        if (!_hasUserManuallySelected && topMatch != null && bestSim >= 0.85 && IssuesListBox.SelectedItem != topMatch)
        {
            IssuesListBox.SelectedItem = topMatch;
        }
    }

    private void UpdatePaginationControls()
    {
        PrevPageButton.IsEnabled = _currentPage > 1;
        // If total count is known, calculate total pages
        if (_selectedSeries?.CountOfIssues.HasValue == true && _selectedSeries.CountOfIssues.Value > 0)
        {
            int totalPages = (int)Math.Ceiling((double)_selectedSeries.CountOfIssues.Value / PageSize);
            PageIndicatorText.Text = $"Page {_currentPage} of {Math.Max(1, totalPages)}";
            NextPageButton.IsEnabled = _currentPage < totalPages;
        }
        else
        {
            PageIndicatorText.Text = $"Page {_currentPage}";
            NextPageButton.IsEnabled = true;
        }
    }

    private async void PrevPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            await LoadSeriesIssuesAsync();
        }
    }

    private async void NextPage_Click(object? sender, RoutedEventArgs e)
    {
        _currentPage++;
        await LoadSeriesIssuesAsync();
    }

    private void BackToStep1_Click(object? sender, RoutedEventArgs e)
    {
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
            string matchNote = vm.VisualSimilarity.HasValue && vm.VisualSimilarity.Value > 0 ? $" [Cover Match: {vm.VisualSimilarity.Value:P0}]" : "";
            SelectedIssueSummaryText.Text = $"Selected: {vm.Result.SeriesTitle} #{vm.Result.IssueNumber} ({vm.Result.IssueTitle}){matchNote}";
            CompareApplyButton.IsEnabled = true;
            QuickApplyButton.IsEnabled = true;
        }
        else
        {
            SelectedResult = null;
            SelectedIssueSummaryText.Text = "No issue selected. Click an issue from the list above.";
            CompareApplyButton.IsEnabled = false;
            QuickApplyButton.IsEnabled = false;
        }
    }

    private void CompareApply_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedResult != null)
        {
            WasApplied = true;
            RequestCompareDiff = true;
            Close();
        }
    }

    private void QuickApply_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedResult != null)
        {
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

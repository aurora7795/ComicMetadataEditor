using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Scrapers;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public class FieldDiffItem
{
    public bool IsSelected { get; set; } = true;
    public string FieldName { get; set; } = string.Empty;
    public string LocalValue { get; set; } = string.Empty;
    public string OnlineValue { get; set; } = string.Empty;
}

public partial class ScraperMatchWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    private readonly ComicInfo _targetComic;
    private readonly MetadataScraperService _scraperService;
    private readonly ulong? _localCoverHash;
    private ComicInfo? _fetchedComic;

    private Avalonia.Media.Imaging.Bitmap? _localCoverImage;
    public Avalonia.Media.Imaging.Bitmap? LocalCoverImage
    {
        get => _localCoverImage;
        set { _localCoverImage = value; OnPropertyChanged(nameof(LocalCoverImage)); }
    }

    private Avalonia.Media.Imaging.Bitmap? _selectedCandidateThumbnail;
    public Avalonia.Media.Imaging.Bitmap? SelectedCandidateThumbnail
    {
        get => _selectedCandidateThumbnail;
        set { _selectedCandidateThumbnail = value; OnPropertyChanged(nameof(SelectedCandidateThumbnail)); }
    }

    private string _visualSimilarityText = string.Empty;
    public string VisualSimilarityText
    {
        get => _visualSimilarityText;
        set { _visualSimilarityText = value; OnPropertyChanged(nameof(VisualSimilarityText)); OnPropertyChanged(nameof(HasVisualSimilarityText)); }
    }

    public bool HasVisualSimilarityText => !string.IsNullOrEmpty(_visualSimilarityText);

    public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public bool WasApplied { get; private set; }
    public ComicSearchResult? SelectedCandidate { get; private set; }
    public ComicInfo? FetchedComic => _fetchedComic;
    public ObservableCollection<FieldDiffItem> FieldDiffs { get; } = new();

    public ScraperMatchWindow() : this(new ComicInfo())
    {
    }

    public ScraperMatchWindow(ComicInfo targetComic, IEnumerable<ComicSearchResult>? initialCandidates = null, Avalonia.Media.Imaging.Bitmap? localCover = null, ulong? localCoverHash = null, string? filePath = null)
    {
        InitializeComponent();
        DataContext = this;
        _targetComic = targetComic;
        _localCoverHash = localCoverHash;
        LocalCoverImage = localCover;
        _scraperService = new MetadataScraperService(new AppSettingsService());

        DiffDataGrid.ItemsSource = FieldDiffs;

        // Populate search queries from target comic or fallback to filename parser
        string series = targetComic.Series ?? "";
        string issue = targetComic.Number ?? "";
        string year = targetComic.Year?.ToString() ?? "";

        if ((string.IsNullOrWhiteSpace(series) || string.IsNullOrWhiteSpace(issue) || string.IsNullOrWhiteSpace(year)) && !string.IsNullOrWhiteSpace(filePath))
        {
            var parsed = InkTag.Core.Parsing.ComicFilenameParser.Parse(filePath);
            if (string.IsNullOrWhiteSpace(series) && !string.IsNullOrWhiteSpace(parsed.Series))
            {
                series = parsed.Series;
            }
            if (string.IsNullOrWhiteSpace(issue) && !string.IsNullOrWhiteSpace(parsed.IssueNumber))
            {
                issue = parsed.IssueNumber;
            }
            if (string.IsNullOrWhiteSpace(year) && parsed.Year.HasValue)
            {
                year = parsed.Year.Value.ToString();
            }
        }

        SeriesTextBox.Text = series;
        IssueTextBox.Text = issue;
        YearTextBox.Text = year;

        if (initialCandidates != null && initialCandidates.Any())
        {
            int.TryParse(year, out int parsedYear);
            var initialQuery = new ComicSearchQuery
            {
                Series = series,
                IssueNumber = issue,
                Year = parsedYear != 0 ? parsedYear : null
            };
            SetCandidates(initialCandidates, initialQuery);
        }
        else if (!string.IsNullOrWhiteSpace(series))
        {
            _ = PerformSearchAsync();
        }
    }

    private void SetCandidates(IEnumerable<ComicSearchResult> candidates, ComicSearchQuery? query = null)
    {
        var viewModels = candidates.Select(c =>
        {
            var vm = new CandidateItemViewModel(c, _localCoverHash, query);
            vm.OnCoverHashComputed += OnCandidateCoverHashComputed;
            return vm;
        }).ToList();

        var sorted = viewModels
            .OrderByDescending(c => c.MatchConfidence)
            .ThenByDescending(c => c.VisualSimilarity ?? 0.0)
            .ToList();

        UpdateTopVisualMatchFlag(sorted);
        CandidatesListBox.ItemsSource = sorted;

        if (sorted.Any())
        {
            CandidatesListBox.SelectedIndex = 0;
        }
    }

    private void OnCandidateCoverHashComputed(CandidateItemViewModel vm)
    {
        if (!_localCoverHash.HasValue || _localCoverHash.Value == 0) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (CandidatesListBox.ItemsSource is IEnumerable<CandidateItemViewModel> currentItems)
            {
                var currentList = currentItems.ToList();
                var selected = CandidatesListBox.SelectedItem as CandidateItemViewModel;

                var sorted = currentList
                    .OrderByDescending(c => c.MatchConfidence)
                    .ThenByDescending(c => c.VisualSimilarity ?? 0.0)
                    .ToList();

                UpdateTopVisualMatchFlag(sorted);

                if (!currentList.SequenceEqual(sorted))
                {
                    CandidatesListBox.ItemsSource = sorted;
                    if (selected != null && sorted.Contains(selected))
                    {
                        CandidatesListBox.SelectedItem = selected;
                    }
                    else if (sorted.Any())
                    {
                        CandidatesListBox.SelectedIndex = 0;
                    }
                }
            }
        });
    }

    private void UpdateTopVisualMatchFlag(List<CandidateItemViewModel> items)
    {
        var top = items.FirstOrDefault(c => c.VisualSimilarity.HasValue && c.VisualSimilarity.Value >= 0.70);
        foreach (var item in items)
        {
            item.IsTopVisualMatch = top != null && item == top;
        }
    }

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        await PerformSearchAsync();
    }

    private async void SeriesWizard_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
        {
            return;
        }

        string initialQuery = SeriesTextBox.Text?.Trim() ?? _targetComic.Series ?? "";
        var wizard = new SeriesSearchWizardWindow(initialQuery, _localCoverHash);
        await wizard.ShowDialog(this);

        if (wizard.WasApplied && wizard.SelectedResult != null)
        {
            int? year = int.TryParse(YearTextBox.Text, out int y) ? y : _targetComic.Year;
            var targetQuery = new ComicSearchQuery
            {
                Series = !string.IsNullOrWhiteSpace(SeriesTextBox.Text) ? SeriesTextBox.Text.Trim() : (_targetComic.Series ?? ""),
                IssueNumber = !string.IsNullOrWhiteSpace(IssueTextBox.Text) ? IssueTextBox.Text.Trim() : (_targetComic.Number ?? ""),
                Year = year
            };
            wizard.SelectedResult.MatchConfidence = ComicVineProvider.CalculateConfidence(wizard.SelectedResult, targetQuery, _localCoverHash);

            SetCandidates(new[] { wizard.SelectedResult });

            if (!wizard.RequestCompareDiff)
            {
                // Quick Apply All
                OverwriteAll_Click(sender, e);
            }
        }
    }

    private async Task PerformSearchAsync()
    {
        if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
        {
            return;
        }

        SearchButton.IsEnabled = false;
        try
        {
            int? year = int.TryParse(YearTextBox.Text, out int y) ? y : null;
            var query = new ComicSearchQuery
            {
                Series = SeriesTextBox.Text?.Trim() ?? "",
                IssueNumber = IssueTextBox.Text?.Trim() ?? "",
                Year = year
            };

            var candidates = (await _scraperService.SearchCandidatesAsync(query)).ToList();
            SetCandidates(candidates, query);
        }
        catch
        {
            CandidatesListBox.ItemsSource = Array.Empty<CandidateItemViewModel>();
        }
        finally
        {
            SearchButton.IsEnabled = true;
        }
    }

    private CandidateItemViewModel? _subscribedCandidateVm;

    private async void CandidatesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_subscribedCandidateVm != null)
        {
            _subscribedCandidateVm.PropertyChanged -= CandidateVm_PropertyChanged;
            _subscribedCandidateVm = null;
        }

        ComicSearchResult? selected = null;
        if (CandidatesListBox.SelectedItem is CandidateItemViewModel vm)
        {
            _subscribedCandidateVm = vm;
            _subscribedCandidateVm.PropertyChanged += CandidateVm_PropertyChanged;
            selected = vm.Result;
            SelectedCandidateThumbnail = vm.Thumbnail;
            UpdateVisualSimilarityDisplay(vm);
        }
        else if (CandidatesListBox.SelectedItem is ComicSearchResult res)
        {
            selected = res;
            SelectedCandidateThumbnail = null;
            VisualSimilarityText = string.Empty;
        }
        else
        {
            VisualSimilarityText = string.Empty;
        }

        if (selected != null)
        {
            try
            {
                _fetchedComic = await _scraperService.FetchMetadataAsync(selected.IssueId);
                BuildDiffTable(_targetComic, _fetchedComic);
            }
            catch
            {
                FieldDiffs.Clear();
            }
        }
    }

    private void CandidateVm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is CandidateItemViewModel vm)
        {
            if (e.PropertyName == nameof(CandidateItemViewModel.Thumbnail))
            {
                SelectedCandidateThumbnail = vm.Thumbnail;
            }
            if (e.PropertyName == nameof(CandidateItemViewModel.VisualSimilarity))
            {
                UpdateVisualSimilarityDisplay(vm);
            }
        }
    }

    private void UpdateVisualSimilarityDisplay(CandidateItemViewModel vm)
    {
        if (vm.VisualSimilarity.HasValue && vm.VisualSimilarity.Value > 0)
        {
            VisualSimilarityText = $"👁 {vm.VisualSimilarity.Value:P0} Cover Match";
        }
        else
        {
            VisualSimilarityText = string.Empty;
        }
    }

    private void BuildDiffTable(ComicInfo local, ComicInfo online)
    {
        FieldDiffs.Clear();
        PropertyInfo[] properties = typeof(ComicInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name == nameof(ComicInfo.PageCount) || prop.Name == nameof(ComicInfo.Pages)) continue;

            string localVal = prop.GetValue(local)?.ToString() ?? "";
            string onlineVal = prop.GetValue(online)?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(onlineVal)) continue;

            bool isMissingInLocal = string.IsNullOrWhiteSpace(localVal);

            FieldDiffs.Add(new FieldDiffItem
            {
                IsSelected = isMissingInLocal || localVal != onlineVal,
                FieldName = prop.Name,
                LocalValue = string.IsNullOrEmpty(localVal) ? "<empty>" : localVal,
                OnlineValue = onlineVal
            });
        }
    }

    private void SelectAllFields_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in FieldDiffs) item.IsSelected = true;
        DiffDataGrid.ItemsSource = null;
        DiffDataGrid.ItemsSource = FieldDiffs;
    }

    private void SelectNoFields_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in FieldDiffs) item.IsSelected = false;
        DiffDataGrid.ItemsSource = null;
        DiffDataGrid.ItemsSource = FieldDiffs;
    }

    private void ApplySelected_Click(object? sender, RoutedEventArgs e)
    {
        if (_fetchedComic == null) return;

        SelectedCandidate = (CandidatesListBox.SelectedItem as CandidateItemViewModel)?.Result;
        var selectedFields = new HashSet<string>(FieldDiffs.Where(f => f.IsSelected).Select(f => f.FieldName));
        _scraperService.ApplyMetadata(_targetComic, _fetchedComic, ScrapeMergeMode.SelectiveFields, selectedFields);
        WasApplied = true;
        Close();
    }

    private void OverwriteAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_fetchedComic == null) return;

        SelectedCandidate = (CandidatesListBox.SelectedItem as CandidateItemViewModel)?.Result;
        _scraperService.ApplyMetadata(_targetComic, _fetchedComic, ScrapeMergeMode.OverwriteAll);
        WasApplied = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasApplied = false;
        Close();
    }
}

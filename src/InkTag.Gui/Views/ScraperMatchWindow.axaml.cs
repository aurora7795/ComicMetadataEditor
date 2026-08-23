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
    private readonly ulong? _localCoverHash;
    private readonly string? _filePath;
    private readonly MetadataScraperService _scraperService;
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
        _filePath = filePath;
        LocalCoverImage = localCover;
        _scraperService = new MetadataScraperService(new AppSettingsService());

        DiffDataGrid.ItemsSource = FieldDiffs;

        // Populate search queries from target comic or fallback to filename parser & parent directory inference
        var query = MetadataScraperService.ExtractQueryFromComicInfo(targetComic, filePath);
        string series = query.Series;
        string issue = query.IssueNumber;
        string year = query.Year?.ToString() ?? "";

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
                Year = parsedYear > 0 ? parsedYear : null
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
        var candidateList = candidates.ToList();
        var viewModels = candidateList.Select(c =>
        {
            var vm = new CandidateItemViewModel(c, _localCoverHash, query);
            vm.OnCoverHashComputed += OnCandidateCoverHashComputed;
            return vm;
        }).ToList();

        CandidatesListBox.ItemsSource = viewModels;

        if (viewModels.Any())
        {
            CandidatesListBox.SelectedIndex = 0;
        }
        else
        {
            SelectedCandidateThumbnail = null;
            VisualSimilarityText = string.Empty;
        }
    }

    private void OnCandidateCoverHashComputed(CandidateItemViewModel vm)
    {
        if (CandidatesListBox.ItemsSource is IEnumerable<CandidateItemViewModel> currentItems)
        {
            var list = currentItems.ToList();
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

        // Reorder candidates to place top visual matches at the top of the list
        var sorted = list
            .OrderByDescending(c => c.VisualSimilarity ?? 0.0)
            .ThenByDescending(c => c.MatchConfidence)
            .ToList();

        if (!list.SequenceEqual(sorted))
        {
            var selected = CandidatesListBox.SelectedItem as CandidateItemViewModel;
            CandidatesListBox.ItemsSource = sorted;

            if (topMatch != null && bestSim >= 0.70)
            {
                CandidatesListBox.SelectedItem = topMatch;
            }
            else if (selected != null && sorted.Contains(selected))
            {
                CandidatesListBox.SelectedItem = selected;
            }
        }
        else if (topMatch != null && bestSim >= 0.85 && CandidatesListBox.SelectedItem != topMatch)
        {
            CandidatesListBox.SelectedItem = topMatch;
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
        var wizard = new SeriesSearchWizardWindow(initialQuery, _localCoverHash, _filePath);
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
                // Quick Apply All: fetch full issue metadata and apply directly to target comic
                try
                {
                    var fetchedComic = await _scraperService.FetchMetadataAsync(wizard.SelectedResult.IssueId);
                    _scraperService.ApplyMetadata(_targetComic, fetchedComic, ScrapeMergeMode.OverwriteAll);
                    SelectedCandidate = wizard.SelectedResult;
                    _fetchedComic = fetchedComic;
                    WasApplied = true;
                    Close();
                }
                catch (Exception ex)
                {
                    Core.Logging.AppLogger.LogError($"Failed to quick apply metadata for issue {wizard.SelectedResult.IssueId}", ex);
                }
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
            VisualSimilarityText = $"👁 {(int)Math.Round(vm.VisualSimilarity.Value * 100)}% Cover Match";
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

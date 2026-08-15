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

    public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public bool WasApplied { get; private set; }
    public ObservableCollection<FieldDiffItem> FieldDiffs { get; } = new();

    public ScraperMatchWindow() : this(new ComicInfo())
    {
    }

    public ScraperMatchWindow(ComicInfo targetComic, IEnumerable<ComicSearchResult>? initialCandidates = null, Avalonia.Media.Imaging.Bitmap? localCover = null, ulong? localCoverHash = null)
    {
        InitializeComponent();
        DataContext = this;
        _targetComic = targetComic;
        _localCoverHash = localCoverHash;
        LocalCoverImage = localCover;
        _scraperService = new MetadataScraperService(new AppSettingsService());

        DiffDataGrid.ItemsSource = FieldDiffs;

        // Populate search queries from target comic
        SeriesTextBox.Text = targetComic.Series ?? "";
        IssueTextBox.Text = targetComic.Number ?? "";
        YearTextBox.Text = targetComic.Year?.ToString() ?? "";

        if (initialCandidates != null && initialCandidates.Any())
        {
            var viewModels = initialCandidates.Select(c => new CandidateItemViewModel(c, _localCoverHash)).ToList();
            CandidatesListBox.ItemsSource = viewModels;
            CandidatesListBox.SelectedIndex = 0;
        }
        else
        {
            _ = PerformSearchAsync();
        }
    }

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        await PerformSearchAsync();
    }

    private async void SeriesWizard_Click(object? sender, RoutedEventArgs e)
    {
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

            var vm = new CandidateItemViewModel(wizard.SelectedResult, _localCoverHash);
            CandidatesListBox.ItemsSource = new List<CandidateItemViewModel> { vm };
            CandidatesListBox.SelectedIndex = 0;

            if (!wizard.RequestCompareDiff)
            {
                // Quick Apply All
                OverwriteAll_Click(sender, e);
            }
        }
    }

    private async Task PerformSearchAsync()
    {
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
            var viewModels = candidates.Select(c => new CandidateItemViewModel(c, _localCoverHash)).ToList();
            CandidatesListBox.ItemsSource = viewModels;

            if (viewModels.Any())
            {
                CandidatesListBox.SelectedIndex = 0;
            }
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
        }
        else if (CandidatesListBox.SelectedItem is ComicSearchResult res)
        {
            selected = res;
            SelectedCandidateThumbnail = null;
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
        if (e.PropertyName == nameof(CandidateItemViewModel.Thumbnail) && sender is CandidateItemViewModel vm)
        {
            SelectedCandidateThumbnail = vm.Thumbnail;
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

        var selectedFields = new HashSet<string>(FieldDiffs.Where(f => f.IsSelected).Select(f => f.FieldName));
        _scraperService.ApplyMetadata(_targetComic, _fetchedComic, ScrapeMergeMode.SelectiveFields, selectedFields);
        WasApplied = true;
        Close();
    }

    private void OverwriteAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_fetchedComic == null) return;

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

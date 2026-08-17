using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public partial class MainWindow : Window
{
    private GridLength _savedInspectorWidth = new GridLength(350);

    private ColumnDefinition? InspectorColumn => 
        MainWorkspaceGrid?.ColumnDefinitions != null && MainWorkspaceGrid.ColumnDefinitions.Count > 2 
            ? MainWorkspaceGrid.ColumnDefinitions[2] 
            : null;

    public MainWindow()
    {
        InitializeComponent();
        
        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.SaveFinishedWithErrors += OnSaveFinishedWithErrors;
        vm.PropertyChanged += Vm_PropertyChanged;

        UpdateInspectorColumnWidth(vm.IsInspectorVisible);
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsInspectorVisible) && DataContext is MainWindowViewModel vm)
        {
            UpdateInspectorColumnWidth(vm.IsInspectorVisible);
        }
    }

    private void UpdateInspectorColumnWidth(bool isVisible)
    {
        if (InspectorColumn == null) return;

        if (!isVisible)
        {
            if (InspectorColumn.Width.Value > 0)
            {
                _savedInspectorWidth = InspectorColumn.Width;
            }
            InspectorColumn.MinWidth = 0;
            InspectorColumn.Width = new GridLength(0);
        }
        else
        {
            InspectorColumn.MinWidth = 250;
            InspectorColumn.Width = _savedInspectorWidth.Value > 0 ? _savedInspectorWidth : new GridLength(350);
        }
    }

    private async void OnSaveFinishedWithErrors()
    {
        if (DataContext is MainWindowViewModel vm && vm.SaveFailures.Any())
        {
            var dialog = new ErrorSummaryWindow(vm.SaveFailures);
            await dialog.ShowDialog(this);
        }
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Comics Folder",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0)
        {
            var folderPath = folders[0].Path.LocalPath;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectedDirectory = folderPath;
                await vm.LoadDirectoryCommand.ExecuteAsync(null);
            }
        }
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV File",
            DefaultExtension = ".csv",
            SuggestedFileName = "comics_metadata.csv"
        });

        if (file != null)
        {
            var filePath = file.Path.LocalPath;
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.ExportCsvCommand.ExecuteAsync(filePath);
            }
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        ComicsGrid.SelectAll();
    }

    private void ClearSelection_Click(object? sender, RoutedEventArgs e)
    {
        ComicsGrid.SelectedItems.Clear();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow();
        dialog.ShowDialog(this);
    }

    private async void ScrapeMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var targetItem = vm.ActiveComic ?? vm.Comics.FirstOrDefault();
            if (targetItem == null)
            {
                return;
            }

            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            if (vm.ActiveComic == null)
            {
                vm.ActiveComic = targetItem;
            }

            if (targetItem.CoverImage == null && !string.IsNullOrEmpty(targetItem.FilePath))
            {
                var coverService = new Services.ArchiveCoverService();
                await targetItem.LoadCoverAsync(coverService, System.Threading.CancellationToken.None);
            }

            var model = targetItem.ToModel();
            var dialog = new ScraperMatchWindow(model, null, targetItem.CoverImage, targetItem.CoverHash != 0 ? targetItem.CoverHash : null, targetItem.FilePath);
            await dialog.ShowDialog(this);

            if (dialog.WasApplied)
            {
                targetItem.LoadFromModel(model);
                targetItem.IsDirty = true;
            }
        }
    }

    private async void SeriesSearchWizard_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var targetItem = vm.ActiveComic ?? vm.Comics.FirstOrDefault();
            if (targetItem == null)
            {
                return;
            }

            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            if (vm.ActiveComic == null)
            {
                vm.ActiveComic = targetItem;
            }

            if (targetItem.CoverImage == null && !string.IsNullOrEmpty(targetItem.FilePath))
            {
                var coverService = new Services.ArchiveCoverService();
                await targetItem.LoadCoverAsync(coverService, System.Threading.CancellationToken.None);
            }

            string initialQuery = targetItem.Series ?? "";
            var wizard = new SeriesSearchWizardWindow(initialQuery, targetItem.CoverHash != 0 ? targetItem.CoverHash : null, targetItem.FilePath);
            await wizard.ShowDialog(this);

            if (wizard.WasApplied && wizard.SelectedResult != null)
            {
                var model = targetItem.ToModel();
                if (wizard.RequestCompareDiff)
                {
                    var matchWindow = new ScraperMatchWindow(model, new[] { wizard.SelectedResult }, targetItem.CoverImage, targetItem.CoverHash != 0 ? targetItem.CoverHash : null, targetItem.FilePath);
                    await matchWindow.ShowDialog(this);
                    if (matchWindow.WasApplied)
                    {
                        targetItem.LoadFromModel(model);
                        targetItem.IsDirty = true;
                    }
                }
                else
                {
                    var scraperService = new InkTag.Core.Scrapers.MetadataScraperService(new InkTag.Core.Configuration.AppSettingsService());
                    var fetchedComic = await scraperService.FetchMetadataAsync(wizard.SelectedResult.IssueId);
                    scraperService.ApplyMetadata(model, fetchedComic, InkTag.Core.Scrapers.ScrapeMergeMode.OverwriteAll);
                    targetItem.LoadFromModel(model);
                    targetItem.IsDirty = true;
                }
            }
        }
    }

    private void InferMetadataFromFilenames_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var targetComics = ComicsGrid.SelectedItems?.Cast<ComicItemViewModel>().ToList();
            if (targetComics == null || targetComics.Count == 0)
            {
                targetComics = vm.Comics.ToList();
            }

            foreach (var item in targetComics)
            {
                if (string.IsNullOrEmpty(item.FilePath)) continue;

                var parsed = InkTag.Core.Parsing.ComicFilenameParser.Parse(item.FilePath);
                bool modified = false;

                if (string.IsNullOrWhiteSpace(item.Series) && !string.IsNullOrWhiteSpace(parsed.Series))
                {
                    item.Series = parsed.Series;
                    modified = true;
                }

                if (string.IsNullOrWhiteSpace(item.Number) && !string.IsNullOrWhiteSpace(parsed.IssueNumber))
                {
                    item.Number = parsed.IssueNumber;
                    modified = true;
                }

                if ((!item.Year.HasValue || item.Year == 0) && parsed.Year.HasValue)
                {
                    item.Year = parsed.Year.Value;
                    modified = true;
                }

                if ((!item.Volume.HasValue || item.Volume == 0) && parsed.Volume.HasValue)
                {
                    item.Volume = parsed.Volume.Value;
                    modified = true;
                }

                if (modified)
                {
                    item.IsDirty = true;
                }
            }
        }
    }

    private void NativeInferMetadataFromFilenames_Click(object? sender, EventArgs e) => InferMetadataFromFilenames_Click(sender, new RoutedEventArgs());

    private void NativeSeriesSearchWizard_Click(object? sender, EventArgs e) => SeriesSearchWizard_Click(sender, new RoutedEventArgs());

    private void OpenAbout_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow();
        dialog.ShowDialog(this);
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void NativeOpenFolder_Click(object? sender, EventArgs e) => OpenFolder_Click(sender, new RoutedEventArgs());
    private async void NativeSaveAll_Click(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.SaveAllCommand.ExecuteAsync(null);
    }
    private void NativeExportCsv_Click(object? sender, EventArgs e) => ExportCsv_Click(sender, new RoutedEventArgs());
    private void NativeExit_Click(object? sender, EventArgs e) => Close();
    private void NativeSelectAll_Click(object? sender, EventArgs e) => SelectAll_Click(sender, new RoutedEventArgs());
    private void NativeClearSelection_Click(object? sender, EventArgs e) => ClearSelection_Click(sender, new RoutedEventArgs());
    private async void NativeRefreshGrid_Click(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.RefreshGridCommand.ExecuteAsync(null);
    }
    private void NativeOpenLogs_Click(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.OpenLogsCommand.Execute(null);
    }
    private async void NativeCheckForUpdates_Click(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.CheckForUpdatesCommand.ExecuteAsync(null);
    }
    private void NativeAbout_Click(object? sender, EventArgs e) => OpenAbout_Click(sender, new RoutedEventArgs());

    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is MainWindowViewModel vm)
        {
            vm.UpdateSelection(grid.SelectedItems);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.HasDirtyItems)
        {
            e.Cancel = true;

            var dialog = new PromptWindow();
            await dialog.ShowDialog(this);

            if (dialog.Result == PromptWindow.PromptResult.Save)
            {
                await vm.SaveAllCommand.ExecuteAsync(null);
                if (!vm.SaveFailures.Any())
                {
                    foreach (var item in vm.Comics)
                    {
                        item.IsDirty = false;
                    }
                    Close();
                }
            }
            else if (dialog.Result == PromptWindow.PromptResult.Discard)
            {
                foreach (var item in vm.Comics)
                {
                    item.IsDirty = false;
                }
                Close();
            }
        }
        else
        {
            base.OnClosing(e);
        }
    }
}

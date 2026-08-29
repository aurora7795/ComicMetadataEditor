using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Renaming;
using InkTag.Core.Scrapers;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public partial class BulkScrapeQueueWindow : Window
{
    public bool WasApplied { get; private set; }

    public BulkScrapeQueueWindow() : this(Array.Empty<string>())
    {
    }

    public BulkScrapeQueueWindow(IEnumerable<string> filePaths)
    {
        InitializeComponent();
        DataContext = new BulkScrapeQueueViewModel(filePaths, null, new AppSettingsService());
    }

    private async void StartQueue_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BulkScrapeQueueViewModel vm)
        {
            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            await vm.StartQueueAsync();
        }
    }

    private void CancelQueue_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BulkScrapeQueueViewModel vm)
        {
            vm.CancelQueue();
        }
    }

    private async void ChangeMatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BulkScrapeItemViewModel itemVm)
        {
            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            var dialog = new ScraperMatchWindow(
                itemVm.Item.ExistingComic,
                itemVm.Item.Candidates.Any() ? itemVm.Item.Candidates : null,
                itemVm.LocalThumbnail,
                itemVm.Item.LocalCoverHash != 0 ? itemVm.Item.LocalCoverHash : null,
                itemVm.FilePath,
                isBulkQueueMode: true);

            await dialog.ShowDialog(this);

            if (dialog.WasApplied && dialog.SelectedCandidate != null)
            {
                itemVm.MatchedCandidate = dialog.SelectedCandidate;
                itemVm.Status = BulkScrapeItemStatus.Matched;
                itemVm.StatusMessage = $"Manually matched: {dialog.SelectedCandidate.SeriesTitle} #{dialog.SelectedCandidate.IssueNumber}";
                itemVm.IsSelected = true;
                
                if (DataContext is BulkScrapeQueueViewModel vm)
                {
                    vm.UpdateCounts();

                    if (dialog.ApplySeriesToRemainingUnmatched && dialog.ChosenSeries != null)
                    {
                        _ = vm.RematchUnmatchedWithSeriesAsync(dialog.ChosenSeries);
                    }
                }
            }
        }
    }

    private async void ApplyMatched_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BulkScrapeQueueViewModel vm)
        {
            if (vm.IsAllDone)
            {
                Close();
                return;
            }

            var targetItems = vm.Items
                .Where(i => i.IsSelected && (i.Status == BulkScrapeItemStatus.Matched || i.Status == BulkScrapeItemStatus.LowConfidence) && i.MatchedCandidate != null)
                .ToList();

            if (targetItems.Count == 0) return;

            var cbrItems = targetItems.Where(i => i.IsCbr).ToList();
            bool shouldConfirmCbr = cbrItems.Count > 0 && vm.SettingsService.Settings.ConfirmCbrToCbzConversion;
            bool shouldConfirmRename = vm.AlsoRenameFiles;

            if (shouldConfirmCbr || shouldConfirmRename)
            {
                string chosenTemplate = (vm.SelectedRenameTemplateIndex >= 0 && vm.SelectedRenameTemplateIndex < vm.RenameTemplates.Count)
                    ? vm.RenameTemplates[vm.SelectedRenameTemplateIndex]
                    : ComicFileRenamer.DefaultTemplate;

                var confirmItems = targetItems.Select(item =>
                {
                    string targetName = item.Filename;
                    bool isRenamed = false;
                    if (vm.AlsoRenameFiles && item.MatchedCandidate != null)
                    {
                        var simulatedComic = new ComicInfo
                        {
                            Series = item.MatchedCandidate.SeriesTitle,
                            Number = item.MatchedCandidate.IssueNumber,
                            Title = item.MatchedCandidate.IssueTitle,
                            Year = item.MatchedCandidate.VolumeStartYear ?? item.Item.ParsedQuery.Year
                        };
                        targetName = ComicFileRenamer.GenerateFilename(simulatedComic, item.FilePath, chosenTemplate, preserveScanInfo: false);
                        isRenamed = !string.Equals(item.Filename, targetName, StringComparison.Ordinal);
                    }
                    else if (item.IsCbr)
                    {
                        targetName = Path.ChangeExtension(item.Filename, ".cbz");
                    }

                    return new BulkApplyConfirmItem
                    {
                        OriginalFilename = item.Filename,
                        TargetFilename = targetName,
                        IsCbrConversion = item.IsCbr,
                        IsRenamed = isRenamed
                    };
                }).ToList();

                var confirmDialog = new BulkApplyConfirmWindow(confirmItems, cbrItems.Count, shouldConfirmRename, chosenTemplate);
                await confirmDialog.ShowDialog(this);

                if (!confirmDialog.Confirmed)
                {
                    return;
                }

                if (confirmDialog.DoNotAskAgainCbr)
                {
                    vm.SettingsService.Settings.ConfirmCbrToCbzConversion = false;
                    vm.SettingsService.SaveSettings();
                }
            }

            int savedCount = await vm.ApplyMatchedAsync();
            if (savedCount > 0)
            {
                WasApplied = true;
            }
        }
    }

    private void ItemCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is BulkScrapeItemViewModel clickedItem)
        {
            bool isChecked = cb.IsChecked ?? false;
            var selectedRows = QueueDataGrid.SelectedItems?.OfType<BulkScrapeItemViewModel>().ToList();

            // If multiple rows are highlighted/selected and the user clicked one of them,
            // propagate the checked state to all highlighted rows
            if (selectedRows != null && selectedRows.Count > 1 && selectedRows.Contains(clickedItem))
            {
                foreach (var row in selectedRows)
                {
                    row.IsSelected = isChecked;
                }
            }

            if (DataContext is BulkScrapeQueueViewModel vm)
            {
                vm.UpdateCounts();
            }
        }
    }

    private void QueueDataGrid_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Space)
        {
            var selectedRows = QueueDataGrid.SelectedItems?.OfType<BulkScrapeItemViewModel>().ToList();
            if (selectedRows != null && selectedRows.Count > 0)
            {
                // Toggle state of all selected rows based on the inverted state of the first item
                bool newState = !selectedRows[0].IsSelected;
                foreach (var row in selectedRows)
                {
                    row.IsSelected = newState;
                }
                e.Handled = true;

                if (DataContext is BulkScrapeQueueViewModel vm)
                {
                    vm.UpdateCounts();
                }
            }
        }
    }

    private void IncludeSelected_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedRowsCheckedState(true);
    }

    private void ExcludeSelected_Click(object? sender, RoutedEventArgs e)
    {
        SetSelectedRowsCheckedState(false);
    }

    private void ToggleSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selectedRows = QueueDataGrid.SelectedItems?.OfType<BulkScrapeItemViewModel>().ToList();
        if (selectedRows != null && selectedRows.Count > 0)
        {
            bool newState = !selectedRows[0].IsSelected;
            foreach (var row in selectedRows)
            {
                row.IsSelected = newState;
            }
            if (DataContext is BulkScrapeQueueViewModel vm)
            {
                vm.UpdateCounts();
            }
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BulkScrapeQueueViewModel vm)
        {
            vm.SelectAll = true;
        }
    }

    private void DeselectAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BulkScrapeQueueViewModel vm)
        {
            vm.SelectAll = false;
        }
    }

    private void SetSelectedRowsCheckedState(bool isChecked)
    {
        var selectedRows = QueueDataGrid.SelectedItems?.OfType<BulkScrapeItemViewModel>().ToList();
        if (selectedRows != null && selectedRows.Count > 0)
        {
            foreach (var row in selectedRows)
            {
                row.IsSelected = isChecked;
            }
            if (DataContext is BulkScrapeQueueViewModel vm)
            {
                vm.UpdateCounts();
            }
        }
    }

    private async void ContextTweakMatch_Click(object? sender, RoutedEventArgs e)
    {
        if (QueueDataGrid.SelectedItem is BulkScrapeItemViewModel itemVm)
        {
            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            var dialog = new ScraperMatchWindow(
                itemVm.Item.ExistingComic,
                itemVm.Item.Candidates.Any() ? itemVm.Item.Candidates : null,
                itemVm.LocalThumbnail,
                itemVm.Item.LocalCoverHash != 0 ? itemVm.Item.LocalCoverHash : null,
                itemVm.FilePath,
                isBulkQueueMode: true);

            await dialog.ShowDialog(this);

            if (dialog.WasApplied && dialog.SelectedCandidate != null)
            {
                itemVm.MatchedCandidate = dialog.SelectedCandidate;
                itemVm.Status = BulkScrapeItemStatus.Matched;
                itemVm.StatusMessage = $"Manually matched: {dialog.SelectedCandidate.SeriesTitle} #{dialog.SelectedCandidate.IssueNumber}";
                itemVm.IsSelected = true;

                if (DataContext is BulkScrapeQueueViewModel vm)
                {
                    vm.UpdateCounts();

                    if (dialog.ApplySeriesToRemainingUnmatched && dialog.ChosenSeries != null)
                    {
                        _ = vm.RematchUnmatchedWithSeriesAsync(dialog.ChosenSeries);
                    }
                }
            }
        }
    }

    private async void ContextSeriesWizard_Click(object? sender, RoutedEventArgs e)
    {
        if (QueueDataGrid.SelectedItem is BulkScrapeItemViewModel itemVm)
        {
            if (!await ApiKeyRequiredWindow.EnsureApiKeyConfiguredAsync(this))
            {
                return;
            }

            string initialQuery = !string.IsNullOrWhiteSpace(itemVm.ParsedSeries) ? itemVm.ParsedSeries : (itemVm.Item.ExistingComic?.Series ?? "");
            var wizard = new SeriesSearchWizardWindow(initialQuery, itemVm.Item.LocalCoverHash != 0 ? itemVm.Item.LocalCoverHash : null, itemVm.FilePath, isBulkQueueMode: true);
            await wizard.ShowDialog(this);

            if (wizard.WasApplied && wizard.SelectedResult != null)
            {
                itemVm.MatchedCandidate = wizard.SelectedResult;
                itemVm.Status = BulkScrapeItemStatus.Matched;
                itemVm.StatusMessage = $"Manually matched: {wizard.SelectedResult.SeriesTitle} #{wizard.SelectedResult.IssueNumber}";
                itemVm.IsSelected = true;

                if (DataContext is BulkScrapeQueueViewModel vm)
                {
                    vm.UpdateCounts();

                    if (wizard.ApplySeriesToRemainingUnmatched && wizard.ChosenSeries != null)
                    {
                        _ = vm.RematchUnmatchedWithSeriesAsync(wizard.ChosenSeries);
                    }
                }
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

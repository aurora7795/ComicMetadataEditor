using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core.Configuration;
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
                itemVm.FilePath);

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
                itemVm.FilePath);

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
                }
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

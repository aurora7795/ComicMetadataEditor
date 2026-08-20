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
            int savedCount = await vm.ApplyMatchedAsync();
            if (savedCount > 0)
            {
                WasApplied = true;
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

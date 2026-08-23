using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace InkTag.Gui.Views;

public class BulkApplyConfirmItem
{
    public string OriginalFilename { get; set; } = string.Empty;
    public string TargetFilename { get; set; } = string.Empty;
    public bool IsCbrConversion { get; set; }
    public bool IsRenamed { get; set; }
    public bool HasNewName => IsRenamed || IsCbrConversion;
}

public partial class BulkApplyConfirmWindow : Window
{
    public bool Confirmed { get; private set; }
    public bool DoNotAskAgainCbr => DoNotAskAgainCheckBox?.IsChecked ?? false;

    public BulkApplyConfirmWindow()
    {
        InitializeComponent();
    }

    public BulkApplyConfirmWindow(
        List<BulkApplyConfirmItem> items,
        int cbrCount,
        bool isRenameEnabled,
        string renameTemplate) : this()
    {
        FileListItemsControl.ItemsSource = items;

        if (cbrCount > 0)
        {
            CbrNoticeBanner.IsVisible = true;
            CbrNoticeText.Text = $"{cbrCount} CBR archive(s) will be automatically repacked into standard CBZ (ZIP) files because RAR archives cannot be edited directly.";
            DoNotAskAgainCheckBox.IsVisible = true;
        }

        if (isRenameEnabled)
        {
            RenameNoticeBanner.IsVisible = true;
            RenameNoticeText.Text = $"File renaming is enabled ({items.Count} files). Template: '{renameTemplate}'";
        }

        ProceedButton.Content = $"💾 Proceed with Save ({items.Count} files)";
    }

    private void Proceed_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}

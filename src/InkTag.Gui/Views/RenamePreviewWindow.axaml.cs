using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Core;
using InkTag.Core.Renaming;
using InkTag.Gui.ViewModels;

namespace InkTag.Gui.Views;

public partial class RenamePreviewWindow : Window
{
    public bool WasApplied { get; private set; }
    public RenameBatchResult? Result { get; private set; }

    public RenamePreviewWindow()
    {
        InitializeComponent();
    }

    public RenamePreviewWindow(IEnumerable<(string FilePath, ComicInfo Comic)> items) : this()
    {
        DataContext = new RenamePreviewViewModel(items);
    }

    private async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RenamePreviewViewModel vm)
        {
            var result = await vm.ExecuteRenameAsync();
            if (result.Renamed > 0)
            {
                WasApplied = true;
                Result = result;
                Close();
            }
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

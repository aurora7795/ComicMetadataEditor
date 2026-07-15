using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaApp.ViewModels;

namespace AvaloniaApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.SaveFinishedWithErrors += OnSaveFinishedWithErrors;
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
            // Cancel closing immediately to await the dialog window asynchronously
            e.Cancel = true;

            var dialog = new PromptWindow();
            await dialog.ShowDialog(this);

            if (dialog.Result == PromptWindow.PromptResult.Save)
            {
                await vm.SaveAllCommand.ExecuteAsync(null);
                // If saved successfully with no remaining failures, proceed to close
                if (!vm.SaveFailures.Any())
                {
                    // Mark all clean to prevent entering this block again
                    foreach (var item in vm.Comics)
                    {
                        item.IsDirty = false;
                    }
                    Close();
                }
            }
            else if (dialog.Result == PromptWindow.PromptResult.Discard)
            {
                // Mark all clean and close
                foreach (var item in vm.Comics)
                {
                    item.IsDirty = false;
                }
                Close();
            }
            // If PromptResult is Cancel, do nothing (closing remains cancelled)
        }
        else
        {
            base.OnClosing(e);
        }
    }
}

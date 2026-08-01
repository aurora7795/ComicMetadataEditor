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

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        ComicsGrid.SelectAll();
    }

    private void ClearSelection_Click(object? sender, RoutedEventArgs e)
    {
        ComicsGrid.SelectedItems.Clear();
    }

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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace InkTag.Gui.Views;

public partial class CbrConversionConfirmWindow : Window
{
    public bool Confirmed { get; private set; }
    public bool DoNotAskAgain => DoNotAskAgainCheckBox?.IsChecked ?? false;

    public CbrConversionConfirmWindow()
    {
        InitializeComponent();
    }

    public CbrConversionConfirmWindow(IEnumerable<string> fileNames) : this()
    {
        FileListItemsControl.ItemsSource = fileNames;
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

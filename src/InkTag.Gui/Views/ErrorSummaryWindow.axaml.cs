using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace InkTag.Gui.Views;

public class SaveErrorItem
{
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public partial class ErrorSummaryWindow : Window
{
    public ErrorSummaryWindow()
    {
        InitializeComponent();
    }

    public ErrorSummaryWindow(List<(string Path, Exception Exception)> errors) : this()
    {
        var errorsListBox = this.FindControl<ListBox>("ErrorsListBox");
        if (errorsListBox != null)
        {
            var items = new List<SaveErrorItem>();
            foreach (var err in errors)
            {
                items.Add(new SaveErrorItem
                {
                    Path = Path.GetFileName(err.Path),
                    Message = err.Exception.Message
                });
            }
            errorsListBox.ItemsSource = items;
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace InkTag.Gui.Views;

public partial class ThirdPartyLicensesWindow : Window
{
    public ThirdPartyLicensesWindow()
    {
        InitializeComponent();
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

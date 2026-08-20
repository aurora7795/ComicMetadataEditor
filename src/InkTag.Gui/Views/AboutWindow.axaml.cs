using Avalonia.Controls;
using Avalonia.Interactivity;
using InkTag.Gui.Services;

namespace InkTag.Gui.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Version {UpdateService.CurrentAppVersion.ToString(3)}";
    }

    private void OpenGitHubRepoClick(object? sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrlInBrowser("https://github.com/aurora7795/InkTag");
    }

    private void OpenLicenseClick(object? sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrlInBrowser("https://github.com/aurora7795/InkTag/blob/main/LICENSE");
    }

    private void OpenContributorsClick(object? sender, RoutedEventArgs e)
    {
        UpdateService.OpenUrlInBrowser("https://github.com/aurora7795/InkTag/graphs/contributors");
    }

    private void OpenThirdPartyLicensesClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ThirdPartyLicensesWindow();
        dialog.ShowDialog(this);
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

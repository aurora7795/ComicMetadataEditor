using Avalonia.Controls;
using Avalonia.Interactivity;

namespace InkTag.Gui.Views;

public partial class PromptWindow : Window
{
    public enum PromptResult { Save, Discard, Cancel }
    public PromptResult Result { get; private set; } = PromptResult.Cancel;

    public PromptWindow()
    {
        InitializeComponent();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Result = PromptResult.Save;
        Close();
    }

    private void DiscardClick(object sender, RoutedEventArgs e)
    {
        Result = PromptResult.Discard;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        Result = PromptResult.Cancel;
        Close();
    }
}

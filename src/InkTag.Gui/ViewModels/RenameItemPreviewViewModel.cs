using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Renaming;

namespace InkTag.Gui.ViewModels;

public class RenameItemPreviewViewModel : ObservableObject
{
    public RenameItemPreview Preview { get; }

    public string OriginalFilename => Preview.OriginalFilename;
    public string ProposedFilename => Preview.ProposedFilename;
    public string OriginalFilePath => Preview.OriginalFilePath;
    public string ProposedFilePath => Preview.ProposedFilePath;
    public bool HasChange => Preview.HasChange;
    public bool HasCollision => Preview.HasCollision;
    public string? ErrorMessage => Preview.ErrorMessage;

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StatusText
    {
        get
        {
            if (HasCollision) return "Collision";
            if (!HasChange) return "Unchanged";
            return "Ready";
        }
    }

    public IBrush StatusBadgeBackground
    {
        get
        {
            if (HasCollision) return new SolidColorBrush(Color.Parse("#A80000")); // Red
            if (!HasChange) return new SolidColorBrush(Color.Parse("#3F3F46")); // Gray
            return new SolidColorBrush(Color.Parse("#107C41")); // Green
        }
    }

    public IBrush ProposedFilenameForeground
    {
        get
        {
            if (HasCollision) return new SolidColorBrush(Color.Parse("#FF6B6B"));
            if (!HasChange) return new SolidColorBrush(Color.Parse("#888888"));
            return new SolidColorBrush(Color.Parse("#4EC9B0")); // Teal
        }
    }

    public RenameItemPreviewViewModel(RenameItemPreview preview)
    {
        Preview = preview;
        _isSelected = preview.HasChange && !preview.HasCollision;
    }
}

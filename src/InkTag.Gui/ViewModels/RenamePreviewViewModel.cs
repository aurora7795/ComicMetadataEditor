using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core;
using InkTag.Core.Renaming;

namespace InkTag.Gui.ViewModels;

public class RenamePreviewViewModel : ObservableObject
{
    private readonly List<(string FilePath, ComicInfo Comic)> _sourceItems;

    public ObservableCollection<RenameItemPreviewViewModel> Items { get; } = new();

    public string[] TemplatePresets { get; } = new[]
    {
        "{Series} #{Number:3} ({Year})",
        "{Series} #{Number:3} - {Title} ({Year})",
        "{Series} #{Number:3} ({Year}) {ScanInfo}",
        "{Series} {Number:3} ({Year})",
        "{Publisher} - {Series} v{Volume} #{Number:3} ({Year})",
        "Custom Template..."
    };

    private int _selectedPresetIndex = 0;
    public int SelectedPresetIndex
    {
        get => _selectedPresetIndex;
        set
        {
            if (SetProperty(ref _selectedPresetIndex, value))
            {
                if (value >= 0 && value < TemplatePresets.Length - 1)
                {
                    TemplatePattern = TemplatePresets[value];
                }
                OnPropertyChanged(nameof(IsCustomPattern));
            }
        }
    }

    public bool IsCustomPattern => SelectedPresetIndex == TemplatePresets.Length - 1;

    private string _templatePattern = ComicFileRenamer.DefaultTemplate;
    public string TemplatePattern
    {
        get => _templatePattern;
        set
        {
            if (SetProperty(ref _templatePattern, value))
            {
                GeneratePreviews();
            }
        }
    }

    private bool _preserveScanInfo = false;
    public bool PreserveScanInfo
    {
        get => _preserveScanInfo;
        set
        {
            if (SetProperty(ref _preserveScanInfo, value))
            {
                GeneratePreviews();
            }
        }
    }

    private int _renameCount;
    public int RenameCount
    {
        get => _renameCount;
        set => SetProperty(ref _renameCount, value);
    }

    private int _unchangedCount;
    public int UnchangedCount
    {
        get => _unchangedCount;
        set => SetProperty(ref _unchangedCount, value);
    }

    private int _collisionCount;
    public int CollisionCount
    {
        get => _collisionCount;
        set => SetProperty(ref _collisionCount, value);
    }

    private string _statusMessage = "Ready to rename";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanExecuteRename => Items.Any(i => i.IsSelected && i.HasChange && !i.HasCollision);

    public RenamePreviewViewModel(IEnumerable<(string FilePath, ComicInfo Comic)> items)
    {
        _sourceItems = items.ToList();
        GeneratePreviews();
    }

    public void GeneratePreviews()
    {
        Items.Clear();

        var previews = ComicFileRenamer.PreviewBatchRename(_sourceItems, TemplatePattern, PreserveScanInfo);
        foreach (var p in previews)
        {
            var vm = new RenameItemPreviewViewModel(p);
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(RenameItemPreviewViewModel.IsSelected))
                {
                    UpdateCounts();
                }
            };
            Items.Add(vm);
        }

        UpdateCounts();
    }

    public void UpdateCounts()
    {
        RenameCount = Items.Count(i => i.IsSelected && i.HasChange && !i.HasCollision);
        UnchangedCount = Items.Count(i => !i.HasChange);
        CollisionCount = Items.Count(i => i.HasCollision);
        OnPropertyChanged(nameof(CanExecuteRename));
        StatusMessage = $"{RenameCount} file(s) to rename, {UnchangedCount} unchanged, {CollisionCount} collision(s).";
    }

    public async Task<RenameBatchResult> ExecuteRenameAsync()
    {
        var targetPreviews = Items.Where(i => i.IsSelected).Select(i => i.Preview).ToList();

        return await Task.Run(() =>
        {
            return ComicFileRenamer.ExecuteBatchRename(targetPreviews);
        });
    }
}

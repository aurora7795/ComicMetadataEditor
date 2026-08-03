using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InkTag.Gui.ViewModels;

public partial class BulkEditRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private BulkEditFieldInfo _selectedField;

    [ObservableProperty]
    private BulkEditOperation _selectedOperation = BulkEditOperation.Set;

    [ObservableProperty]
    private string _stringValue = string.Empty;

    [ObservableProperty]
    private int? _numericValue;

    [ObservableProperty]
    private string? _selectedEnumOption;

    [ObservableProperty]
    private string _findValue = string.Empty;

    [ObservableProperty]
    private string _replaceValue = string.Empty;

    public IReadOnlyList<BulkEditFieldInfo> AvailableFields => BulkEditCatalog.AllFields;

    public IEnumerable<BulkEditOperation> AvailableOperations
    {
        get
        {
            if (SelectedField == null) return Enum.GetValues<BulkEditOperation>();

            return SelectedField.DataType switch
            {
                BulkEditFieldDataType.String => new[] { BulkEditOperation.Set, BulkEditOperation.Clear, BulkEditOperation.Append, BulkEditOperation.Prepend, BulkEditOperation.Replace },
                BulkEditFieldDataType.Numeric => new[] { BulkEditOperation.Set, BulkEditOperation.Clear },
                BulkEditFieldDataType.Enum => new[] { BulkEditOperation.Set, BulkEditOperation.Clear },
                _ => new[] { BulkEditOperation.Set, BulkEditOperation.Clear }
            };
        }
    }

    public string[] EnumOptions => SelectedField?.EnumOptions ?? Array.Empty<string>();

    public bool IsTextEditorVisible => SelectedField?.DataType == BulkEditFieldDataType.String && SelectedOperation != BulkEditOperation.Clear && SelectedOperation != BulkEditOperation.Replace;

    public bool IsNumericEditorVisible => SelectedField?.DataType == BulkEditFieldDataType.Numeric && SelectedOperation != BulkEditOperation.Clear;

    public bool IsEnumEditorVisible => SelectedField?.DataType == BulkEditFieldDataType.Enum && SelectedOperation != BulkEditOperation.Clear;

    public bool IsReplaceEditorVisible => SelectedField?.DataType == BulkEditFieldDataType.String && SelectedOperation == BulkEditOperation.Replace;

    public BulkEditRuleViewModel(BulkEditFieldInfo? initialField = null)
    {
        _selectedField = initialField ?? BulkEditCatalog.AllFields[0];
        if (_selectedField.EnumOptions?.Length > 0)
        {
            _selectedEnumOption = _selectedField.EnumOptions[0];
        }
    }

    partial void OnSelectedFieldChanged(BulkEditFieldInfo value)
    {
        OnPropertyChanged(nameof(AvailableOperations));
        if (!AvailableOperations.Contains(SelectedOperation))
        {
            SelectedOperation = AvailableOperations.First();
        }

        if (value.EnumOptions?.Length > 0 && (SelectedEnumOption == null || !value.EnumOptions.Contains(SelectedEnumOption)))
        {
            SelectedEnumOption = value.EnumOptions[0];
        }

        NotifyVisibilityChanges();
    }

    partial void OnSelectedOperationChanged(BulkEditOperation value)
    {
        NotifyVisibilityChanges();
    }

    private void NotifyVisibilityChanges()
    {
        OnPropertyChanged(nameof(EnumOptions));
        OnPropertyChanged(nameof(IsTextEditorVisible));
        OnPropertyChanged(nameof(IsNumericEditorVisible));
        OnPropertyChanged(nameof(IsEnumEditorVisible));
        OnPropertyChanged(nameof(IsReplaceEditorVisible));
    }
}

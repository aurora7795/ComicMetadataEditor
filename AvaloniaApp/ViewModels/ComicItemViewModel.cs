using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ComicMetadataEditor;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaApp.Services;

namespace AvaloniaApp.ViewModels;

public partial class ComicItemViewModel : ObservableValidator
{
    private readonly ComicInfo _model;
    private bool _isInitializing;

    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private string? _series;

    [ObservableProperty]
    private string? _number;

    [ObservableProperty]
    [Range(0, int.MaxValue, ErrorMessage = "Volume must be a positive integer")]
    private int? _volume;

    [ObservableProperty]
    private string? _publisher;

    [ObservableProperty]
    [Range(1000, 9999, ErrorMessage = "Year must be a 4-digit number")]
    private int? _year;

    [ObservableProperty]
    [Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]
    private int? _month;

    [ObservableProperty]
    private string? _genre;

    [ObservableProperty]
    private string? _tags;

    [ObservableProperty]
    private string? _writer;

    [ObservableProperty]
    private string? _languageISO;

    [ObservableProperty]
    private bool _manga;

    public ComicItemViewModel(string filePath, ComicInfo model)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        _model = model;

        LoadFromModel();
    }

    public void LoadFromModel()
    {
        _isInitializing = true;
        try
        {
            Title = _model.Title;
            Series = _model.Series;
            Number = _model.Number;
            Volume = _model.Volume;
            Publisher = _model.Publisher;
            Year = _model.Year;
            Month = _model.Month;
            Genre = _model.Genre;
            Tags = _model.Tags;
            Writer = _model.Writer;
            LanguageISO = _model.LanguageISO;
            Manga = _model.Manga == "Yes";

            ValidateAllProperties();
            IsDirty = false;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public void ApplyChangesToModel()
    {
        ApplyChangesToModel(_model);
    }

    public void ApplyChangesToModel(ComicInfo target)
    {
        target.Title = Title;
        target.Series = Series;
        target.Number = Number;
        target.Volume = Volume;
        target.Publisher = Publisher;
        target.Year = Year;
        target.Month = Month;
        target.Genre = Genre;
        target.Tags = Tags;
        target.Writer = Writer;
        target.LanguageISO = LanguageISO;
        target.Manga = Manga ? "Yes" : "No";
    }

    public async Task LoadCoverAsync(ArchiveCoverService coverService, CancellationToken cancellationToken)
    {
        if (CoverImage != null) return;
        var bitmap = await coverService.LoadCoverAsync(FilePath, cancellationToken);
        if (bitmap != null)
        {
            CoverImage = bitmap;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!_isInitializing && 
            e.PropertyName != nameof(IsDirty) && 
            e.PropertyName != nameof(CoverImage) && 
            e.PropertyName != nameof(HasErrors))
        {
            IsDirty = true;
        }
    }
}

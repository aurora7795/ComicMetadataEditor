using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using InkTag.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Gui.Services;

namespace InkTag.Gui.ViewModels;

public partial class ComicItemViewModel : ObservableValidator
{
    private ComicInfo _model;
    private bool _isInitializing;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _hasReadError;

    [ObservableProperty]
    private string? _readErrorMessage;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private ulong _coverHash;

    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private string? _series;

    [ObservableProperty]
    private string? _number;

    [ObservableProperty]
    private int? _count;

    [ObservableProperty]
    [Range(0, int.MaxValue, ErrorMessage = "Volume must be a positive integer")]
    private int? _volume;

    [ObservableProperty]
    private string? _publisher;

    [ObservableProperty]
    private string? _imprint;

    [ObservableProperty]
    [Range(1000, 9999, ErrorMessage = "Year must be a 4-digit number")]
    private int? _year;

    [ObservableProperty]
    [Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]
    private int? _month;

    [ObservableProperty]
    [Range(1, 31, ErrorMessage = "Day must be between 1 and 31")]
    private int? _day;

    [ObservableProperty]
    private string? _genre;

    [ObservableProperty]
    private string? _tags;

    [ObservableProperty]
    private string? _writer;

    [ObservableProperty]
    private string? _penciller;

    [ObservableProperty]
    private string? _inker;

    [ObservableProperty]
    private string? _colorist;

    [ObservableProperty]
    private string? _letterer;

    [ObservableProperty]
    private string? _coverArtist;

    [ObservableProperty]
    private string? _editor;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string? _languageISO;

    [ObservableProperty]
    private string? _format;

    [ObservableProperty]
    private string? _blackAndWhite;

    [ObservableProperty]
    private MangaDirection? _mangaDirection;

    [ObservableProperty]
    private bool _manga;

    [ObservableProperty]
    private string? _characters;

    [ObservableProperty]
    private string? _teams;

    [ObservableProperty]
    private string? _locations;

    [ObservableProperty]
    private string? _scanInformation;

    [ObservableProperty]
    private string? _storyArc;

    [ObservableProperty]
    private string? _seriesGroup;

    [ObservableProperty]
    private string? _ageRating;

    [ObservableProperty]
    private string? _web;

    [ObservableProperty]
    private int? _pageCount;

    [ObservableProperty]
    private bool _hasEmbeddedXml = true;

    [ObservableProperty]
    private bool _hasLegacyMetadata;

    public bool IsUntagged => !HasEmbeddedXml || !HasEssentialMetadata;
    public bool HasEssentialMetadata => !string.IsNullOrWhiteSpace(Series) || !string.IsNullOrWhiteSpace(Title);

    public ComicItemViewModel(string filePath, ComicInfo model, bool hasEmbeddedXml = true)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        _model = model;
        _hasEmbeddedXml = hasEmbeddedXml;
        _hasLegacyMetadata = model.HasLegacyMetadata;

        LoadFromModel();
    }

    public void UpdateFilePath(string newPath)
    {
        FilePath = newPath;
        FileName = Path.GetFileName(newPath);
    }

    public void LoadFromModel(ComicInfo model)
    {
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
            Count = _model.Count is > 0 ? _model.Count : null;
            Volume = _model.Volume is >= 0 ? _model.Volume : null;
            Summary = _model.Summary;
            Notes = _model.Notes;
            Year = _model.Year is >= 1000 ? _model.Year : null;
            Month = _model.Month is >= 1 and <= 12 ? _model.Month : null;
            Day = _model.Day is >= 1 and <= 31 ? _model.Day : null;
            Writer = _model.Writer;
            Penciller = _model.Penciller;
            Inker = _model.Inker;
            Colorist = _model.Colorist;
            Letterer = _model.Letterer;
            CoverArtist = _model.CoverArtist;
            Editor = _model.Editor;
            Publisher = _model.Publisher;
            Imprint = _model.Imprint;
            Genre = _model.Genre;
            Tags = _model.Tags;
            Web = _model.Web;
            PageCount = _model.PageCount is > 0 ? _model.PageCount : null;
            LanguageISO = _model.LanguageISO;
            Format = _model.Format;
            BlackAndWhite = _model.BlackAndWhite;
            MangaDirection = _model.Manga;
            Manga = _model.Manga == InkTag.Core.MangaDirection.Yes || _model.Manga == InkTag.Core.MangaDirection.YesAndRightToLeft;
            Characters = _model.Characters;
            Teams = _model.Teams;
            Locations = _model.Locations;
            ScanInformation = _model.ScanInformation;
            StoryArc = _model.StoryArc;
            SeriesGroup = _model.SeriesGroup;
            AgeRating = _model.AgeRating;

            ValidateAllProperties();
            HasLegacyMetadata = _model.HasLegacyMetadata;
            IsDirty = _model.HasLegacyMetadata && !HasEmbeddedXml;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public ComicInfo ToModel()
    {
        var model = new ComicInfo();
        ApplyChangesToModel(model);
        return model;
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
        target.Count = Count is > 0 ? Count : null;
        target.Volume = Volume is >= 0 ? Volume : null;
        target.Summary = Summary;
        target.Notes = Notes;
        target.Year = Year is > 0 ? Year : null;
        target.Month = Month is > 0 ? Month : null;
        target.Day = Day is > 0 ? Day : null;
        target.Writer = Writer;
        target.Penciller = Penciller;
        target.Inker = Inker;
        target.Colorist = Colorist;
        target.Letterer = Letterer;
        target.CoverArtist = CoverArtist;
        target.Editor = Editor;
        target.Publisher = Publisher;
        target.Imprint = Imprint;
        target.Genre = Genre;
        target.Tags = Tags;
        target.Web = Web;
        target.PageCount = PageCount is > 0 ? PageCount : null;
        target.LanguageISO = LanguageISO;
        target.Format = Format;
        target.BlackAndWhite = BlackAndWhite;
        target.Characters = Characters;
        target.Teams = Teams;
        target.Locations = Locations;
        target.ScanInformation = ScanInformation;
        target.StoryArc = StoryArc;
        target.SeriesGroup = SeriesGroup;
        target.AgeRating = AgeRating;

        if (MangaDirection.HasValue)
        {
            target.Manga = MangaDirection.Value;
        }
        else if (Manga)
        {
            target.Manga = InkTag.Core.MangaDirection.Yes;
        }
        else
        {
            target.Manga = InkTag.Core.MangaDirection.No;
        }
    }

    public void ApplyBulkRule(BulkEditRuleViewModel rule)
    {
        if (rule?.SelectedField == null) return;
        var field = rule.SelectedField;
        var op = rule.SelectedOperation;

        if (field.PropertyName == "MangaDirection")
        {
            if (op == BulkEditOperation.Clear)
            {
                MangaDirection = InkTag.Core.MangaDirection.Unknown;
                Manga = false;
            }
            else if (op == BulkEditOperation.Set && Enum.TryParse<MangaDirection>(rule.SelectedEnumOption, out var dir))
            {
                MangaDirection = dir;
                Manga = dir == InkTag.Core.MangaDirection.Yes || dir == InkTag.Core.MangaDirection.YesAndRightToLeft;
            }
            return;
        }

        var prop = typeof(ComicItemViewModel).GetProperty(field.PropertyName);
        if (prop == null || !prop.CanWrite) return;

        if (field.DataType == BulkEditFieldDataType.String)
        {
            string? current = prop.GetValue(this) as string;
            switch (op)
            {
                case BulkEditOperation.Set:
                    prop.SetValue(this, rule.StringValue);
                    break;
                case BulkEditOperation.Clear:
                    prop.SetValue(this, null);
                    break;
                case BulkEditOperation.Append:
                    if (string.IsNullOrWhiteSpace(current))
                    {
                        prop.SetValue(this, rule.StringValue);
                    }
                    else
                    {
                        string separator = (field.PropertyName == "Tags" || field.PropertyName == "Genre" || field.PropertyName == "Characters" || field.PropertyName == "Teams" || field.PropertyName == "Locations") ? ", " : " ";
                        prop.SetValue(this, current + separator + rule.StringValue);
                    }
                    break;
                case BulkEditOperation.Prepend:
                    if (string.IsNullOrWhiteSpace(current))
                    {
                        prop.SetValue(this, rule.StringValue);
                    }
                    else
                    {
                        string separator = (field.PropertyName == "Tags" || field.PropertyName == "Genre" || field.PropertyName == "Characters" || field.PropertyName == "Teams" || field.PropertyName == "Locations") ? ", " : " ";
                        prop.SetValue(this, rule.StringValue + separator + current);
                    }
                    break;
                case BulkEditOperation.Replace:
                    if (!string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(rule.FindValue))
                    {
                        prop.SetValue(this, current.Replace(rule.FindValue, rule.ReplaceValue));
                    }
                    break;
            }
        }
        else if (field.DataType == BulkEditFieldDataType.Numeric)
        {
            if (op == BulkEditOperation.Set)
            {
                prop.SetValue(this, rule.NumericValue);
            }
            else if (op == BulkEditOperation.Clear)
            {
                prop.SetValue(this, null);
            }
        }
        else if (field.DataType == BulkEditFieldDataType.Enum)
        {
            if (op == BulkEditOperation.Set)
            {
                prop.SetValue(this, rule.SelectedEnumOption);
            }
            else if (op == BulkEditOperation.Clear)
            {
                prop.SetValue(this, null);
            }
        }
    }

    public async Task LoadCoverAsync(ArchiveCoverService coverService, CancellationToken cancellationToken)
    {
        if (CoverImage != null && CoverHash != 0) return;
        var (bitmap, hash) = await coverService.LoadCoverWithHashAsync(FilePath, cancellationToken);
        if (bitmap != null)
        {
            CoverImage = bitmap;
            CoverHash = hash;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(Title) || e.PropertyName == nameof(Series) || e.PropertyName == nameof(HasEmbeddedXml))
        {
            OnPropertyChanged(nameof(HasEssentialMetadata));
            OnPropertyChanged(nameof(IsUntagged));
        }

        if (!_isInitializing && 
            e.PropertyName != nameof(IsDirty) && 
            e.PropertyName != nameof(CoverImage) && 
            e.PropertyName != nameof(HasErrors) &&
            e.PropertyName != nameof(HasEssentialMetadata) &&
            e.PropertyName != nameof(IsUntagged))
        {
            IsDirty = true;
        }
    }
}

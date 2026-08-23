using System;
using System.Linq;
using System.Xml.Serialization;

namespace InkTag.Core;

/// <summary>
/// Specifies the reading direction for manga comics.
/// </summary>
public enum MangaDirection
{
    Unknown,
    No,
    Yes,
    YesAndRightToLeft
}

/// <summary>
/// Represents the ComicRack/Anansi ComicInfo.xml metadata schema.
/// </summary>
[XmlRoot("ComicInfo")]
public class ComicInfo
{
    /// <summary>The title or story name of the comic issue.</summary>
    [XmlElement("Title")]
    public string? Title { get; set; }

    /// <summary>The series or book title.</summary>
    [XmlElement("Series")]
    public string? Series { get; set; }

    /// <summary>The issue number (can be non-integer like 'Annual 1' or '0.5').</summary>
    [XmlElement("Number")]
    public string? Number { get; set; }

    /// <summary>The total number of issues in the series or limited run.</summary>
    [XmlElement("Count")]
    public int? Count { get; set; }
    public bool ShouldSerializeCount() => Count.HasValue;

    /// <summary>The volume number of the series (often represented as publishing year or sequential volume).</summary>
    [XmlElement("Volume")]
    public int? Volume { get; set; }
    public bool ShouldSerializeVolume() => Volume.HasValue;

    /// <summary>An alternate series title.</summary>
    [XmlElement("AlternateSeries")]
    public string? AlternateSeries { get; set; }

    /// <summary>An alternate issue number.</summary>
    [XmlElement("AlternateNumber")]
    public string? AlternateNumber { get; set; }

    /// <summary>An alternate issue count.</summary>
    [XmlElement("AlternateCount")]
    public int? AlternateCount { get; set; }
    public bool ShouldSerializeAlternateCount() => AlternateCount.HasValue;

    /// <summary>A plot synopsis or summary of the issue.</summary>
    [XmlElement("Summary")]
    public string? Summary { get; set; }

    /// <summary>Free-form notes, curation details, or provenance info.</summary>
    [XmlElement("Notes")]
    public string? Notes { get; set; }

    /// <summary>The publication year.</summary>
    [XmlElement("Year")]
    public int? Year { get; set; }
    public bool ShouldSerializeYear() => Year.HasValue;

    /// <summary>The publication month (1-12).</summary>
    [XmlElement("Month")]
    public int? Month { get; set; }
    public bool ShouldSerializeMonth() => Month.HasValue;

    /// <summary>The publication day (1-31).</summary>
    [XmlElement("Day")]
    public int? Day { get; set; }
    public bool ShouldSerializeDay() => Day.HasValue;

    /// <summary>The writer(s) credited for the comic.</summary>
    [XmlElement("Writer")]
    public string? Writer { get; set; }

    /// <summary>The penciller(s) credited for the comic.</summary>
    [XmlElement("Penciller")]
    public string? Penciller { get; set; }

    /// <summary>The inker(s) credited for the comic.</summary>
    [XmlElement("Inker")]
    public string? Inker { get; set; }

    /// <summary>The colorist(s) credited for the comic.</summary>
    [XmlElement("Colorist")]
    public string? Colorist { get; set; }

    /// <summary>The letterer(s) credited for the comic.</summary>
    [XmlElement("Letterer")]
    public string? Letterer { get; set; }

    /// <summary>The cover artist(s) credited for the comic.</summary>
    [XmlElement("CoverArtist")]
    public string? CoverArtist { get; set; }

    /// <summary>The editor(s) credited for the comic.</summary>
    [XmlElement("Editor")]
    public string? Editor { get; set; }

    /// <summary>The publisher company or label.</summary>
    [XmlElement("Publisher")]
    public string? Publisher { get; set; }

    /// <summary>The publishing imprint under the publisher.</summary>
    [XmlElement("Imprint")]
    public string? Imprint { get; set; }

    /// <summary>The genre(s) of the comic (e.g. Superhero, Sci-Fi).</summary>
    [XmlElement("Genre")]
    public string? Genre { get; set; }

    /// <summary>Comma-separated descriptive tags or keywords.</summary>
    [XmlElement("Tags")]
    public string? Tags { get; set; }

    /// <summary>Web URL or source reference link.</summary>
    [XmlElement("Web")]
    public string? Web { get; set; }

    /// <summary>The total number of pages in the comic.</summary>
    [XmlElement("PageCount")]
    public int? PageCount { get; set; }
    public bool ShouldSerializePageCount() => PageCount.HasValue;

    /// <summary>ISO language code (e.g., 'en', 'fr', 'ja').</summary>
    [XmlElement("LanguageISO")]
    public string? LanguageISO { get; set; }

    /// <summary>Publication format (e.g., 'Trade Paperback', 'Single Issue', 'Graphic Novel').</summary>
    [XmlElement("Format")]
    public string? Format { get; set; }

    /// <summary>Indicates whether the comic is black and white ('Yes' or 'No').</summary>
    [XmlElement("BlackAndWhite")]
    public string? BlackAndWhite { get; set; }

    /// <summary>Manga reading direction indicator.</summary>
    [XmlElement("Manga")]
    public MangaDirection? Manga { get; set; }
    public bool ShouldSerializeManga() => Manga.HasValue;

    /// <summary>Comma-separated list of characters appearing in the comic.</summary>
    [XmlElement("Characters")]
    public string? Characters { get; set; }

    /// <summary>Comma-separated list of teams or factions appearing in the comic.</summary>
    [XmlElement("Teams")]
    public string? Teams { get; set; }

    /// <summary>Comma-separated list of locations featured in the story.</summary>
    [XmlElement("Locations")]
    public string? Locations { get; set; }

    /// <summary>Digital scanner, ripper, or provenance information.</summary>
    [XmlElement("ScanInformation")]
    public string? ScanInformation { get; set; }

    /// <summary>The story arc or crossover event name.</summary>
    [XmlElement("StoryArc")]
    public string? StoryArc { get; set; }

    /// <summary>The series group or franchise bucket.</summary>
    [XmlElement("SeriesGroup")]
    public string? SeriesGroup { get; set; }

    /// <summary>Age or content rating (e.g. 'Teen+', 'Mature 17+').</summary>
    [XmlElement("AgeRating")]
    public string? AgeRating { get; set; }

    /// <summary>Community rating score (0.0 to 5.0).</summary>
    [XmlElement("CommunityRating")]
    public decimal? CommunityRating { get; set; }
    public bool ShouldSerializeCommunityRating() => CommunityRating.HasValue;

    /// <summary>Primary protagonist character or central team.</summary>
    [XmlElement("MainCharacterOrTeam")]
    public string? MainCharacterOrTeam { get; set; }

    /// <summary>Editorial or critic review text.</summary>
    [XmlElement("Review")]
    public string? Review { get; set; }

    /// <summary>Individual page metadata descriptors.</summary>
    [XmlElement("Pages")]
    public PageCollection? Pages { get; set; }

    /// <summary>Returns true if the issue has at least Series or Title populated.</summary>
    [XmlIgnore]
    public bool HasEssentialMetadata => !string.IsNullOrWhiteSpace(Series) || !string.IsNullOrWhiteSpace(Title);

    /// <summary>Indicates if metadata was merged or detected from legacy ComicBookInfo.</summary>
    [XmlIgnore]
    public bool HasLegacyMetadata { get; set; }

    /// <summary>Returns true if any major metadata field is populated.</summary>
    [XmlIgnore]
    public bool HasAnyMetadata =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Series) ||
        !string.IsNullOrWhiteSpace(Number) ||
        !string.IsNullOrWhiteSpace(Summary) ||
        !string.IsNullOrWhiteSpace(Writer) ||
        !string.IsNullOrWhiteSpace(Publisher) ||
        !string.IsNullOrWhiteSpace(Genre) ||
        !string.IsNullOrWhiteSpace(Tags) ||
        Year.HasValue || Volume.HasValue || Count.HasValue;

    /// <summary>
    /// Creates a deep copy of this ComicInfo object.
    /// </summary>
    public ComicInfo Clone()
    {
        var copy = (ComicInfo)MemberwiseClone();
        if (Pages?.Page != null)
        {
            copy.Pages = new PageCollection
            {
                Page = Pages.Page.Select(p => new Page
                {
                    Image = p.Image,
                    Type = p.Type,
                    DoublePage = p.DoublePage,
                    ImageSize = p.ImageSize,
                    Key = p.Key,
                    Bookmark = p.Bookmark,
                    ImageWidth = p.ImageWidth,
                    ImageHeight = p.ImageHeight
                }).ToArray()
            };
        }
        return copy;
    }
}

/// <summary>
/// Encapsulates a collection of comic pages.
/// </summary>
public class PageCollection
{
    [XmlElement("Page")]
    public Page[]? Page { get; set; }
}

/// <summary>
/// Represents individual comic page metadata.
/// </summary>
public class Page
{
    [XmlAttribute("Image")]
    public int Image { get; set; }

    [XmlAttribute("Type")]
    public string? Type { get; set; }

    [XmlIgnore]
    public bool? DoublePage { get; set; }
    [XmlAttribute("DoublePage")]
    public bool DoublePageValue
    {
        get => DoublePage ?? false;
        set => DoublePage = value;
    }
    public bool ShouldSerializeDoublePageValue() => DoublePage.HasValue;

    [XmlIgnore]
    public long? ImageSize { get; set; }
    [XmlAttribute("ImageSize")]
    public long ImageSizeValue
    {
        get => ImageSize ?? 0;
        set => ImageSize = value;
    }
    public bool ShouldSerializeImageSizeValue() => ImageSize.HasValue;

    [XmlAttribute("Key")]
    public string? Key { get; set; }

    [XmlAttribute("Bookmark")]
    public string? Bookmark { get; set; }

    [XmlIgnore]
    public int? ImageWidth { get; set; }
    [XmlAttribute("ImageWidth")]
    public int ImageWidthValue
    {
        get => ImageWidth ?? 0;
        set => ImageWidth = value;
    }
    public bool ShouldSerializeImageWidthValue() => ImageWidth.HasValue;

    [XmlIgnore]
    public int? ImageHeight { get; set; }
    [XmlAttribute("ImageHeight")]
    public int ImageHeightValue
    {
        get => ImageHeight ?? 0;
        set => ImageHeight = value;
    }
    public bool ShouldSerializeImageHeightValue() => ImageHeight.HasValue;
}
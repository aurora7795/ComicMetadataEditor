using System;
using System.Linq;
using System.Xml.Serialization;

namespace InkTag.Core;

public enum MangaDirection
{
    Unknown,
    No,
    Yes,
    YesAndRightToLeft
}

[XmlRoot("ComicInfo")]
public class ComicInfo
{
    [XmlElement("Title")]
    public string? Title { get; set; }

    [XmlElement("Series")]
    public string? Series { get; set; }

    [XmlElement("Number")]
    public string? Number { get; set; }

    [XmlElement("Count")]
    public int? Count { get; set; }
    public bool ShouldSerializeCount() => Count.HasValue;

    [XmlElement("Volume")]
    public int? Volume { get; set; }
    public bool ShouldSerializeVolume() => Volume.HasValue;

    [XmlElement("AlternateSeries")]
    public string? AlternateSeries { get; set; }

    [XmlElement("AlternateNumber")]
    public string? AlternateNumber { get; set; }

    [XmlElement("AlternateCount")]
    public int? AlternateCount { get; set; }
    public bool ShouldSerializeAlternateCount() => AlternateCount.HasValue;

    [XmlElement("Summary")]
    public string? Summary { get; set; }

    [XmlElement("Notes")]
    public string? Notes { get; set; }

    [XmlElement("Year")]
    public int? Year { get; set; }
    public bool ShouldSerializeYear() => Year.HasValue;

    [XmlElement("Month")]
    public int? Month { get; set; }
    public bool ShouldSerializeMonth() => Month.HasValue;

    [XmlElement("Day")]
    public int? Day { get; set; }
    public bool ShouldSerializeDay() => Day.HasValue;

    [XmlElement("Writer")]
    public string? Writer { get; set; }

    [XmlElement("Penciller")]
    public string? Penciller { get; set; }

    [XmlElement("Inker")]
    public string? Inker { get; set; }

    [XmlElement("Colorist")]
    public string? Colorist { get; set; }

    [XmlElement("Letterer")]
    public string? Letterer { get; set; }

    [XmlElement("CoverArtist")]
    public string? CoverArtist { get; set; }

    [XmlElement("Editor")]
    public string? Editor { get; set; }

    [XmlElement("Publisher")]
    public string? Publisher { get; set; }

    [XmlElement("Imprint")]
    public string? Imprint { get; set; }

    [XmlElement("Genre")]
    public string? Genre { get; set; }

    [XmlElement("Tags")]
    public string? Tags { get; set; }

    [XmlElement("Web")]
    public string? Web { get; set; }

    [XmlElement("PageCount")]
    public int? PageCount { get; set; }
    public bool ShouldSerializePageCount() => PageCount.HasValue;

    [XmlElement("LanguageISO")]
    public string? LanguageISO { get; set; }

    [XmlElement("Format")]
    public string? Format { get; set; }

    [XmlElement("BlackAndWhite")]
    public string? BlackAndWhite { get; set; }

    [XmlElement("Manga")]
    public MangaDirection? Manga { get; set; }
    public bool ShouldSerializeManga() => Manga.HasValue;

    [XmlElement("Characters")]
    public string? Characters { get; set; }

    [XmlElement("Teams")]
    public string? Teams { get; set; }

    [XmlElement("Locations")]
    public string? Locations { get; set; }

    [XmlElement("ScanInformation")]
    public string? ScanInformation { get; set; }

    [XmlElement("StoryArc")]
    public string? StoryArc { get; set; }

    [XmlElement("SeriesGroup")]
    public string? SeriesGroup { get; set; }

    [XmlElement("AgeRating")]
    public string? AgeRating { get; set; }

    [XmlElement("CommunityRating")]
    public decimal? CommunityRating { get; set; }
    public bool ShouldSerializeCommunityRating() => CommunityRating.HasValue;

    [XmlElement("MainCharacterOrTeam")]
    public string? MainCharacterOrTeam { get; set; }

    [XmlElement("Review")]
    public string? Review { get; set; }

    [XmlElement("Pages")]
    public PageCollection? Pages { get; set; }

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

public class PageCollection
{
    [XmlElement("Page")]
    public Page[]? Page { get; set; }
}

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
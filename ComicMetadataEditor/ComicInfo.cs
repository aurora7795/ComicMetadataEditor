using System.Xml.Serialization;

namespace ComicMetadataEditor;

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
    public int Count { get; set; }

    [XmlElement("Volume")]
    public int Volume { get; set; }

    [XmlElement("Summary")]
    public string? Summary { get; set; }

    [XmlElement("Notes")]
    public string? Notes { get; set; }

    [XmlElement("Year")]
    public int Year { get; set; }

    [XmlElement("Month")]
    public int Month { get; set; }

    [XmlElement("Day")]
    public int Day { get; set; }

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
    public int PageCount { get; set; }

    [XmlElement("LanguageISO")]
    public string? LanguageISO { get; set; }

    [XmlElement("Format")]
    public string? Format { get; set; }

    [XmlElement("BlackAndWhite")]
    public string? BlackAndWhite { get; set; }

    [XmlElement("Manga")]
    public string? Manga { get; set; } // "Yes" for right-to-left, "No" for left-to-right

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

    [XmlElement("Pages")]
    public PageCollection? Pages { get; set; }
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

    [XmlAttribute("DoublePage")]
    public bool DoublePage { get; set; }

    [XmlAttribute("ImageSize")]
    public long ImageSize { get; set; }

    [XmlAttribute("Key")]
    public string? Key { get; set; }

    [XmlAttribute("Bookmark")]
    public string? Bookmark { get; set; }

    [XmlAttribute("ImageWidth")]
    public int ImageWidth { get; set; }

    [XmlAttribute("ImageHeight")]
    public int ImageHeight { get; set; }
}
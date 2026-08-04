using System;

namespace InkTag.Gui.ViewModels;

public enum BulkEditFieldDataType
{
    String,
    Numeric,
    Enum
}

public enum BulkEditOperation
{
    Set,
    Clear,
    Append,
    Prepend,
    Replace
}

public class BulkEditFieldInfo
{
    public string PropertyName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public BulkEditFieldDataType DataType { get; }
    public string[]? EnumOptions { get; }

    public BulkEditFieldInfo(string propertyName, string displayName, string category, BulkEditFieldDataType dataType, string[]? enumOptions = null)
    {
        PropertyName = propertyName;
        DisplayName = displayName;
        Category = category;
        DataType = dataType;
        EnumOptions = enumOptions;
    }

    public override string ToString() => DisplayName;
}

public static class BulkEditCatalog
{
    public static readonly BulkEditFieldInfo[] AllFields = new[]
    {
        // General / Basic
        new BulkEditFieldInfo("Series", "Series", "General", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Title", "Title", "General", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Number", "Issue Number", "General", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Volume", "Volume", "General", BulkEditFieldDataType.Numeric),
        new BulkEditFieldInfo("Count", "Issue Count", "General", BulkEditFieldDataType.Numeric),
        new BulkEditFieldInfo("Year", "Year", "General", BulkEditFieldDataType.Numeric),
        new BulkEditFieldInfo("Month", "Month", "General", BulkEditFieldDataType.Numeric),
        new BulkEditFieldInfo("Day", "Day", "General", BulkEditFieldDataType.Numeric),

        // Creators / Credits
        new BulkEditFieldInfo("Writer", "Writer", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Penciller", "Penciller", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Inker", "Inker", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Colorist", "Colorist", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Letterer", "Letterer", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("CoverArtist", "Cover Artist", "Creators", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Editor", "Editor", "Creators", BulkEditFieldDataType.String),

        // Publisher & Imprint
        new BulkEditFieldInfo("Publisher", "Publisher", "Publisher", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Imprint", "Imprint", "Publisher", BulkEditFieldDataType.String),

        // Content & Categorization
        new BulkEditFieldInfo("Genre", "Genre", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Tags", "Tags", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Summary", "Summary", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Notes", "Notes", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Format", "Format", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("LanguageISO", "Language ISO", "Content", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("AgeRating", "Age Rating", "Content", BulkEditFieldDataType.Enum, new[] { "Unknown", "Adult 18+", "Early Childhood", "Everyone", "Everyone 10+", "G", "Kid to Adult", "M", "MA15+", "Maturation 17+", "PG", "R18+", "Rating Pending", "Teen", "X18+" }),

        // Manga / Reading
        new BulkEditFieldInfo("MangaDirection", "Manga Reading Direction", "Format", BulkEditFieldDataType.Enum, new[] { "Unknown", "No", "Yes", "YesAndRightToLeft" }),
        new BulkEditFieldInfo("BlackAndWhite", "Black & White", "Format", BulkEditFieldDataType.Enum, new[] { "Unknown", "No", "Yes" }),

        // Universe & Continuity
        new BulkEditFieldInfo("Characters", "Characters", "Universe", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Teams", "Teams", "Universe", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("Locations", "Locations", "Universe", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("StoryArc", "Story Arc", "Universe", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("SeriesGroup", "Series Group", "Universe", BulkEditFieldDataType.String),

        // Technical / Web
        new BulkEditFieldInfo("Web", "Web URL", "Other", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("ScanInformation", "Scan Info", "Other", BulkEditFieldDataType.String),
        new BulkEditFieldInfo("PageCount", "Page Count", "Other", BulkEditFieldDataType.Numeric),
    };
}

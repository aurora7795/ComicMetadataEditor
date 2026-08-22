using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InkTag.Core.Komga;

public class KomgaPathMapping
{
    public string LocalPrefix { get; set; } = string.Empty;
    public string ServerPrefix { get; set; } = string.Empty;
}

public class KomgaLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    [JsonPropertyName("scanForceModifiedTime")]
    public bool ScanForceModifiedTime { get; set; }

    [JsonPropertyName("scanInterval")]
    public string? ScanInterval { get; set; }
}

public class KomgaSeriesDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string LibraryId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("booksCount")]
    public int BooksCount { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaSeriesMetadataDto? Metadata { get; set; }
}

public class KomgaSeriesMetadataDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ONGOING";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("ageRating")]
    public int? AgeRating { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("totalBookCount")]
    public int? TotalBookCount { get; set; }
}

public class KomgaBookDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("seriesId")]
    public string SeriesId { get; set; } = string.Empty;

    [JsonPropertyName("seriesTitle")]
    public string? SeriesTitle { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("metadata")]
    public KomgaBookMetadataDto? Metadata { get; set; }

    [JsonPropertyName("media")]
    public KomgaMediaDto? Media { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public class KomgaBookMetadataDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("numberSort")]
    public double NumberSort { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("authors")]
    public List<KomgaAuthorDto> Authors { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("isbn")]
    public string? Isbn { get; set; }

    [JsonPropertyName("links")]
    public List<KomgaWebLinkDto> Links { get; set; } = new();
}

public class KomgaAuthorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

public class KomgaWebLinkDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class KomgaMediaDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("pagesCount")]
    public int PagesCount { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class KomgaCollectionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ordered")]
    public bool Ordered { get; set; }

    [JsonPropertyName("seriesIds")]
    public List<string> SeriesIds { get; set; } = new();
}

public class KomgaCollectionCreationDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ordered")]
    public bool Ordered { get; set; }

    [JsonPropertyName("seriesIds")]
    public List<string> SeriesIds { get; set; } = new();
}

public class KomgaPageWrapper<T>
{
    [JsonPropertyName("content")]
    public List<T> Content { get; set; } = new();

    [JsonPropertyName("totalElements")]
    public long TotalElements { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }
}

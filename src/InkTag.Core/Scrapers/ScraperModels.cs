using System;
using System.Collections.Generic;

namespace InkTag.Core.Scrapers;

public enum ScrapeMergeMode
{
    FillMissingOnly,
    OverwriteAll,
    SelectiveFields
}

public class ComicSearchQuery
{
    public string Series { get; set; } = string.Empty;
    public string IssueNumber { get; set; } = string.Empty;
    public int? Year { get; set; }

    public override string ToString() => $"{Series} #{IssueNumber} ({Year?.ToString() ?? "Unknown Year"})";
}

public class ComicSearchResult
{
    public string IssueId { get; set; } = string.Empty;
    public string VolumeId { get; set; } = string.Empty;
    public string SeriesTitle { get; set; } = string.Empty;
    public string IssueNumber { get; set; } = string.Empty;
    public string IssueTitle { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string CoverDate { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string SmallCoverUrl { get; set; } = string.Empty;
    public string SiteDetailUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Confidence score calculated during search (0.0 to 1.0)
    public double MatchConfidence { get; set; }
}

public class SeriesSearchResult
{
    public string VolumeId { get; set; } = string.Empty;
    public string SeriesTitle { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public int? StartYear { get; set; }
    public int? CountOfIssues { get; set; }
    public string CoverUrl { get; set; } = string.Empty;
    public string SmallCoverUrl { get; set; } = string.Empty;
    public string SiteDetailUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}


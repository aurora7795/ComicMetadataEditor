using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Images;
using InkTag.Core.Scrapers;
using InkTag.Gui.Services;

namespace InkTag.Gui.ViewModels;

public class BulkScrapeItemViewModel : ObservableObject
{
    private static readonly LruImageCache OnlineImageCache = new(60);
    private static readonly HttpClient HttpThumbnailClient = new();

    // Pre-allocated static brushes to avoid heap allocation on every getter invocation
    private static readonly IBrush BrushDarkGray = new SolidColorBrush(Color.Parse("#333338"));
    private static readonly IBrush BrushZinc = new SolidColorBrush(Color.Parse("#3F3F46"));
    private static readonly IBrush BrushDimGray = new SolidColorBrush(Color.Parse("#2D2D30"));
    private static readonly IBrush BrushMediumGray = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly IBrush BrushGreen = new SolidColorBrush(Color.Parse("#107C41"));
    private static readonly IBrush BrushAmber = new SolidColorBrush(Color.Parse("#CA5010"));
    private static readonly IBrush BrushRed = new SolidColorBrush(Color.Parse("#A80000"));
    private static readonly IBrush BrushBlue = new SolidColorBrush(Color.Parse("#0078D4"));
    private static readonly IBrush BrushNavy = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush BrushSkyBlue = new SolidColorBrush(Color.Parse("#2B88D8"));
    private static readonly IBrush BrushDarkSlate = new SolidColorBrush(Color.Parse("#2D3748"));
    private static readonly IBrush BrushMutedText = new SolidColorBrush(Color.Parse("#CCCCCC"));

    static BulkScrapeItemViewModel()
    {
        HttpThumbnailClient.Timeout = TimeSpan.FromSeconds(6);
        if (!HttpThumbnailClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            HttpThumbnailClient.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (BulkScraperQueue)");
        }
    }

    public BulkScrapeQueueItem Item { get; }

    public BulkScrapeItemViewModel(BulkScrapeQueueItem item)
    {
        Item = item;
        _isSelected = item.IsSelected;
        _status = item.Status;
        _statusMessage = item.StatusMessage;

        // Load local cover thumbnail if bytes are available
        if (item.LocalCoverBytes != null && item.LocalCoverBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(item.LocalCoverBytes);
                _localThumbnail = new Bitmap(ms);
            }
            catch
            {
                _localThumbnail = null;
            }
        }
    }

    public string Filename => Item.Filename;
    public string FilePath => Item.FilePath;
    public bool IsCbr => !string.IsNullOrEmpty(Item.FilePath) &&
                         Path.GetExtension(Item.FilePath).Equals(".cbr", StringComparison.OrdinalIgnoreCase);
    public string FormatBadgeText => "CBR ➔ CBZ";
    public string FormatConversionTooltip => "CBR (RAR) comic archive. Will be automatically repacked into modern, open-standard CBZ (ZIP) format upon saving.";

    public string ParsedSeries => Item.ParsedQuery.Series;
    public string ParsedIssue => Item.ParsedQuery.IssueNumber;
    public int? ParsedYear => Item.ParsedQuery.Year;

    public string ParsedQueryText
    {
        get
        {
            if (string.IsNullOrEmpty(ParsedSeries)) return "—";
            var parts = new List<string> { ParsedSeries };
            if (!string.IsNullOrEmpty(ParsedIssue)) parts.Add($"#{ParsedIssue}");
            if (ParsedYear.HasValue && ParsedYear.Value > 0) parts.Add($"({ParsedYear})");
            return string.Join(" ", parts);
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                Item.IsSelected = value;
                if (!value && (Status == BulkScrapeItemStatus.Ready || Status == BulkScrapeItemStatus.Queued))
                {
                    Status = BulkScrapeItemStatus.Excluded;
                    StatusMessage = "Excluded from auto-tag";
                }
                else if (value && Status == BulkScrapeItemStatus.Excluded)
                {
                    Status = BulkScrapeItemStatus.Ready;
                    StatusMessage = "Ready";
                }
            }
        }
    }

    private BulkScrapeItemStatus _status;
    public BulkScrapeItemStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                Item.Status = value;
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private string _statusMessage;
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                Item.StatusMessage = value;
            }
        }
    }

    private Bitmap? _localThumbnail;
    public Bitmap? LocalThumbnail
    {
        get => _localThumbnail;
        set => SetProperty(ref _localThumbnail, value);
    }

    private Bitmap? _matchedThumbnail;
    public Bitmap? MatchedThumbnail
    {
        get => _matchedThumbnail;
        set
        {
            if (SetProperty(ref _matchedThumbnail, value))
            {
                OnPropertyChanged(nameof(RemoteThumbnail));
            }
        }
    }

    private ComicSearchResult? _lastLoadedCandidate;

    public ComicSearchResult? MatchedCandidate
    {
        get => Item.MatchedCandidate;
        set
        {
            if (ReferenceEquals(_lastLoadedCandidate, value) && Item.MatchedCandidate == value) return;
            _lastLoadedCandidate = value;
            Item.MatchedCandidate = value;
            OnPropertyChanged(nameof(MatchedCandidate));
            OnPropertyChanged(nameof(HasMatch));
            OnPropertyChanged(nameof(MatchedSeriesAndIssue));
            OnPropertyChanged(nameof(MatchedSeriesName));
            OnPropertyChanged(nameof(MatchedTitle));
            OnPropertyChanged(nameof(MatchedIssueTitle));
            OnPropertyChanged(nameof(VisualSimilarity));
            OnPropertyChanged(nameof(VisualSimilarityBadge));
            OnPropertyChanged(nameof(VisualScorePercentage));
            OnPropertyChanged(nameof(HasVisualScore));
            OnPropertyChanged(nameof(VisualMatchText));
            OnPropertyChanged(nameof(VisualBadgeBackground));
            OnPropertyChanged(nameof(VisualMatchLabel));
            OnPropertyChanged(nameof(VisualMatchTooltip));
            OnPropertyChanged(nameof(ConfidenceTooltip));
            OnPropertyChanged(nameof(VisualBadgeForeground));
            OnPropertyChanged(nameof(ConfidenceBadge));
            OnPropertyChanged(nameof(ConfidencePercentage));
            OnPropertyChanged(nameof(ConfidenceScore));
            OnPropertyChanged(nameof(ConfidenceColor));
            
            if (value != null)
            {
                LoadMatchedThumbnailAsync(value);
            }
            else
            {
                MatchedThumbnail = null;
            }
        }
    }

    public string MatchedSeriesAndIssue
    {
        get
        {
            if (MatchedCandidate == null) return "—";
            string series = !string.IsNullOrEmpty(MatchedCandidate.SeriesTitle) ? MatchedCandidate.SeriesTitle : "Unknown Series";
            string issue = !string.IsNullOrEmpty(MatchedCandidate.IssueNumber) ? $"#{MatchedCandidate.IssueNumber}" : string.Empty;
            return $"{series} {issue}".Trim();
        }
    }

    public string MatchedSeriesName => MatchedSeriesAndIssue;
    public string MatchedTitle => MatchedSeriesAndIssue;
    public string MatchedIssueTitle => MatchedCandidate?.IssueTitle ?? string.Empty;
    public Bitmap? RemoteThumbnail => MatchedThumbnail;

    public bool HasMatch => MatchedCandidate != null;
    private static string FormatPercent(double value) => $"{(int)Math.Round(value * 100)}%";

    public double VisualSimilarity => MatchedCandidate?.VisualSimilarity ?? 0.0;
    public double MatchConfidence => MatchedCandidate?.MatchConfidence ?? 0.0;

    public double ConfidenceScore => MatchConfidence;
    public string ConfidencePercentage => MatchedCandidate != null ? FormatPercent(MatchConfidence) : "—";
    public string ConfidenceBadge => MatchedCandidate != null ? $"{FormatPercent(MatchConfidence)} Conf." : "—";

    public string ConfidenceTooltip
    {
        get
        {
            if (MatchedCandidate == null) return "No match found";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Match Confidence: {FormatPercent(MatchConfidence)}");
            sb.AppendLine("────────────────────────");

            bool seriesMatched = !string.IsNullOrEmpty(Item.ParsedQuery.Series) &&
                                 !string.IsNullOrEmpty(MatchedCandidate.SeriesTitle);
            string seriesIcon = seriesMatched ? "✓" : "○";
            sb.AppendLine($"{seriesIcon} Series: '{MatchedCandidate.SeriesTitle}'");

            bool issueMatched = !string.IsNullOrEmpty(Item.ParsedQuery.IssueNumber) &&
                                !string.IsNullOrEmpty(MatchedCandidate.IssueNumber) &&
                                ComicVineProvider.NormalizeIssueNumber(Item.ParsedQuery.IssueNumber) == ComicVineProvider.NormalizeIssueNumber(MatchedCandidate.IssueNumber);
            string issueIcon = issueMatched ? "✓" : "○";
            sb.AppendLine($"{issueIcon} Issue: #{MatchedCandidate.IssueNumber}");

            if (Item.ParsedQuery.Year.HasValue)
            {
                if (MatchedCandidate.VolumeStartYear.HasValue)
                {
                    sb.AppendLine($"✓ Year: {Item.ParsedQuery.Year.Value} (Volume Start: {MatchedCandidate.VolumeStartYear.Value})");
                }
                else if (!string.IsNullOrEmpty(MatchedCandidate.CoverDate))
                {
                    sb.AppendLine($"✓ Cover Date: {MatchedCandidate.CoverDate}");
                }
            }

            if (HasVisualScore)
            {
                sb.AppendLine($"✓ Cover Visual Match: {FormatPercent(MatchedCandidate.VisualSimilarity!.Value)}");
            }
            else
            {
                sb.AppendLine($"○ Cover Visual Match: {VisualMatchLabel}");
            }

            return sb.ToString().TrimEnd();
        }
    }

    public IBrush ConfidenceColor
    {
        get
        {
            if (MatchedCandidate == null) return BrushDarkGray;
            if (MatchConfidence >= 0.8) return BrushGreen;
            if (MatchConfidence >= 0.5) return BrushAmber;
            return BrushRed;
        }
    }

    public bool HasVisualScore => MatchedCandidate?.VisualSimilarity.HasValue == true && MatchedCandidate.VisualSimilarity.Value > 0.01;
    public string VisualScorePercentage => HasVisualScore ? FormatPercent(MatchedCandidate!.VisualSimilarity!.Value) : "—";

    public string VisualMatchLabel
    {
        get
        {
            if (HasVisualScore) return FormatPercent(MatchedCandidate!.VisualSimilarity!.Value);
            if (Item.LocalCoverBytes == null || Item.LocalCoverBytes.Length == 0 || Item.LocalCoverHash == 0) return "No Local Cover";
            if (MatchedCandidate != null && string.IsNullOrEmpty(MatchedCandidate.SmallCoverUrl) && string.IsNullOrEmpty(MatchedCandidate.CoverUrl)) return "No Remote Cover";
            if (MatchedCandidate != null) return "Text Only";
            return "—";
        }
    }

    public string VisualMatchTooltip
    {
        get
        {
            if (HasVisualScore)
            {
                return $"Cover perceptual dHash visual match: {FormatPercent(MatchedCandidate!.VisualSimilarity!.Value)}";
            }
            if (Item.LocalCoverBytes == null || Item.LocalCoverBytes.Length == 0 || Item.LocalCoverHash == 0)
            {
                return "Local comic archive does not contain a readable cover image or extraction failed.";
            }
            if (MatchedCandidate != null && string.IsNullOrEmpty(MatchedCandidate.SmallCoverUrl) && string.IsNullOrEmpty(MatchedCandidate.CoverUrl))
            {
                return "ComicVine does not have a cover thumbnail image for this issue.";
            }
            if (MatchedCandidate != null)
            {
                return "Matched by series title and issue number. Visual cover hash comparison was not performed.";
            }
            return "No candidate matched.";
        }
    }

    public string VisualSimilarityBadge => (MatchedCandidate?.VisualSimilarity.HasValue == true && MatchedCandidate.VisualSimilarity.Value > 0)
        ? $"{FormatPercent(MatchedCandidate.VisualSimilarity.Value)} Visual"
        : "—";

    public string VisualMatchText => VisualSimilarityBadge;

    public IBrush VisualBadgeBackground
    {
        get
        {
            if (HasVisualScore)
            {
                double sim = MatchedCandidate!.VisualSimilarity!.Value;
                if (sim >= 0.85) return BrushGreen;
                if (sim >= 0.65) return BrushAmber;
                return BrushRed;
            }

            if (MatchedCandidate != null)
            {
                return BrushDarkSlate;
            }

            return BrushDarkGray;
        }
    }

    public IBrush VisualBadgeForeground
    {
        get
        {
            if (HasVisualScore) return Brushes.White;
            return BrushMutedText;
        }
    }

    public IBrush StatusBadgeBackground
    {
        get
        {
            return Status switch
            {
                BulkScrapeItemStatus.Ready => BrushZinc,
                BulkScrapeItemStatus.Excluded => BrushDimGray,
                BulkScrapeItemStatus.Queued => BrushNavy,
                BulkScrapeItemStatus.Matched => BrushGreen,
                BulkScrapeItemStatus.Saved => BrushBlue,
                BulkScrapeItemStatus.LowConfidence => BrushAmber,
                BulkScrapeItemStatus.Unmatched => BrushMediumGray,
                BulkScrapeItemStatus.Error => BrushRed,
                _ => BrushSkyBlue
            };
        }
    }

    public string StatusText => Status switch
    {
        BulkScrapeItemStatus.Ready => "Ready",
        BulkScrapeItemStatus.Excluded => "Excluded",
        BulkScrapeItemStatus.Queued => "Queued",
        BulkScrapeItemStatus.ExtractingCover => "Extracting Cover",
        BulkScrapeItemStatus.SearchingComicVine => "Searching",
        BulkScrapeItemStatus.ComparingVisuals => "Comparing",
        BulkScrapeItemStatus.Matched => "Matched",
        BulkScrapeItemStatus.LowConfidence => "Review Needed",
        BulkScrapeItemStatus.Unmatched => "Unmatched",
        BulkScrapeItemStatus.Error => "Error",
        BulkScrapeItemStatus.Saved => "Saved",
        _ => Status.ToString()
    };

    public void SyncFromItem()
    {
        Status = Item.Status;
        StatusMessage = Item.StatusMessage;
        MatchedCandidate = Item.MatchedCandidate;
        IsSelected = Item.IsSelected;
        OnPropertyChanged(nameof(IsCbr));
        OnPropertyChanged(nameof(Filename));
        OnPropertyChanged(nameof(FilePath));

        if (LocalThumbnail == null && Item.LocalCoverBytes != null && Item.LocalCoverBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(Item.LocalCoverBytes);
                LocalThumbnail = new Bitmap(ms);
            }
            catch
            {
                // Ignore
            }
        }
    }

    private void LoadMatchedThumbnailAsync(ComicSearchResult candidate)
    {
        string? url = !string.IsNullOrEmpty(candidate.SmallCoverUrl) ? candidate.SmallCoverUrl : candidate.CoverUrl;
        if (string.IsNullOrEmpty(url)) return;

        if (OnlineImageCache.TryGetValue(url, out var cached) && cached != null)
        {
            MatchedThumbnail = cached;
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                byte[] bytes = await HttpThumbnailClient.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);
                OnlineImageCache.Set(url, bitmap);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (MatchedCandidate == candidate)
                    {
                        MatchedThumbnail = bitmap;
                    }
                });
            }
            catch
            {
                // Thumbnail loading error
            }
        });
    }
}

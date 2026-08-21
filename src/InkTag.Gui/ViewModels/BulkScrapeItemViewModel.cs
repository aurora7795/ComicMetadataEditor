using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Images;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.ViewModels;

public class BulkScrapeItemViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<string, Bitmap> OnlineImageCache = new();
    private static readonly HttpClient HttpThumbnailClient = new();

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
        set => SetProperty(ref _matchedThumbnail, value);
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
            OnPropertyChanged(nameof(MatchedSeriesAndIssue));
            OnPropertyChanged(nameof(MatchedTitle));
            OnPropertyChanged(nameof(MatchedIssueTitle));
            OnPropertyChanged(nameof(VisualSimilarity));
            OnPropertyChanged(nameof(VisualSimilarityBadge));
            OnPropertyChanged(nameof(VisualMatchText));
            OnPropertyChanged(nameof(VisualBadgeBackground));
            OnPropertyChanged(nameof(ConfidenceBadge));
            
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

    public string MatchedTitle => MatchedSeriesAndIssue;
    public string MatchedIssueTitle => MatchedCandidate?.IssueTitle ?? string.Empty;

    public double VisualSimilarity => MatchedCandidate?.VisualSimilarity ?? 0.0;
    public double MatchConfidence => MatchedCandidate?.MatchConfidence ?? 0.0;

    public string VisualSimilarityBadge => (MatchedCandidate?.VisualSimilarity.HasValue == true && MatchedCandidate.VisualSimilarity.Value > 0)
        ? $"{MatchedCandidate.VisualSimilarity.Value:P0} Visual"
        : "—";

    public string VisualMatchText => VisualSimilarityBadge;

    public string ConfidenceBadge => MatchedCandidate != null
        ? $"{MatchedCandidate.MatchConfidence:P0} Conf."
        : "—";

    public IBrush VisualBadgeBackground
    {
        get
        {
            if (MatchedCandidate == null || !MatchedCandidate.VisualSimilarity.HasValue || MatchedCandidate.VisualSimilarity.Value <= 0.01)
            {
                return new SolidColorBrush(Color.Parse("#333338"));
            }

            double sim = MatchedCandidate.VisualSimilarity.Value;
            if (sim >= 0.85) return new SolidColorBrush(Color.Parse("#107C41")); // Green
            if (sim >= 0.65) return new SolidColorBrush(Color.Parse("#CA5010")); // Amber
            return new SolidColorBrush(Color.Parse("#A80000")); // Red
        }
    }

    public IBrush StatusBadgeBackground
    {
        get
        {
            return Status switch
            {
                BulkScrapeItemStatus.Ready => new SolidColorBrush(Color.Parse("#3F3F46")),
                BulkScrapeItemStatus.Excluded => new SolidColorBrush(Color.Parse("#2D2D30")),
                BulkScrapeItemStatus.Queued => new SolidColorBrush(Color.Parse("#0E639C")),
                BulkScrapeItemStatus.Matched => new SolidColorBrush(Color.Parse("#107C41")),
                BulkScrapeItemStatus.Saved => new SolidColorBrush(Color.Parse("#0078D4")),
                BulkScrapeItemStatus.LowConfidence => new SolidColorBrush(Color.Parse("#CA5010")),
                BulkScrapeItemStatus.Unmatched => new SolidColorBrush(Color.Parse("#555555")),
                BulkScrapeItemStatus.Error => new SolidColorBrush(Color.Parse("#A80000")),
                _ => new SolidColorBrush(Color.Parse("#2B88D8"))
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

        if (OnlineImageCache.TryGetValue(url, out var cached))
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
                OnlineImageCache[url] = bitmap;

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

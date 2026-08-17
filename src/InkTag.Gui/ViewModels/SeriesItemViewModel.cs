using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.ViewModels;

public class SeriesItemViewModel : ObservableObject
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Avalonia.Media.Imaging.Bitmap> ImageCache = new();

    public SeriesSearchResult Result { get; }

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public string SeriesTitle => Result.SeriesTitle;
    public string Publisher => string.IsNullOrEmpty(Result.Publisher) ? "Unknown Publisher" : Result.Publisher;
    public string StartYear => Result.StartYear.HasValue ? Result.StartYear.Value.ToString() : "Unknown Year";
    public string IssueCount => Result.CountOfIssues.HasValue ? $"{Result.CountOfIssues.Value} Issues" : "Issues unknown";
    public string PublisherAndYear => $"{Publisher} • {StartYear} • {IssueCount}";
    public string CoverUrl => !string.IsNullOrEmpty(Result.SmallCoverUrl) ? Result.SmallCoverUrl : Result.CoverUrl;
    public string Description => Result.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Result.Description);
    public string DescriptionSnippet => string.IsNullOrWhiteSpace(Result.Description) ? "No series description available." : Result.Description;
    public bool IsDescriptionTruncated => !string.IsNullOrWhiteSpace(Result.Description) && (Result.Description.Length > 160 || Result.Description.Contains('\n'));
    public string? DescriptionToolTip => IsDescriptionTruncated ? Result.Description : null;
    public string? Aliases => Result.Aliases;
    public bool HasAliases => !string.IsNullOrWhiteSpace(Result.Aliases);

    public SeriesItemViewModel(SeriesSearchResult result)
    {
        Result = result;
        _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(CoverUrl)) return;

        if (ImageCache.TryGetValue(CoverUrl, out var cached))
        {
            Thumbnail = cached;
            return;
        }

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ComicMetadataEditor)");
            byte[] bytes = await client.GetByteArrayAsync(CoverUrl);
            using var ms = new System.IO.MemoryStream(bytes);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
            ImageCache[CoverUrl] = bitmap;

            Avalonia.Threading.Dispatcher.UIThread.Post(() => Thumbnail = bitmap);
        }
        catch
        {
            // Ignore thumbnail load failure
        }
    }
}

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Scrapers;
using InkTag.Gui.Services;

namespace InkTag.Gui.ViewModels;

public class SeriesItemViewModel : ObservableObject
{
    private static readonly LruImageCache ImageCache = new(60);
    private static readonly HttpClient SharedHttpClient = new();

    static SeriesItemViewModel()
    {
        SharedHttpClient.Timeout = TimeSpan.FromSeconds(8);
        if (!SharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (SeriesWizard)");
        }
    }

    public SeriesSearchResult Result { get; }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public string SeriesTitle => Result.SeriesTitle;
    public string Publisher => string.IsNullOrEmpty(Result.Publisher) ? "Unknown Publisher" : Result.Publisher;
    public string StartYear => Result.StartYear.HasValue ? Result.StartYear.Value.ToString() : "Unknown Year";
    public string IssueCount => Result.CountOfIssues.HasValue 
        ? (Result.CountOfIssues.Value == 1 ? "1 Issue" : $"{Result.CountOfIssues.Value} Issues") 
        : "Issues unknown";
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

        if (ImageCache.TryGetValue(CoverUrl, out var cached) && cached != null)
        {
            Thumbnail = cached;
            return;
        }

        try
        {
            byte[] bytes = await SharedHttpClient.GetByteArrayAsync(CoverUrl);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            ImageCache.Set(CoverUrl, bitmap);

            Dispatcher.UIThread.Post(() => Thumbnail = bitmap);
        }
        catch
        {
            // Ignore thumbnail load failure
        }
    }
}

using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.ViewModels;

public class CandidateItemViewModel : ObservableObject
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Avalonia.Media.Imaging.Bitmap> ImageCache = new();

    public ComicSearchResult Result { get; }

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public string SeriesTitle => Result.SeriesTitle;
    public string IssueNumber => string.IsNullOrEmpty(Result.IssueNumber) ? "" : $"#{Result.IssueNumber}";
    public string IssueTitle => Result.IssueTitle;
    public string DisplayTitle => !string.IsNullOrWhiteSpace(IssueTitle) ? $"{IssueNumber} - {IssueTitle}" : IssueNumber;
    public string CoverDate => Result.CoverDate;
    public double MatchConfidence => Result.MatchConfidence;
    public string CoverUrl => !string.IsNullOrEmpty(Result.SmallCoverUrl) ? Result.SmallCoverUrl : Result.CoverUrl;

    public CandidateItemViewModel(ComicSearchResult result)
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
            // Ignore cover load errors
        }
    }
}

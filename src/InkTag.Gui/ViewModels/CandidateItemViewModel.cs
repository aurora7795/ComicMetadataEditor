using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InkTag.Core.Images;
using InkTag.Core.Scrapers;

namespace InkTag.Gui.ViewModels;

public class CandidateItemViewModel : ObservableObject
{
    private static readonly ConcurrentDictionary<string, Bitmap> ImageCache = new();
    private static readonly ConcurrentDictionary<string, ulong> HashCache = new();

    public ComicSearchResult Result { get; }
    private readonly ulong? _targetCoverHash;

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    private ulong? _coverHash;
    public ulong? CoverHash
    {
        get => _coverHash;
        set => SetProperty(ref _coverHash, value);
    }

    private double? _visualSimilarity;
    public double? VisualSimilarity
    {
        get => _visualSimilarity;
        set
        {
            if (SetProperty(ref _visualSimilarity, value))
            {
                OnPropertyChanged(nameof(VisualSimilarityBadge));
                OnPropertyChanged(nameof(HasVisualMatchBadge));
            }
        }
    }

    private bool _isTopVisualMatch;
    public bool IsTopVisualMatch
    {
        get => _isTopVisualMatch;
        set => SetProperty(ref _isTopVisualMatch, value);
    }

    public string SeriesTitle => Result.SeriesTitle;
    public string IssueNumber => string.IsNullOrEmpty(Result.IssueNumber) ? "" : $"#{Result.IssueNumber}";
    public string IssueTitle => Result.IssueTitle;
    public string DisplayTitle => !string.IsNullOrWhiteSpace(IssueTitle) ? $"{IssueNumber} - {IssueTitle}" : IssueNumber;
    public string CoverDate => Result.CoverDate;
    public double MatchConfidence => Result.MatchConfidence;
    public string CoverUrl => !string.IsNullOrEmpty(Result.SmallCoverUrl) ? Result.SmallCoverUrl : Result.CoverUrl;

    public string VisualSimilarityBadge => VisualSimilarity.HasValue && VisualSimilarity.Value > 0 ? $"👁 {VisualSimilarity.Value:P0} Cover Match" : "";
    public bool HasVisualMatchBadge => VisualSimilarity.HasValue && VisualSimilarity.Value >= 0.70;

    public event Action<CandidateItemViewModel>? OnCoverHashComputed;

    public CandidateItemViewModel(ComicSearchResult result, ulong? targetCoverHash = null)
    {
        Result = result;
        _targetCoverHash = targetCoverHash;

        if (result.CoverHash.HasValue && result.CoverHash.Value != 0)
        {
            CoverHash = result.CoverHash.Value;
            if (targetCoverHash.HasValue && targetCoverHash.Value != 0)
            {
                VisualSimilarity = PerceptualHashService.CalculateSimilarity(targetCoverHash.Value, CoverHash.Value);
                Result.VisualSimilarity = VisualSimilarity;
            }
        }

        _ = LoadThumbnailAsync();
    }

    public void UpdateVisualSimilarity(ulong targetHash)
    {
        if (CoverHash.HasValue && CoverHash.Value != 0)
        {
            VisualSimilarity = PerceptualHashService.CalculateSimilarity(targetHash, CoverHash.Value);
            Result.VisualSimilarity = VisualSimilarity;
        }
    }

    private async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(CoverUrl)) return;

        if (ImageCache.TryGetValue(CoverUrl, out var cachedBitmap))
        {
            Thumbnail = cachedBitmap;
            if (HashCache.TryGetValue(CoverUrl, out var cachedHash))
            {
                ProcessCoverHash(cachedHash);
            }
            return;
        }

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "InkTag/1.0 (ComicMetadataEditor)");
            byte[] bytes = await client.GetByteArrayAsync(CoverUrl);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            ImageCache[CoverUrl] = bitmap;

            // Compute perceptual dHash for online thumbnail
            ulong hash = PerceptualHashService.ComputeDHash(bytes);
            if (hash != 0)
            {
                HashCache[CoverUrl] = hash;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Thumbnail = bitmap;
                if (hash != 0)
                {
                    ProcessCoverHash(hash);
                }
            });
        }
        catch
        {
            // Ignore cover load errors
        }
    }

    private void ProcessCoverHash(ulong hash)
    {
        CoverHash = hash;
        Result.CoverHash = hash;

        if (_targetCoverHash.HasValue && _targetCoverHash.Value != 0)
        {
            VisualSimilarity = PerceptualHashService.CalculateSimilarity(_targetCoverHash.Value, hash);
            Result.VisualSimilarity = VisualSimilarity;
        }

        OnCoverHashComputed?.Invoke(this);
    }
}

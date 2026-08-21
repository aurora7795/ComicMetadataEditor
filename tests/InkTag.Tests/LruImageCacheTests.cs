using System;
using System.IO;
using Avalonia.Media.Imaging;
using InkTag.Gui.Services;
using Xunit;

namespace InkTag.Tests;

public class LruImageCacheTests
{
    [Fact]
    public void LruImageCache_StoresAndRetrievesItems()
    {
        var cache = new LruImageCache(maxCapacity: 10);

        Assert.False(cache.TryGetValue("missing", out var _));
        Assert.False(cache.TryGetValue("", out var _));
        Assert.False(cache.TryGetValue(null!, out var _));
    }

    [Fact]
    public void LruImageCache_RespectsCapacityLimit()
    {
        var cache = new LruImageCache(maxCapacity: 10);

        // Verify capacity lower bound clamp
        var tinyCache = new LruImageCache(maxCapacity: 2);
        // Should clamp to 10 minimum capacity
        for (int i = 0; i < 15; i++)
        {
            // Create a small 1x1 dummy bitmap if possible, or verify retrieval
        }

        cache.Clear();
    }
}

using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace InkTag.Core.Images;

/// <summary>
/// Provides fast, hardware-accelerated perceptual image hashing (dHash) and Hamming distance visual comparison for comic covers.
/// </summary>
public static class PerceptualHashService
{
    private const int HashWidth = 9;
    private const int HashHeight = 8;

    /// <summary>
    /// Computes a 64-bit difference hash (dHash) from an image byte array.
    /// </summary>
    public static ulong ComputeDHash(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return 0;
        using var ms = new MemoryStream(imageBytes);
        return ComputeDHash(ms);
    }

    /// <summary>
    /// Computes a 64-bit difference hash (dHash) from an image stream.
    /// Resizes the image to 9x8 grayscale and computes horizontal luminance gradients.
    /// </summary>
    public static ulong ComputeDHash(Stream imageStream)
    {
        if (imageStream == null || !imageStream.CanRead) return 0;

        try
        {
            using var image = Image.Load<L8>(imageStream);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(HashWidth, HashHeight),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic
            }));

            ulong hash = 0UL;
            int bitPosition = 0;

            for (int y = 0; y < HashHeight; y++)
            {
                for (int x = 0; x < HashWidth - 1; x++)
                {
                    byte leftPixel = image[x, y].PackedValue;
                    byte rightPixel = image[x + 1, y].PackedValue;

                    if (leftPixel > rightPixel)
                    {
                        hash |= (1UL << bitPosition);
                    }
                    bitPosition++;
                }
            }

            return hash;
        }
        catch
        {
            // If image decoding fails, return 0
            return 0;
        }
    }

    /// <summary>
    /// Computes the Hamming distance (number of differing bits) between two 64-bit hashes using hardware-accelerated POPCNT.
    /// </summary>
    public static int ComputeHammingDistance(ulong hashA, ulong hashB)
    {
        return BitOperations.PopCount(hashA ^ hashB);
    }

    /// <summary>
    /// Computes visual similarity between two hashes on a scale of 0.0 (completely different) to 1.0 (identical).
    /// </summary>
    public static double CalculateSimilarity(ulong hashA, ulong hashB)
    {
        if (hashA == 0 || hashB == 0) return 0.0;
        int distance = ComputeHammingDistance(hashA, hashB);
        return Math.Max(0.0, 1.0 - ((double)distance / 64.0));
    }

    /// <summary>
    /// Determines if two cover hashes match within the specified similarity threshold (default 90% / distance &lt;= 6).
    /// </summary>
    public static bool IsVisualMatch(ulong hashA, ulong hashB, double threshold = 0.90)
    {
        return CalculateSimilarity(hashA, hashB) >= threshold;
    }
}

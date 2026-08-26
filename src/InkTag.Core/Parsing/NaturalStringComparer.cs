using System;
using System.Collections.Generic;

namespace InkTag.Core.Parsing;

/// <summary>
/// Compares strings using natural sort order, so that numbers inside strings
/// are evaluated by numerical value rather than lexicographical character codes (e.g. '01' &lt; '02' &lt; '10').
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer OrdinalIgnoreCase = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                // Parse numeric spans
                int startX = ix;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                ReadOnlySpan<char> spanX = x.AsSpan(startX, ix - startX);

                int startY = iy;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                ReadOnlySpan<char> spanY = y.AsSpan(startY, iy - startY);

                // Trim leading zeros for numerical comparison
                ReadOnlySpan<char> trimX = spanX.TrimStart('0');
                ReadOnlySpan<char> trimY = spanY.TrimStart('0');

                if (trimX.Length != trimY.Length)
                {
                    return trimX.Length.CompareTo(trimY.Length);
                }

                int numComp = trimX.SequenceCompareTo(trimY);
                if (numComp != 0)
                {
                    return numComp;
                }

                // If numeric value is identical (e.g. 01 vs 001), fewer leading zeros come first
                if (spanX.Length != spanY.Length)
                {
                    return spanX.Length.CompareTo(spanY.Length);
                }
            }
            else
            {
                int comp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (comp != 0) return comp;
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}

namespace InkTag.Gui.Controls;

using System;
using Avalonia;
using Avalonia.Controls;

/// <summary>
/// A specialized horizontal panel for placing a flexible leading element (e.g. a TextBlock that can trim/ellipsize)
/// alongside trailing badges (e.g. format tag).
/// When available width allows, the trailing elements stay right next to the leading text ("hugging" it).
/// When available space is constrained, trailing elements are preserved and the leading element is constrained/truncated.
/// </summary>
public class HuggingBadgePanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<HuggingBadgePanel, double>(nameof(Spacing), 6.0);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        if (children.Count == 0)
        {
            return default;
        }

        double spacing = Spacing;
        double trailingWidth = 0;
        double maxHeight = 0;
        int visibleTrailingCount = 0;

        // Measure trailing children first (index 1 to N-1) with unconstrained or available size
        for (int i = 1; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsVisible)
            {
                child.Measure(availableSize);
                trailingWidth += child.DesiredSize.Width;
                maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
                visibleTrailingCount++;
            }
        }

        if (visibleTrailingCount > 0)
        {
            trailingWidth += visibleTrailingCount * spacing;
        }

        // Measure primary child (index 0) with remaining width
        var primaryChild = children[0];
        double primaryWidth = 0;

        if (primaryChild.IsVisible)
        {
            double availableForPrimary = double.IsPositiveInfinity(availableSize.Width)
                ? double.PositiveInfinity
                : Math.Max(0, availableSize.Width - trailingWidth);

            primaryChild.Measure(new Size(availableForPrimary, availableSize.Height));
            primaryWidth = primaryChild.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, primaryChild.DesiredSize.Height);
        }

        double totalDesiredWidth = primaryWidth + (visibleTrailingCount > 0 ? trailingWidth : 0);
        if (!double.IsPositiveInfinity(availableSize.Width))
        {
            totalDesiredWidth = Math.Min(totalDesiredWidth, availableSize.Width);
        }

        return new Size(totalDesiredWidth, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;
        if (children.Count == 0)
        {
            return finalSize;
        }

        double spacing = Spacing;
        double trailingWidth = 0;
        int visibleTrailingCount = 0;

        for (int i = 1; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsVisible)
            {
                trailingWidth += child.DesiredSize.Width;
                visibleTrailingCount++;
            }
        }

        if (visibleTrailingCount > 0)
        {
            trailingWidth += visibleTrailingCount * spacing;
        }

        var primaryChild = children[0];
        double currentX = 0;

        if (primaryChild.IsVisible)
        {
            double maxPrimaryWidth = Math.Max(0, finalSize.Width - (visibleTrailingCount > 0 ? trailingWidth : 0));
            double primaryWidth = Math.Min(primaryChild.DesiredSize.Width, maxPrimaryWidth);

            primaryChild.Arrange(new Rect(0, 0, primaryWidth, finalSize.Height));
            currentX = primaryWidth;
        }

        for (int i = 1; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsVisible)
            {
                if (currentX > 0)
                {
                    currentX += spacing;
                }

                double childWidth = child.DesiredSize.Width;
                child.Arrange(new Rect(currentX, 0, childWidth, finalSize.Height));
                currentX += childWidth;
            }
        }

        return finalSize;
    }
}

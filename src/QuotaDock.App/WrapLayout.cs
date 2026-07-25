using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace QuotaDock.App;

/// <summary>
/// A simple non-virtualizing wrap layout for <see cref="ItemsRepeater"/> that
/// places items left-to-right and wraps to the next row when the available
/// width is exceeded. Unlike <c>UniformGridLayout</c>, each item keeps its own
/// measured height, so a mix of expanded and collapsed cards renders correctly
/// with per-row heights equal to the tallest item in that row.
/// </summary>
internal sealed class WrapLayout : NonVirtualizingLayout
{
    public double DesiredColumnWidth { get; set; } = 280;
    public double ColumnSpacing { get; set; } = 8;
    public double RowSpacing { get; set; } = 8;

    private readonly List<Rect> _arrangeRects = [];

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        _arrangeRects.Clear();
        var maxWidth = availableSize.Width;
        if (double.IsInfinity(maxWidth) || maxWidth <= 0)
        {
            maxWidth = 400;
        }

        double x = 0, y = 0, rowHeight = 0, totalHeight = 0;
        var columnWidth = Math.Max(DesiredColumnWidth, 120);

        foreach (var element in context.Children)
        {
            element.Measure(new Size(columnWidth, double.PositiveInfinity));
            var desired = element.DesiredSize;
            var itemWidth = Math.Min(desired.Width, maxWidth);
            var itemHeight = desired.Height;

            if (x + itemWidth > maxWidth && x > 0)
            {
                x = 0;
                y += rowHeight + RowSpacing;
                rowHeight = 0;
            }

            _arrangeRects.Add(new Rect(x, y, itemWidth, itemHeight));
            x += itemWidth + ColumnSpacing;
            rowHeight = Math.Max(rowHeight, itemHeight);
        }

        totalHeight = y + rowHeight;
        return new Size(maxWidth, totalHeight);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        for (var i = 0; i < context.Children.Count && i < _arrangeRects.Count; i++)
        {
            context.Children[i].Arrange(_arrangeRects[i]);
        }

        return finalSize;
    }
}

using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// A panel that positions child elements sequentially from left to right,
/// breaking to a new line when elements reach the edge of the panel.
/// </summary>
public class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0.0, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0.0, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double curX = 0;
        double totalY = 0;
        double lineH = 0;
        double maxW = 0;
        int countInLine = 0;

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = child.DesiredSize;
            double spacing = countInLine > 0 ? HorizontalSpacing : 0;

            if (!double.IsInfinity(availableSize.Width) && curX + spacing + sz.Width > availableSize.Width && countInLine > 0)
            {
                maxW = Math.Max(maxW, curX);
                totalY += lineH + VerticalSpacing;
                curX = sz.Width;
                lineH = sz.Height;
                countInLine = 1;
            }
            else
            {
                curX += spacing + sz.Width;
                lineH = Math.Max(lineH, sz.Height);
                countInLine++;
            }
        }

        maxW = Math.Max(maxW, curX);
        if (countInLine > 0)
        {
            totalY += lineH;
        }

        double finalW = double.IsInfinity(availableSize.Width) ? maxW : Math.Min(availableSize.Width, maxW);
        double finalH = double.IsInfinity(availableSize.Height) ? totalY : Math.Min(availableSize.Height, totalY);

        return new Size(finalW, finalH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double curX = 0;
        double curY = 0;
        double lineH = 0;
        int countInLine = 0;

        var lineElements = new List<UIElement>();

        void ArrangeCurrentLine(double y, double height)
        {
            double x = 0;
            for (int i = 0; i < lineElements.Count; i++)
            {
                var el = lineElements[i];
                if (i > 0) x += HorizontalSpacing;

                var sz = el.DesiredSize;
                // Center vertically within the row line
                double childY = y + Math.Max(0, (height - sz.Height) / 2.0);
                el.Arrange(new Rect(x, childY, sz.Width, sz.Height));
                x += sz.Width;
            }
            lineElements.Clear();
        }

        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            var sz = child.DesiredSize;
            double spacing = countInLine > 0 ? HorizontalSpacing : 0;

            if (curX + spacing + sz.Width > finalSize.Width && countInLine > 0)
            {
                ArrangeCurrentLine(curY, lineH);
                curY += lineH + VerticalSpacing;
                curX = sz.Width;
                lineH = sz.Height;
                countInLine = 1;
                lineElements.Add(child);
            }
            else
            {
                curX += spacing + sz.Width;
                lineH = Math.Max(lineH, sz.Height);
                countInLine++;
                lineElements.Add(child);
            }
        }

        if (lineElements.Count > 0)
        {
            ArrangeCurrentLine(curY, lineH);
        }

        return finalSize;
    }
}

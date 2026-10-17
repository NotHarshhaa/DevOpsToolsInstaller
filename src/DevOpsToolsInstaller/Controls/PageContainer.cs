using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Consistent page shell for every page: uniform outer padding plus a centred,
/// capped content width so all titles and left edges line up. Place a single
/// root element inside it (a Grid/StackPanel/ScrollViewer).
///
/// Data-grid pages (Catalog, Installed) set <see cref="FullWidth"/> so their
/// tables use every available pixel; every other page keeps the shared
/// <see cref="MaxContentWidth"/> so content stays aligned when the window is
/// resized from narrow up to wide.
/// </summary>
public sealed class PageContainer : Panel
{
    /// <summary>Identifies the <see cref="PagePadding"/> dependency property.</summary>
    public static readonly DependencyProperty PagePaddingProperty =
        DependencyProperty.Register(
            nameof(PagePadding),
            typeof(Thickness),
            typeof(PageContainer),
            new PropertyMetadata(new Thickness(32, 24, 0, 24), OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="MaxContentWidth"/> dependency property.</summary>
    public static readonly DependencyProperty MaxContentWidthProperty =
        DependencyProperty.Register(
            nameof(MaxContentWidth),
            typeof(double),
            typeof(PageContainer),
            new PropertyMetadata(1200d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="FullWidth"/> dependency property.</summary>
    public static readonly DependencyProperty FullWidthProperty =
        DependencyProperty.Register(
            nameof(FullWidth),
            typeof(bool),
            typeof(PageContainer),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    /// <summary>Outer padding around the content (defaults to 32 left, 24 top, 0 right, 24 bottom so scrollbars reach the corner right).</summary>
    public Thickness PagePadding
    {
        get => (Thickness)GetValue(PagePaddingProperty);
        set => SetValue(PagePaddingProperty, value);
    }

    /// <summary>Maximum width of the content column.</summary>
    public double MaxContentWidth
    {
        get => (double)GetValue(MaxContentWidthProperty);
        set => SetValue(MaxContentWidthProperty, value);
    }

    /// <summary>When true the content stretches edge to edge instead of being capped.</summary>
    public bool FullWidth
    {
        get => (bool)GetValue(FullWidthProperty);
        set => SetValue(FullWidthProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PageContainer)d).InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pad = PagePadding;
        var width = double.IsInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - pad.Left - pad.Right);
        var height = double.IsInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Height - pad.Top - pad.Bottom);

        var inner = new Size(width, height);

        var contentWidth = 0d;
        var contentHeight = 0d;
        foreach (UIElement child in Children)
        {
            child.Measure(inner);
            contentWidth = Math.Max(contentWidth, child.DesiredSize.Width);
            contentHeight = Math.Max(contentHeight, child.DesiredSize.Height);
        }

        var resultWidth = double.IsInfinity(availableSize.Width)
            ? contentWidth + pad.Left + pad.Right
            : availableSize.Width;
        var resultHeight = double.IsInfinity(availableSize.Height)
            ? contentHeight + pad.Top + pad.Bottom
            : availableSize.Height;

        return new Size(resultWidth, resultHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var pad = PagePadding;
        var childWidth = Math.Max(0, finalSize.Width - pad.Left - pad.Right);
        var childHeight = Math.Max(0, finalSize.Height - pad.Top - pad.Bottom);

        foreach (UIElement child in Children)
        {
            child.Arrange(new Rect(pad.Left, pad.Top, childWidth, childHeight));
        }

        return finalSize;
    }
}

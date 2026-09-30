using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using SkiaSharp;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Shared plumbing for the LiveCharts (SkiaSharp/WinUI) sparklines and bar
/// charts. Centralizes accent/theme colors, bounded sample buffers, and
/// series/axis factories so pages never repeat chart styling.
///
/// All charts are fed at or below ~4 samples/second — the download pipeline
/// reports every 400 ms and page timers tick at 250 ms–1 s.
/// </summary>
public static class ChartTelemetry
{
    /// <summary>Samples kept per sparkline before the oldest is dropped.</summary>
    public const int DefaultCapacity = 60;

    /// <summary>Fixed-size ring buffer of chart values.</summary>
    public sealed class BoundedValues : ObservableCollection<ObservableValue>
    {
        private readonly int _capacity;

        public BoundedValues(int capacity = DefaultCapacity) => _capacity = capacity;

        public void Push(double value)
        {
            Add(new ObservableValue(value));
            while (Count > _capacity)
            {
                RemoveAt(0);
            }
        }

        public void ClearSamples() => Clear();
    }

    /// <summary>System accent color as an SKColor.</summary>
    public static SKColor Accent()
    {
        try
        {
            if (Application.Current.Resources["SystemAccentColor"] is Windows.UI.Color c)
            {
                return new SKColor(c.R, c.G, c.B);
            }
        }
        catch
        {
            // Fall through to the default accent.
        }
        return new SKColor(0x00, 0x78, 0xD4); // Windows default blue
    }

    /// <summary>Axis label / text color for the current theme.</summary>
    public static SKColor TextColor(ElementTheme theme) =>
        theme == ElementTheme.Light
            ? new SKColor(0x5B, 0x5B, 0x5B)
            : new SKColor(0xC8, 0xC8, 0xC8);

    /// <summary>Solid series stroke in the accent color.</summary>
    public static SolidColorPaint AccentStroke(float thickness = 2f) =>
        new(Accent()) { StrokeThickness = thickness };

    /// <summary>Translucent accent area fill (for line sparklines).</summary>
    public static SolidColorPaint AccentFill() =>
        new(new SKColor(Accent().Red, Accent().Green, Accent().Blue, 0x33));

    /// <summary>A sparkline line series (no points, smooth, accent-colored).</summary>
    public static LineSeries<ObservableValue> CreateSparkLine(ObservableCollection<ObservableValue> values) =>
        new()
        {
            Values = values,
            Stroke = AccentStroke(),
            Fill = AccentFill(),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.7,
            DataPadding = new LiveChartsCore.Drawing.LvcPoint(0, 0.4f)
        };

    /// <summary>A rounded accent bar series (for per-tool latency).</summary>
    public static ColumnSeries<ObservableValue> CreateBarSeries(ObservableCollection<ObservableValue> values) =>
        new()
        {
            Values = values,
            Fill = AccentFill(),
            Stroke = null,
            MaxBarWidth = 26,
            DataPadding = new LiveChartsCore.Drawing.LvcPoint(0, 0.4f)
        };

    /// <summary>Minimal axes for a sparkline: hidden X, tick-only Y starting at zero.</summary>
    public static Axis[] CreateSparkAxes(ElementTheme theme)
    {
        var text = TextColor(theme);
        return
        [
            new Axis // X — hidden
            {
                IsVisible = false,
                LabelsPaint = null
            },
            new Axis // Y — sparse ticks, no grid clutter
            {
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 10,
                MinLimit = 0,
                ShowSeparatorLines = false,
                SeparatorsPaint = null
            }
        ];
    }

    /// <summary>Axes for a labeled bar chart (tool names on X, values on Y).</summary>
    public static Axis[] CreateLabeledAxes(string[] labels, ElementTheme theme, double labelsRotation)
    {
        var text = TextColor(theme);
        return
        [
            new Axis // X — tool names
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 10,
                LabelsRotation = labelsRotation,
                ShowSeparatorLines = false,
                SeparatorsPaint = null
            },
            new Axis // Y — values (ms)
            {
                LabelsPaint = new SolidColorPaint(text),
                TextSize = 10,
                MinLimit = 0,
                ShowSeparatorLines = false,
                SeparatorsPaint = null
            }
        ];
    }

    /// <summary>Empty axes (used to hide chart chrome entirely).</summary>
    public static Axis[] HiddenAxes() => [new Axis { IsVisible = false }, new Axis { IsVisible = false }];
}

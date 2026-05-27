using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.Views.Controls;

/// <summary>
/// Interactive curve chart wrapping ScottPlot.
/// Shows original curve (solid blue) and optional overridden curve (dashed orange).
/// Supports dual Y-axis — left series and right series selected independently.
/// </summary>
public partial class InteractiveCurveChart : UserControl
{
    public InteractiveCurveChart()
    {
        InitializeComponent();
        InitializePlot();
    }

    private void InitializePlot()
    {
        var plot = PlotControl.Plot;

        // Title & labels
        plot.Title("FX Forward Curve");
        plot.XLabel("Tenor");

        // Configure left Y axis
        plot.Axes.Left.Label.Text = "";
        plot.Axes.Left.Label.FontSize = 12;

        // Configure right Y axis (hidden until used)
        plot.Axes.Right.Label.Text = "";
        plot.Axes.Right.Label.FontSize = 12;
        plot.Axes.Right.IsVisible = false;

        // Grid
        plot.Grid.MajorLineWidth = 0.5f;
        plot.Grid.MinorLineWidth = 0.2f;

        // Legend
        plot.ShowLegend(Alignment.UpperRight);

        // Set initial axis limits
        plot.Axes.SetLimits(-0.5, 32, 0, 6);
    }

    /// <summary>
    /// Update the chart with the latest curve snapshots.
    /// Call this from UI timer or ViewModel when new data arrives.
    /// </summary>
    public void UpdateChart(
        List<CurveSnapshot> snapshots,
        string leftSeriesName,
        string rightSeriesName)
    {
        var plot = PlotControl.Plot;
        plot.Clear();  // Remove all plottables

        bool hasRightSeries = !string.IsNullOrEmpty(rightSeriesName) &&
                              !rightSeriesName.Equals("None", StringComparison.OrdinalIgnoreCase);

        // Show/hide right axis
        plot.Axes.Right.IsVisible = hasRightSeries;

        // Legend items for current display
        var legendItems = new List<(string name, System.Drawing.Color color, LineStyle style)>();

        // Plot each snapshot as a separate line
        foreach (var snapshot in snapshots)
        {
            if (snapshot == null || snapshot.TenorYearFractions.Count == 0)
                continue;

            double[] xs = snapshot.TenorYearFractions.ToArray();

            // Plot left Y-axis series
            var leftSeries = snapshot.Series.FirstOrDefault(s => s.Name == leftSeriesName);
            if (leftSeries != null && leftSeries.Values.Count == xs.Length)
            {
                double[] ys = leftSeries.Values.Select(v => (double)v).ToArray();
                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Axes.YAxisIndex = 0;  // Left axis

                bool isOriginal = snapshot.Tag == "original";
                scatter.Color = isOriginal ? System.Drawing.Color.DodgerBlue : System.Drawing.Color.OrangeRed;
                scatter.LineStyle = isOriginal ? LineStyle.Solid : LineStyle.Dash;
                scatter.LineWidth = isOriginal ? 2f : 2f;
                scatter.MarkerStyle = MarkerStyle.OpenCircle;
                scatter.MarkerSize = isOriginal ? 4f : 5f;
                scatter.Label = isOriginal
                    ? $"{leftSeriesName} (original)"
                    : $"{leftSeriesName} ({snapshot.Tag})";

                // Mark overridden tenors
                if (!isOriginal)
                {
                    scatter.MarkerStyle = MarkerStyle.FilledDiamond;
                    scatter.MarkerSize = 6f;
                }
            }

            // Plot right Y-axis series
            if (hasRightSeries)
            {
                var rightSeries = snapshot.Series.FirstOrDefault(s => s.Name == rightSeriesName);
                if (rightSeries != null && rightSeries.Values.Count == xs.Length)
                {
                    double[] ys = rightSeries.Values.Select(v => (double)v).ToArray();
                    var scatter2 = plot.Add.Scatter(xs, ys);
                    scatter2.Axes.YAxisIndex = 1;  // Right axis

                    bool isOriginal = snapshot.Tag == "original";
                    scatter2.Color = isOriginal
                        ? System.Drawing.Color.ForestGreen
                        : System.Drawing.Color.DarkViolet;
                    scatter2.LineStyle = isOriginal ? LineStyle.Solid : LineStyle.Dash;
                    scatter2.LineWidth = 1.5f;
                    scatter2.MarkerStyle = MarkerStyle.OpenSquare;
                    scatter2.MarkerSize = isOriginal ? 3f : 5f;
                    scatter2.Label = isOriginal
                        ? $"{rightSeriesName} (original)"
                        : $"{rightSeriesName} ({snapshot.Tag})";
                }
            }
        }

        // Auto-scale Y axes with some padding
        if (snapshots.Count > 0 && snapshots[0]?.Series.Count > 0)
        {
            AutoScaleY(plot, leftSeriesName, snapshots);
            if (hasRightSeries)
                AutoScaleY(plot, rightSeriesName, snapshots, isRight: true);
        }

        // Update left/right axis labels
        var leftLabel = leftSeriesName switch
        {
            "Instantaneous Forward Rate" => "Forward Rate (%)",
            "Zero Rate" => "Zero Rate (%)",
            "Discount Factor" => "Discount Factor",
            "Par Rate" => "Par Rate (%)",
            _ => leftSeriesName
        };
        plot.Axes.Left.Label.Text = leftLabel;

        if (hasRightSeries)
        {
            var rightLabel = rightSeriesName switch
            {
                "Instantaneous Forward Rate" => "Forward Rate (%)",
                "Zero Rate" => "Zero Rate (%)",
                "Discount Factor" => "Discount Factor",
                "Par Rate" => "Par Rate (%)",
                _ => rightSeriesName
            };
            plot.Axes.Right.Label.Text = rightLabel;
        }

        // Customize X axis — show tenor labels as text
        ConfigureXAxis(plot, snapshots);

        PlotControl.Refresh();
    }

    private static void AutoScaleY(Plot plot, string seriesName, List<CurveSnapshot> snapshots, bool isRight = false)
    {
        double minVal = double.MaxValue, maxVal = double.MinValue;
        bool found = false;

        foreach (var snap in snapshots)
        {
            var series = snap.Series.FirstOrDefault(s => s.Name == seriesName);
            if (series == null) continue;
            foreach (double v in series.Values)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                minVal = Math.Min(minVal, v);
                maxVal = Math.Max(maxVal, v);
                found = true;
            }
        }

        if (!found) return;

        double padding = (maxVal - minVal) * 0.15;
        if (padding < 0.05) padding = 0.05;
        minVal -= padding;
        maxVal += padding;

        var axis = isRight ? plot.Axes.Right : plot.Axes.Left;
        axis.Min = minVal;
        axis.Max = maxVal;
    }

    private static void ConfigureXAxis(Plot plot, List<CurveSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return;

        // Set X axis limits from tenor range
        var first = snapshots[0];
        if (first.TenorYearFractions.Count > 0)
        {
            double minX = -0.5;
            double maxX = first.TenorYearFractions[^1] + 2;
            plot.Axes.SetLimitsX(minX, maxX);
        }

        // Add custom tick labels for common tenors
        if (first.TenorLabels.Count > 0)
        {
            var ticks = new ScottPlot.Tick[first.TenorLabels.Count];
            for (int i = 0; i < first.TenorLabels.Count; i++)
            {
                ticks[i] = new ScottPlot.Tick(first.TenorYearFractions[i], first.TenorLabels[i]);
            }
            plot.Axes.Bottom.SetTicks(ticks);
            plot.Axes.Bottom.TickLabelStyle.Rotation = -45;
            plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        }
    }

    /// <summary>
    /// Reset the chart to an empty state.
    /// </summary>
    public void Clear()
    {
        PlotControl.Plot.Clear();
        PlotControl.Refresh();
    }
}

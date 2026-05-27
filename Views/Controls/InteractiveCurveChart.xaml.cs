using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using ScottPlot;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.Views.Controls;

/// <summary>
/// Interactive curve chart wrapping ScottPlot 5.
/// Shows original curve and optional overridden curve.
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

        plot.Title("FX Forward Curve");
        plot.XLabel("Tenor");
        plot.YLabel("Rate (%)");
        plot.ShowLegend(Alignment.UpperRight);
        plot.Grid.MajorLineWidth = 0.5f;
        plot.Axes.SetLimits(-0.5, 32, 0, 6);
    }

    public void UpdateChart(
        List<CurveSnapshot> snapshots,
        string leftSeriesName,
        string rightSeriesName)
    {
        var plot = PlotControl.Plot;
        plot.Clear();

        bool hasRightSeries = !string.IsNullOrEmpty(rightSeriesName) &&
                              !rightSeriesName.Equals("None", StringComparison.OrdinalIgnoreCase);

        plot.Axes.Right.IsVisible = hasRightSeries;
        if (hasRightSeries)
        {
            plot.Axes.Right.Label.Text = SeriesToAxisLabel(rightSeriesName);
        }
        plot.Axes.Left.Label.Text = SeriesToAxisLabel(leftSeriesName);

        foreach (var snapshot in snapshots)
        {
            if (snapshot == null || snapshot.TenorYearFractions.Count == 0)
                continue;

            double[] xs = snapshot.TenorYearFractions.ToArray();

            // Left Y-axis series
            var leftSeries = snapshot.Series.FirstOrDefault(s => s.Name == leftSeriesName);
            if (leftSeries != null && leftSeries.Values.Count == xs.Length)
            {
                double[] ys = leftSeries.Values.Select(v => (double)v).ToArray();
                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Axes.YAxis = plot.Axes.Left;

                bool isOriginal = snapshot.Tag == "original";
                scatter.Color = isOriginal
                    ? new ScottPlot.Color(30, 144, 255)  // DodgerBlue
                    : new ScottPlot.Color(255, 69, 0);    // OrangeRed
                scatter.LineWidth = 2;
                scatter.LegendText = isOriginal
                    ? $"{leftSeriesName} (original)"
                    : $"{leftSeriesName} ({snapshot.Tag})";
                scatter.MarkerSize = isOriginal ? 4 : 6;
                scatter.MarkerShape = isOriginal ? MarkerShape.FilledCircle : MarkerShape.FilledDiamond;
            }

            // Right Y-axis series
            if (hasRightSeries)
            {
                var rightSeries = snapshot.Series.FirstOrDefault(s => s.Name == rightSeriesName);
                if (rightSeries != null && rightSeries.Values.Count == xs.Length)
                {
                    double[] ys = rightSeries.Values.Select(v => (double)v).ToArray();
                    var scatter2 = plot.Add.Scatter(xs, ys);
                    scatter2.Axes.YAxis = plot.Axes.Right;

                    bool isOriginal = snapshot.Tag == "original";
                    scatter2.Color = isOriginal
                        ? new ScottPlot.Color(60, 179, 113)  // MediumSeaGreen
                        : new ScottPlot.Color(148, 0, 211);  // DarkViolet
                    scatter2.LineWidth = 1.5f;
                    scatter2.LegendText = isOriginal
                        ? $"{rightSeriesName} (original)"
                        : $"{rightSeriesName} ({snapshot.Tag})";
                    scatter2.MarkerSize = isOriginal ? 3 : 5;
                    scatter2.MarkerShape = isOriginal ? MarkerShape.FilledSquare : MarkerShape.OpenSquare;
                }
            }
        }

        // Auto-scale Y axes
        AutoScaleY(plot, leftSeriesName, snapshots, isRight: false);
        if (hasRightSeries)
            AutoScaleY(plot, rightSeriesName, snapshots, isRight: true);

        // X axis — tenor labels
        ConfigureXAxis(plot, snapshots);

        PlotControl.Refresh();
    }

    private static string SeriesToAxisLabel(string seriesName) => seriesName switch
    {
        "Instantaneous Forward Rate" => "Forward Rate (%)",
        "Zero Rate" => "Zero Rate (%)",
        "Discount Factor" => "Discount Factor",
        "Par Rate" => "Par Rate (%)",
        _ => seriesName
    };

    private static void AutoScaleY(Plot plot, string seriesName, List<CurveSnapshot> snapshots, bool isRight)
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
        var axis = isRight ? plot.Axes.Right : plot.Axes.Left;
        axis.Min = minVal - padding;
        axis.Max = maxVal + padding;
    }

    private static void ConfigureXAxis(Plot plot, List<CurveSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return;
        var first = snapshots[0];
        if (first.TenorYearFractions.Count == 0) return;

        double[] positions = first.TenorYearFractions.ToArray();
        string[] labels = first.TenorLabels.ToArray();
        plot.Axes.Bottom.SetTicks(positions, labels);
        plot.Axes.Bottom.TickLabelStyle.Rotation = -45;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
    }

    public void Clear()
    {
        PlotControl.Plot.Clear();
        PlotControl.Refresh();
    }
}

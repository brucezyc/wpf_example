using System;
using System.Collections.Generic;

namespace WpfPlotMvp.Protocols;

/// <summary>
/// One snapshot of a stripped curve at a point in time.
/// Contains multiple series (InstantForward, ZeroRate, DiscountFactor, etc.)
/// so the UI can let the user choose what to plot on left/right Y-axis.
/// </summary>
public class CurveSnapshot
{
    public string Tag { get; set; } = "original";  // "original" | "overridden"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<string> TenorLabels { get; set; } = new();
    public List<double> TenorYearFractions { get; set; } = new();
    public List<CurveSeries> Series { get; set; } = new();
}

public class CurveSeries
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "%";
    public List<double> Values { get; set; } = new();
}

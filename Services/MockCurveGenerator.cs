using System;
using System.Collections.Generic;
using System.Linq;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.Services;

public class MockCurveGenerator
{
    private readonly Random _rng = new(42);
    private double _beta0 = 3.0;    // long-term rate level (%)
    private double _beta1 = 1.5;    // short-term deviation (%)
    private double _beta2 = -0.8;   // medium-term curvature (%)
    private double _tau = 2.5;      // scaling factor (years)

    // Small noise generators — correlated across tenors
    private double _commonDrift;   // common mode shift
    private double _slopeDrift;    // steepening/flattening
    private double[] _tenorNoise;  // per-tenor residual

    public static readonly (string Label, double YearFraction)[] DefaultTenors =
    {
        ("1W",  0.0192),
        ("1M",  0.0822),
        ("2M",  0.1644),
        ("3M",  0.2466),
        ("6M",  0.4932),
        ("9M",  0.7397),
        ("1Y",  1.0),
        ("2Y",  2.0),
        ("3Y",  3.0),
        ("5Y",  5.0),
        ("7Y",  7.0),
        ("10Y", 10.0),
        ("15Y", 15.0),
        ("20Y", 20.0),
        ("30Y", 30.0),
    };

    public MockCurveGenerator()
    {
        _tenorNoise = new double[DefaultTenors.Length];
    }

    /// <summary>
    /// Generate a clean deterministic baseline curve (overrideable).
    /// </summary>
    public CurveSnapshot GenerateBaseline(string tag = "original")
    {
        _commonDrift = 0;
        _slopeDrift = 0;
        Array.Clear(_tenorNoise, 0, _tenorNoise.Length);

        var snapshot = BuildSnapshotFromCurrentRates(tag);
        return snapshot;
    }

    /// <summary>
    /// Generate the next snapshot with small random perturbations.
    /// Perturbations are partially correlated across tenors to preserve smoothness.
    /// </summary>
    public CurveSnapshot GenerateNext(string tag = "original")
    {
        // Common mode: random walk (std ~ 0.01% per tick)
        _commonDrift += (_rng.NextDouble() - 0.5) * 0.02;

        // Slope: random walk (std ~ 0.005% per tick) — steepen/flatten
        _slopeDrift += (_rng.NextDouble() - 0.5) * 0.01;

        // Per-tenor residual: mean-reverting (0 → 0 over time)
        for (int i = 0; i < _tenorNoise.Length; i++)
        {
            _tenorNoise[i] = _tenorNoise[i] * 0.9 + (_rng.NextDouble() - 0.5) * 0.03;
        }

        return BuildSnapshotFromCurrentRates(tag);
    }

    /// <summary>
    /// Apply tenor-level overrides and re-interpolate.
    /// </summary>
    public CurveSnapshot ApplyOverride(CurveSnapshot baseline, (int tenorIndex, double newRate)[] overrides)
    {
        // 1. Start from baseline rates
        var baseSeries = baseline.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (baseSeries == null) return baseline;

        double[] rates = baseSeries.Values.ToArray();

        // 2. Apply overrides
        foreach (var (idx, rate) in overrides)
        {
            if (idx >= 0 && idx < rates.Length)
                rates[idx] = rate;
        }

        // 3. Re-smooth: linear interpolation of gaps, then light moving-average filter
        rates = SmoothOverriddenRates(rates);

        // 4. Recompute derived series
        return BuildSnapshotFromRates(rates, "overridden");
    }

    // ─── Internal ────────────────────────────────────────

    private CurveSnapshot BuildSnapshotFromCurrentRates(string tag)
    {
        double[] fwdRates = new double[DefaultTenors.Length];
        for (int i = 0; i < DefaultTenors.Length; i++)
        {
            double t = DefaultTenors[i].YearFraction;
            double baseRate = ForwardRate(t);
            double slopeEffect = _slopeDrift * Math.Exp(-t / _tau);   // slope decays with tenor
            fwdRates[i] = baseRate + _commonDrift + slopeEffect + _tenorNoise[i];
        }

        return BuildSnapshotFromRates(fwdRates, tag);
    }

    private CurveSnapshot BuildSnapshotFromRates(double[] fwdRates, string tag)
    {
        var labels = new List<string>();
        var yearFracs = new List<double>();
        var fwdSeries = new CurveSeries { Name = "Instantaneous Forward Rate", Unit = "%", Values = new List<double>() };
        var zeroSeries = new CurveSeries { Name = "Zero Rate", Unit = "%", Values = new List<double>() };
        var dfSeries = new CurveSeries { Name = "Discount Factor", Unit = "", Values = new List<double>() };
        var parSeries = new CurveSeries { Name = "Par Rate", Unit = "%", Values = new List<double>() };

        double cumulativeDiscount = 1.0;
        double prevTime = 0;

        for (int i = 0; i < DefaultTenors.Length; i++)
        {
            var (label, yearFrac) = DefaultTenors[i];
            labels.Add(label);
            yearFracs.Add(yearFrac);

            double fwd = fwdRates[i];
            fwdSeries.Values.Add(Math.Round(fwd, 4));

            // Zero rate: bootstrap from forward rates (piecewise constant)
            double dt = yearFrac - prevTime;
            cumulativeDiscount *= Math.Exp(-fwd / 100.0 * dt);
            double zeroRate = -Math.Log(cumulativeDiscount) / yearFrac * 100.0;
            zeroSeries.Values.Add(Math.Round(zeroRate, 4));

            dfSeries.Values.Add(Math.Round(cumulativeDiscount, 6));

            // Par rate ≈ zero rate (simplified)
            parSeries.Values.Add(Math.Round(zeroRate, 4));

            prevTime = yearFrac;
        }

        return new CurveSnapshot
        {
            Tag = tag,
            Timestamp = DateTime.UtcNow,
            TenorLabels = labels,
            TenorYearFractions = yearFracs,
            Series = new List<CurveSeries> { fwdSeries, zeroSeries, dfSeries, parSeries }
        };
    }

    private double[] SmoothOverriddenRates(double[] rates)
    {
        // Simple moving-average filter (3-point) to smooth out kinks from partial overrides
        var smoothed = new double[rates.Length];
        for (int i = 0; i < rates.Length; i++)
        {
            if (i == 0) smoothed[i] = (rates[i] + rates[i + 1]) / 2;
            else if (i == rates.Length - 1) smoothed[i] = (rates[i - 1] + rates[i]) / 2;
            else smoothed[i] = (rates[i - 1] + rates[i] + rates[i + 1]) / 3;
        }
        return smoothed;
    }

    // ─── Nelson-Siegel formulas ──────────────────────────

    /// <summary>
    /// Nelson-Siegel instantaneous forward rate at time t (years)
    /// f(t) = β0 + β1 * exp(-t/τ) + β2 * (t/τ) * exp(-t/τ)
    /// </summary>
    public double ForwardRate(double t)
    {
        if (t < 1e-10) return _beta0 + _beta1;
        return _beta0 + _beta1 * Math.Exp(-t / _tau) + _beta2 * (t / _tau) * Math.Exp(-t / _tau);
    }

    /// <summary>
    /// Nelson-Siegel zero-coupon rate at time t (years)
    /// r(t) = β0 + (β1 + β2) * (1 - exp(-t/τ)) / (t/τ) - β2 * exp(-t/τ)
    /// </summary>
    public double ZeroRate(double t)
    {
        if (t < 1e-10) return _beta0 + _beta1;
        double x = t / _tau;
        double expNegX = Math.Exp(-x);
        return _beta0 + (_beta1 + _beta2) * (1 - expNegX) / x - _beta2 * expNegX;
    }
}

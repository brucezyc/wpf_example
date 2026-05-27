using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.ViewModels;

public class TenorOverrideItem : ObservableObject
{
    public string Label { get; set; } = "";

    private double _currentRate;
    public double CurrentRate
    {
        get => _currentRate;
        set => SetProperty(ref _currentRate, value);
    }

    private double? _overrideRate;
    public double? OverrideRate
    {
        get => _overrideRate;
        set
        {
            if (SetProperty(ref _overrideRate, value))
                OnPropertyChanged(nameof(IsOverridden));
        }
    }

    public bool IsOverridden => OverrideRate.HasValue;

    public void ClearOverride()
    {
        OverrideRate = null;
    }
}

/// <summary>
/// ViewModel for the override panel (right side of PricingPage).
/// Three tabs: Params, XML (view-only), XML (editable override).
/// </summary>
public partial class OverridePanelViewModel : ObservableObject
{
    private readonly string[] _tenorLabels =
        { "1W", "1M", "2M", "3M", "6M", "9M", "1Y", "2Y", "3Y", "5Y", "7Y", "10Y", "15Y", "20Y", "30Y" };

    public event Action<ApplyOverrideRequest>? OverrideSubmitted;
    public event Action? OverrideCleared;

    [ObservableProperty]
    private int _selectedTab;

    /// <summary>
    /// Read-only original XML — generated from the latest curve snapshot.
    /// </summary>
    [ObservableProperty]
    private string _originalXml = "";

    /// <summary>
    /// Editable override XML — pre-loaded with original, user edits and applies.
    /// </summary>
    [ObservableProperty]
    private string _overrideXml = "";

    [ObservableProperty]
    private bool _isOverrideActive;

    public ObservableCollection<TenorOverrideItem> Tenors { get; } = new();

    public OverridePanelViewModel()
    {
        foreach (var label in _tenorLabels)
        {
            Tenors.Add(new TenorOverrideItem { Label = label, CurrentRate = 0 });
        }
    }

    /// <summary>
    /// Called by PricingPageViewModel when a new curve snapshot arrives.
    /// Updates current rates in the params tab and regenerates original XML.
    /// </summary>
    public void UpdateRates(CurveSnapshot snapshot)
    {
        var fwdSeries = snapshot.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (fwdSeries == null) return;

        for (int i = 0; i < Tenors.Count && i < fwdSeries.Values.Count; i++)
        {
            Tenors[i].CurrentRate = fwdSeries.Values[i];
        }

        // Regenerate original XML from snapshot
        OriginalXml = GenerateCurveXml(snapshot);

        // If override XML hasn't been set yet, pre-load with original
        if (string.IsNullOrEmpty(OverrideXml) && !IsOverrideActive)
        {
            OverrideXml = OriginalXml;
        }
    }

    [RelayCommand]
    private void ApplyOverride()
    {
        if (SelectedTab == 0)
        {
            // Structured params mode
            var overrides = Tenors
                .Where(t => t.OverrideRate.HasValue)
                .Select(t => (t.Label, Rate: t.OverrideRate!.Value))
                .ToList();

            if (overrides.Count == 0) return;

            var payload = string.Join(";",
                overrides.Select(o => $"{o.Label}={o.Rate:F4}"));

            OverrideSubmitted?.Invoke(new ApplyOverrideRequest
            {
                OverrideType = "ParamOverride",
                Payload = payload
            });
        }
        else
        {
            // XML override mode (tabs 1 and 2 both submit from override XML)
            if (string.IsNullOrWhiteSpace(OverrideXml)) return;

            OverrideSubmitted?.Invoke(new ApplyOverrideRequest
            {
                OverrideType = "XmlSnippet",
                Payload = OverrideXml
            });
        }

        IsOverrideActive = true;
    }

    [RelayCommand]
    private void ReloadOriginalXml()
    {
        if (!string.IsNullOrEmpty(OriginalXml))
        {
            OverrideXml = OriginalXml;
        }
    }

    [RelayCommand]
    private void ClearOverride()
    {
        foreach (var tenor in Tenors)
            tenor.ClearOverride();

        IsOverrideActive = false;

        // Restore override XML to original
        if (!string.IsNullOrEmpty(OriginalXml))
            OverrideXml = OriginalXml;

        OverrideCleared?.Invoke();
    }

    // ─── XML Generation ──────────────────────────────────

    private string GenerateCurveXml(CurveSnapshot snapshot)
    {
        var fwdSeries = snapshot.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (fwdSeries == null) return "<!-- No curve data -->";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<curveModel>");
        sb.AppendLine("  <metadata>");
        sb.AppendLine($"    <timestamp>{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss.fff}</timestamp>");
        sb.AppendLine("    <method>NelsonSiegel</method>");
        sb.AppendLine("    <tau>2.5</tau>");
        sb.AppendLine("  </metadata>");
        sb.AppendLine("  <pillars>");

        for (int i = 0; i < fwdSeries.Values.Count && i < snapshot.TenorLabels.Count; i++)
        {
            sb.AppendLine($"    <pillar tenor=\"{snapshot.TenorLabels[i]}\">{fwdSeries.Values[i]:F4}</pillar>");
        }

        sb.AppendLine("  </pillars>");
        sb.AppendLine("</curveModel>");
        return sb.ToString();
    }
}

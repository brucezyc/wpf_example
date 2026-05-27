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
/// Supports two modes: XML snippet editing and structured tenor-level overrides.
/// </summary>
public partial class OverridePanelViewModel : ObservableObject
{
    private readonly string[] _tenorLabels =
        { "1W", "1M", "2M", "3M", "6M", "9M", "1Y", "2Y", "3Y", "5Y", "7Y", "10Y", "15Y", "20Y", "30Y" };

    /// <summary>
    /// Fired when user clicks Apply — PricingPageViewModel sends to worker.
    /// </summary>
    public event Action<ApplyOverrideRequest>? OverrideSubmitted;

    /// <summary>
    /// Fired when user clicks Clear.
    /// </summary>
    public event Action? OverrideCleared;

    [ObservableProperty]
    private int _selectedTab;   // 0 = Structured params, 1 = XML

    [ObservableProperty]
    private string _xmlText = "";

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
    /// Called by PricingPageViewModel when a new curve snapshot arrives,
    /// to update the current rates shown in the override panel.
    /// </summary>
    public void UpdateRates(CurveSnapshot snapshot)
    {
        var fwdSeries = snapshot.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (fwdSeries == null) return;

        for (int i = 0; i < Tenors.Count && i < fwdSeries.Values.Count; i++)
        {
            Tenors[i].CurrentRate = fwdSeries.Values[i];
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

            // Serialize as JSON-like payload
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
            // XML mode
            if (string.IsNullOrWhiteSpace(XmlText)) return;

            OverrideSubmitted?.Invoke(new ApplyOverrideRequest
            {
                OverrideType = "XmlSnippet",
                Payload = XmlText
            });
        }

        IsOverrideActive = true;
    }

    [RelayCommand]
    private void ClearOverride()
    {
        foreach (var tenor in Tenors)
            tenor.ClearOverride();

        XmlText = "";
        IsOverrideActive = false;
        OverrideCleared?.Invoke();
    }

    /// <summary>
    /// Parse a "Label1=Rate1;Label2=Rate2" payload into per-tenor override values.
    /// Called by PricingPageViewModel when re-applying state or syncing.
    /// </summary>
    public void ApplyPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        var parts = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=');
            if (kv.Length != 2) continue;
            if (!double.TryParse(kv[1], out double rate)) continue;

            var tenor = Tenors.FirstOrDefault(t =>
                t.Label.Equals(kv[0], StringComparison.OrdinalIgnoreCase));
            if (tenor != null)
                tenor.OverrideRate = rate;
        }

        IsOverrideActive = true;
    }
}

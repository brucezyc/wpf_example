using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.ViewModels;

public class TenorOverrideItem : ObservableObject
{
    public string Label { get; set; } = "";
    public bool IsCustom { get; set; }

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

    public void ClearOverride() => OverrideRate = null;
}

public partial class OverridePanelViewModel : ObservableObject
{
    private readonly string[] _defaultTenorLabels =
        { "1W", "1M", "2M", "3M", "6M", "9M", "1Y", "2Y", "3Y", "5Y", "7Y", "10Y", "15Y", "20Y", "30Y" };

    // Track which default tenors were removed by the user
    private readonly HashSet<string> _removedTenorLabels = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ApplyOverrideRequest>? OverrideSubmitted;
    public event Action? OverrideCleared;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _originalXml = "";
    [ObservableProperty] private string _overrideXml = "";
    [ObservableProperty] private bool _isOverrideActive;

    [ObservableProperty] private string _newTenorLabel = "";
    [ObservableProperty] private string _newTenorRate = "";

    public ObservableCollection<TenorOverrideItem> Tenors { get; } = new();

    public OverridePanelViewModel()
    {
        foreach (var label in _defaultTenorLabels)
            Tenors.Add(new TenorOverrideItem { Label = label });
    }

    public void UpdateRates(CurveSnapshot snapshot)
    {
        var fwdSeries = snapshot.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (fwdSeries == null) return;

        var rateMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _defaultTenorLabels.Length && i < fwdSeries.Values.Count; i++)
            rateMap[_defaultTenorLabels[i]] = fwdSeries.Values[i];

        foreach (var tenor in Tenors)
        {
            if (rateMap.TryGetValue(tenor.Label, out double rate))
                tenor.CurrentRate = rate;
        }

        OriginalXml = GenerateCurveXml(snapshot);

        if (string.IsNullOrEmpty(OverrideXml) && !IsOverrideActive)
            OverrideXml = OriginalXml;
    }

    // ─── Add / Remove Tenor ──────────────────────────────

    [RelayCommand]
    private void AddTenor()
    {
        string label = NewTenorLabel?.Trim() ?? "";
        if (string.IsNullOrEmpty(label)) return;
        if (!double.TryParse(NewTenorRate, out double rate)) rate = 0;
        if (Tenors.Any(t => t.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) return;

        Tenors.Add(new TenorOverrideItem
        {
            Label = label, CurrentRate = rate,
            OverrideRate = rate, IsCustom = true
        });
        // If it was previously removed from defaults, un-track it
        _removedTenorLabels.Remove(label);

        NewTenorLabel = "";
        NewTenorRate = "";
    }

    [RelayCommand]
    private void RemoveTenor(TenorOverrideItem? tenor)
    {
        if (tenor == null) return;
        if (!tenor.IsCustom)
            _removedTenorLabels.Add(tenor.Label);
        Tenors.Remove(tenor);
    }

    // ─── Apply / Clear ───────────────────────────────────

    [RelayCommand]
    private void ApplyOverride()
    {
        if (SelectedTab == 0)
        {
            // Parts with '=' → override; without '=' → remove
            var parts = new List<string>();

            foreach (var t in Tenors.Where(t => t.OverrideRate.HasValue))
                parts.Add($"{t.Label}={t.OverrideRate!.Value:F4}");

            // Add removed tenors (no '=' sign)
            foreach (var label in _removedTenorLabels)
                parts.Add(label);

            if (parts.Count == 0) return;

            var payload = string.Join(";", parts);

            OverrideSubmitted?.Invoke(new ApplyOverrideRequest
            {
                OverrideType = "ParamOverride",
                Payload = payload
            });
        }
        else
        {
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
            OverrideXml = OriginalXml;
    }

    [RelayCommand]
    private void ClearOverride()
    {
        foreach (var tenor in Tenors)
            tenor.ClearOverride();

        // Restore any removed default tenors
        foreach (var label in _defaultTenorLabels)
        {
            if (_removedTenorLabels.Contains(label) &&
                !Tenors.Any(t => t.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
            {
                Tenors.Add(new TenorOverrideItem { Label = label });
            }
        }
        _removedTenorLabels.Clear();

        IsOverrideActive = false;

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
            sb.AppendLine($"    <pillar tenor=\"{snapshot.TenorLabels[i]}\">{fwdSeries.Values[i]:F4}</pillar>");
        sb.AppendLine("  </pillars>");
        sb.AppendLine("</curveModel>");
        return sb.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfPlotMvp.Protocols;
using WpfPlotMvp.Workers;

namespace WpfPlotMvp.ViewModels;

public partial class PricingPageViewModel : ObservableObject, IDisposable
{
    private readonly IWorkerClient _worker;
    private CancellationTokenSource? _cts;

    // ─── Observable Properties ───────────────────────────

    [ObservableProperty]
    private string _symbol = "EURUSD";

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _currentPrice = "—";

    [ObservableProperty]
    private bool _isStreaming;

    // Available series for dropdowns
    public List<string> AvailableSeries { get; } = new()
    {
        "Instantaneous Forward Rate",
        "Zero Rate",
        "Discount Factor",
        "Par Rate"
    };

    [ObservableProperty]
    private string _leftYSeries = "Instantaneous Forward Rate";

    [ObservableProperty]
    private string _rightYSeries = "None";

    // Right axis includes "None" option
    public List<string> RightAxisOptions { get; } = new()
    {
        "None",
        "Instantaneous Forward Rate",
        "Zero Rate",
        "Discount Factor",
        "Par Rate"
    };

    // Latest snapshots for chart rendering
    private CurveSnapshot? _latestOriginal;
    private CurveSnapshot? _latestOverridden;

    public PricingPageViewModel()
        : this(new InProcessMockWorker())
    {
    }

    public PricingPageViewModel(IWorkerClient worker)
    {
        _worker = worker;
        _worker.SnapshotReceived += OnSnapshotReceived;
        _worker.StatusChanged += OnStatusChanged;
        _worker.PriceTickReceived += OnPriceTick;
    }

    // ─── Commands ────────────────────────────────────────

    [RelayCommand]
    private async Task StartStream()
    {
        if (IsStreaming) return;

        _cts = new CancellationTokenSource();
        IsStreaming = true;

        try
        {
            await _worker.StartAsync(Symbol, "<mock-config-xml/>", _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private async Task StopStream()
    {
        if (!IsStreaming) return;

        IsStreaming = false;
        _cts?.Cancel();

        await _worker.StopAsync();
        StatusText = "Stopped";
    }

    // ─── Worker Event Handlers ───────────────────────────

    private void OnSnapshotReceived(object? sender, CurveSnapshot snapshot)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (snapshot.Tag == "original" || snapshot.Tag == "overridden")
            {
                if (snapshot.Tag == "original")
                    _latestOriginal = snapshot;
                else
                    _latestOverridden = snapshot;

                RequestChartUpdate();
            }
        });
    }

    private void OnStatusChanged(object? sender, StatusMessage msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrEmpty(msg.Detail))
                StatusText = $"{msg.Status}: {msg.Detail}";
            else
                StatusText = msg.Status.ToString();
        });
    }

    private void OnPriceTick(object? sender, double price)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentPrice = price.ToString("F4");
        });
    }

    // ─── Chart Update ────────────────────────────────────

    // Reference to the chart control — set by the View
    public Action<List<CurveSnapshot>, string, string>? UpdateChartAction { get; set; }

    private bool _chartUpdatePending;

    private void RequestChartUpdate()
    {
        if (_chartUpdatePending) return;
        _chartUpdatePending = true;

        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _chartUpdatePending = false;
            PushChartUpdate();
        }));
    }

    private void PushChartUpdate()
    {
        if (UpdateChartAction == null) return;

        var snapshots = new List<CurveSnapshot>();
        if (_latestOriginal != null)
            snapshots.Add(_latestOriginal);
        if (_latestOverridden != null)
            snapshots.Add(_latestOverridden);

        if (snapshots.Count == 0) return;

        string left = LeftYSeries;
        string right = RightYSeries == "None" ? "" : RightYSeries;

        UpdateChartAction(snapshots, left, right);
    }

    partial void OnLeftYSeriesChanged(string value)
    {
        PushChartUpdate();
    }

    partial void OnRightYSeriesChanged(string value)
    {
        PushChartUpdate();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _worker.Dispose();
    }
}

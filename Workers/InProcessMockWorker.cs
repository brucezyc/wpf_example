using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpfPlotMvp.Protocols;
using WpfPlotMvp.Services;

namespace WpfPlotMvp.Workers;

/// <summary>
/// Dev-mode worker: runs in-process with mock data.
/// </summary>
public class InProcessMockWorker : IWorkerClient
{
    public event EventHandler<CurveSnapshot>? SnapshotReceived;
    public event EventHandler<double>? PriceTickReceived;
    public event EventHandler<StatusMessage>? StatusChanged;

    private readonly MockCurveGenerator _generator = new();
    private CancellationTokenSource? _cts;

    // Override state
    private bool _overrideActive;
    private CurveSnapshot? _baseline;
    private (int tenorIndex, double newRate)[]? _currentOverrides;

    public async Task StartAsync(string symbol, string configXml, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        EmitStatus(WorkerStatus.ConfigLoading);
        await Task.Delay(200);

        _baseline = _generator.GenerateBaseline();

        EmitStatus(WorkerStatus.ConfigLoaded);
        await Task.Delay(100);

        EmitStatus(WorkerStatus.Subscribing);
        await Task.Delay(100);

        EmitStatus(WorkerStatus.Subscribed);
        await Task.Delay(50);

        _ = Task.Run(() => StreamLoop(_cts.Token), _cts.Token);
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        EmitStatus(WorkerStatus.Stopped);
        return Task.CompletedTask;
    }

    public Task ApplyOverrideAsync(ApplyOverrideRequest request)
    {
        if (_baseline == null) return Task.CompletedTask;

        if (request.OverrideType == "ParamOverride")
        {
            // Parse "1M=4.50;10Y=2.80" payload
            var parts = request.Payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var overrides = new System.Collections.Generic.List<(int idx, double rate)>();

            foreach (var part in parts)
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                if (!double.TryParse(kv[1], out double rate)) continue;

                // Find tenor index by label
                for (int i = 0; i < MockCurveGenerator.DefaultTenors.Length; i++)
                {
                    if (MockCurveGenerator.DefaultTenors[i].Label.Equals(kv[0], StringComparison.OrdinalIgnoreCase))
                    {
                        overrides.Add((i, rate));
                        break;
                    }
                }
            }

            if (overrides.Count > 0)
            {
                _currentOverrides = overrides.ToArray();
                _overrideActive = true;
                EmitStatus(WorkerStatus.OverrideActive, $"Overrode {overrides.Count} tenors");
            }
        }
        else if (request.OverrideType == "XmlSnippet")
        {
            // Parse XML to extract pillar overrides
            var overrides = new System.Collections.Generic.List<(int idx, double rate)>();
            var xml = request.Payload;

            foreach (var tenor in MockCurveGenerator.DefaultTenors)
            {
                // Look for <pillar tenor="1M">value</pillar>
                var searchStr = $"tenor=\"{tenor.Label}\"";
                int idx = xml.IndexOf(searchStr, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                // Find the value between > and </
                int valueStart = xml.IndexOf('>', idx + searchStr.Length);
                int valueEnd = xml.IndexOf('<', valueStart + 1);
                if (valueStart < 0 || valueEnd < 0) continue;

                valueStart++; // skip '>'
                string valueStr = xml[valueStart..valueEnd].Trim();
                if (double.TryParse(valueStr, out double rate))
                {
                    // Find tenor index by label
                    for (int i = 0; i < MockCurveGenerator.DefaultTenors.Length; i++)
                    {
                        if (MockCurveGenerator.DefaultTenors[i].Label.Equals(tenor.Label, StringComparison.OrdinalIgnoreCase))
                        {
                            overrides.Add((i, rate));
                            break;
                        }
                    }
                }
            }

            if (overrides.Count > 0)
            {
                _currentOverrides = overrides.ToArray();
                _overrideActive = true;
                EmitStatus(WorkerStatus.OverrideActive, $"XML override: {overrides.Count} tenors");
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync()
    {
        _overrideActive = false;
        _currentOverrides = null;
        EmitStatus(WorkerStatus.Streaming, "Override cleared");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    // ─── Internal ────────────────────────────────────────

    private async Task StreamLoop(CancellationToken ct)
    {
        EmitStatus(WorkerStatus.Streaming);

        while (!ct.IsCancellationRequested)
        {
            // Generate original curve
            var snapshot = _generator.GenerateNext("original");
            SnapshotReceived?.Invoke(this, snapshot);

            // If override active, generate overridden curve
            if (_overrideActive && _currentOverrides != null)
            {
                var overridden = _generator.ApplyOverride(snapshot, _currentOverrides);
                SnapshotReceived?.Invoke(this, overridden);
            }

            // Price tick from last tenor
            if (snapshot.Series.Count > 0 && snapshot.Series[0].Values.Count > 0)
            {
                double lastRate = snapshot.Series[0].Values[^1];
                PriceTickReceived?.Invoke(this, Math.Round(lastRate, 4));
            }

            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        EmitStatus(WorkerStatus.Stopped);
    }

    private void EmitStatus(WorkerStatus status, string detail = "")
    {
        StatusChanged?.Invoke(this, new StatusMessage { Status = status, Detail = detail });
    }
}

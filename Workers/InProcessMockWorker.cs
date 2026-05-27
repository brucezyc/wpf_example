using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpfPlotMvp.Protocols;
using WpfPlotMvp.Services;

namespace WpfPlotMvp.Workers;

public class InProcessMockWorker : IWorkerClient
{
    public event EventHandler<CurveSnapshot>? SnapshotReceived;
    public event EventHandler<double>? PriceTickReceived;
    public event EventHandler<StatusMessage>? StatusChanged;

    private readonly MockCurveGenerator _generator = new();
    private CancellationTokenSource? _cts;
    private CurveSnapshot? _baseline;

    // Override state
    private bool _overrideActive;
    private (int tenorIndex, double newRate)[]? _overrideRate;  // tenors with new values
    private int[]? _excludeIndices;  // tenors to remove from curve

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
            var parts = request.Payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var overrideList = new List<(int idx, double rate)>();
            var excludeList = new List<int>();

            foreach (var part in parts)
            {
                var kv = part.Split('=');
                string label = kv[0].Trim();
                int? tenorIdx = FindTenorIndex(label);
                if (tenorIdx == null) continue;

                if (kv.Length == 2 && double.TryParse(kv[1], out double rate))
                    overrideList.Add(((int)tenorIdx, rate));
                else
                    excludeList.Add((int)tenorIdx);
            }

            _overrideRate = overrideList.ToArray();
            _excludeIndices = excludeList.ToArray();
            _overrideActive = (_overrideRate.Length > 0 || _excludeIndices.Length > 0);

            if (_overrideActive)
                EmitStatus(WorkerStatus.OverrideActive,
                    $"Override: {_overrideRate.Length} rates, {_excludeIndices.Length} removed");
        }
        else if (request.OverrideType == "XmlSnippet")
        {
            var overrideList = new List<(int idx, double rate)>();
            var xml = request.Payload;

            foreach (var tenor in MockCurveGenerator.DefaultTenors)
            {
                var searchStr = $"tenor=\"{tenor.Label}\"";
                int idx = xml.IndexOf(searchStr, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                int valueStart = xml.IndexOf('>', idx + searchStr.Length);
                int valueEnd = xml.IndexOf('<', valueStart + 1);
                if (valueStart < 0 || valueEnd < 0) continue;

                valueStart++;
                string valueStr = xml[valueStart..valueEnd].Trim();
                if (double.TryParse(valueStr, out double rate))
                {
                    int? tenorIdx = FindTenorIndex(tenor.Label);
                    if (tenorIdx != null)
                        overrideList.Add(((int)tenorIdx, rate));
                }
            }

            _overrideRate = overrideList.ToArray();
            _excludeIndices = Array.Empty<int>();
            _overrideActive = _overrideRate.Length > 0;

            if (_overrideActive)
                EmitStatus(WorkerStatus.OverrideActive, $"XML override: {_overrideRate.Length} tenors");
        }

        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync()
    {
        _overrideActive = false;
        _overrideRate = null;
        _excludeIndices = null;
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
            var snapshot = _generator.GenerateNext("original");
            SnapshotReceived?.Invoke(this, snapshot);

            if (_overrideActive)
            {
                var overridden = _generator.ApplyOverride(
                    snapshot,
                    _overrideRate,
                    _excludeIndices);
                SnapshotReceived?.Invoke(this, overridden);
            }

            if (snapshot.Series.Count > 0 && snapshot.Series[0].Values.Count > 0)
            {
                double lastRate = snapshot.Series[0].Values[^1];
                PriceTickReceived?.Invoke(this, Math.Round(lastRate, 4));
            }

            try { await Task.Delay(200, ct); }
            catch (OperationCanceledException) { break; }
        }

        EmitStatus(WorkerStatus.Stopped);
    }

    private int? FindTenorIndex(string label)
    {
        for (int i = 0; i < MockCurveGenerator.DefaultTenors.Length; i++)
        {
            if (MockCurveGenerator.DefaultTenors[i].Label.Equals(label, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return null;
    }

    private void EmitStatus(WorkerStatus status, string detail = "")
    {
        StatusChanged?.Invoke(this, new StatusMessage { Status = status, Detail = detail });
    }
}

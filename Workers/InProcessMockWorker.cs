using System;
using System.Threading;
using System.Threading.Tasks;
using WpfPlotMvp.Protocols;
using WpfPlotMvp.Services;

namespace WpfPlotMvp.Workers;

/// <summary>
/// Dev-mode worker: runs in-process with mock data.
/// No Solace, no named pipe, no external dependency DLL.
/// Replaces with NamedPipeWorkerClient when integrating real backend.
/// </summary>
public class InProcessMockWorker : IWorkerClient
{
    public event EventHandler<CurveSnapshot>? SnapshotReceived;
    public event EventHandler<double>? PriceTickReceived;
    public event EventHandler<StatusMessage>? StatusChanged;

    private readonly MockCurveGenerator _generator = new();
    private CancellationTokenSource? _cts;
    private CurveSnapshot? _baseline;

    public async Task StartAsync(string symbol, string configXml, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        EmitStatus(WorkerStatus.ConfigLoading);
        await Task.Delay(200);  // simulate config load

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
        EmitStatus(WorkerStatus.OverrideActive, $"Override: {request.OverrideType}");
        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync()
    {
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
            var snapshot = _generator.GenerateNext();
            snapshot.Tag = "original";
            SnapshotReceived?.Invoke(this, snapshot);

            // Emit a price tick from the last tenor's forward rate
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

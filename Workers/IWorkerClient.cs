using System;
using System.Threading;
using System.Threading.Tasks;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.Workers;

public interface IWorkerClient : IDisposable
{
    event EventHandler<CurveSnapshot>? SnapshotReceived;
    event EventHandler<double>? PriceTickReceived;
    event EventHandler<StatusMessage>? StatusChanged;

    Task StartAsync(string symbol, string configXml, CancellationToken ct);
    Task StopAsync();
    Task ApplyOverrideAsync(ApplyOverrideRequest request);
    Task ClearOverrideAsync();
}

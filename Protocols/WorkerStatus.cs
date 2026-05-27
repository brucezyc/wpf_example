namespace WpfPlotMvp.Protocols;

public enum WorkerStatus
{
    Idle,
    ConfigLoading,
    ConfigLoaded,
    Subscribing,
    Subscribed,
    Streaming,
    OverrideActive,
    Error,
    Stopped
}

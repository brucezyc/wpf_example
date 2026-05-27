namespace WpfPlotMvp.Protocols;

// ─── UI → Worker ───────────────────────────────────────

public class StartStreamRequest
{
    public string Symbol { get; set; } = "";
    public string ConfigXml { get; set; } = "";
}

public class StopStreamRequest { }

public class ApplyOverrideRequest
{
    public string OverrideType { get; set; } = "";  // "XmlSnippet" | "ParamOverride" | "AddTenor"
    public string Payload { get; set; } = "";
}

public class ClearOverrideRequest { }

// ─── Worker → UI ───────────────────────────────────────

public class CurveSnapshotMessage
{
    public CurveSnapshot Snapshot { get; set; } = new();
}

public class PriceTickMessage
{
    public double Price { get; set; }
    public DateTime Timestamp { get; set; }
}

public class StatusMessage
{
    public WorkerStatus Status { get; set; }
    public string Detail { get; set; } = "";
}

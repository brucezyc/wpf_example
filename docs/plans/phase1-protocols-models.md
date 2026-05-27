# Phase 1: Protocols + Data Models + Mock Curve Generator

> **For Hermes:** implement task-by-task, push after each task.

**Goal:** Define the communication protocol, core data structures, and a Nelson-Siegel mock curve generator — all testable without UI, Solace, or named pipes.

**Architecture:** Contracts-first. Protocols/ → pure C# interfaces & messages. Models/ → immutable data structures. Services/MockCurveGenerator → standalone pure math. Everything in this phase has zero WPF dependency.

**Tech Stack:** .NET 8.0, Newtonsoft.Json (for named pipe serialization), Math.NET Numerics (for Nelson-Siegel math)

---

## Task 1: Create Protocol & Message Types

**Objective:** Define the data contracts that UI and Worker use to communicate.

**Files:**
- Create: `Protocols/CurveSnapshot.cs`
- Create: `Protocols/WorkerMessages.cs`
- Create: `Protocols/WorkerStatus.cs`

### Step 1: Write `CurveSnapshot.cs`

```csharp
using System;
using System.Collections.Generic;

namespace WpfPlotMvp.Protocols;

/// <summary>
/// One snapshot of a stripped curve at a point in time.
/// Contains multiple series (InstantForward, ZeroRate, DiscountFactor, etc.)
/// so the UI can let the user choose what to plot on left/right Y-axis.
/// </summary>
public class CurveSnapshot
{
    public string Tag { get; set; } = "original";  // "original" | "overridden"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<string> TenorLabels { get; set; } = new();  // ["1W","1M","3M",...]
    public List<double> TenorYearFractions { get; set; } = new();
    public List<CurveSeries> Series { get; set; } = new();
}

public class CurveSeries
{
    public string Name { get; set; } = "";      // "Instantaneous Forward Rate"
    public string Unit { get; set; } = "%";      // "%" | "bp" | ""
    public List<double> Values { get; set; } = new();
}
```

### Step 2: Write `WorkerMessages.cs`

```csharp
namespace WpfPlotMvp.Protocols;

// UI → Worker
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

// Worker → UI
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
```

### Step 3: Write `WorkerStatus.cs`

```csharp
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
```

### Step 4: Verify compilation context

Run: `dotnet build`
Expected: PASS (these are plain classes, no dependencies)

### Step 5: git commit

```bash
git add Protocols/
git commit -m "feat: add protocol types and messages for worker communication"
```

---

## Task 2: Add Newtonsoft.Json dependency & serialization helpers

**Objective:** Ensure protocol types can serialize/deserialize over named pipe.

**Files:**
- Modify: `WpfPlotMvp.csproj`
- Create: `Protocols/MessageSerializer.cs`

### Step 1: Add NuGet package

```bash
dotnet add package Newtonsoft.Json
```

### Step 2: Write `MessageSerializer.cs`

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WpfPlotMvp.Protocols;

public static class MessageSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Converters = { new StringEnumConverter() },
        Formatting = Formatting.None
    };

    public static string Serialize<T>(T message) =>
        JsonSerializer.Create(Settings).Serialize(message);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Create(Settings).Deserialize<T>(json);
}
```

### Step 3: Build & commit

```bash
dotnet build
git add Protocols/ WpfPlotMvp.csproj
git commit -m "feat: add Newtonsoft.Json and message serializer"
```

---

## Task 3: Core math — Nelson-Siegel Curve Generator

**Objective:** Mock curve generator that produces realistic-looking FX forward curves.

**Files:**
- Create: `Services/MockCurveGenerator.cs`
- Create: `Tests/Services/MockCurveGeneratorTests.cs` (console test)

### Step 1: Tenor definitions

Standard FX tenors:
```
1W:  7 days     (0.0192y)
1M:  30 days    (0.0822y)
2M:  60 days    (0.1644y)
3M:  90 days    (0.2466y)
6M:  180 days   (0.4932y)
9M:  270 days   (0.7397y)
1Y:  365 days   (1.0y)
2Y:  730 days   (2.0y)
3Y:  1095 days  (3.0y)
5Y:  1825 days  (5.0y)
7Y:  2555 days  (7.0y)
10Y: 3650 days  (10.0y)
15Y: 5475 days  (15.0y)
20Y: 7300 days  (20.0y)
30Y: 10950 days (30.0y)
```

### Step 2: Nelson-Siegel formula

```
r(t) = β0 + β1 * (1 - exp(-t/τ)) / (t/τ) + β2 * ((1 - exp(-t/τ)) / (t/τ) - exp(-t/τ))

where:
  β0 = long-term rate (level)       ~3.0%
  β1 = short-term rate deviation     ~1.5%
  β2 = medium-term curvature         ~-0.8%
  τ  = scaling factor                ~2.5
  t  = time to maturity in years
```

From zero rates, compute instantaneous forward rates:
```
f(t) = β0 + β1 * exp(-t/τ) + β2 * (t/τ) * exp(-t/τ)
```

### Step 3: Code structure

```csharp
using System;
using System.Collections.Generic;

namespace WpfPlotMvp.Services;

public class MockCurveGenerator
{
    private readonly Random _rng = new();
    private double _beta0 = 3.0;    // long-term level (%)
    private double _beta1 = 1.5;    // short-term deviation (%)
    private double _beta2 = -0.8;   // curvature (%)
    private double _tau = 2.5;      // scaling factor

    public static readonly (string Label, double YearFraction)[] DefaultTenors =
    {
        ("1W",  0.0192),
        ("1M",  0.0822),
        ("2M",  0.1644),
        ("3M",  0.2466),
        ("6M",  0.4932),
        ("9M",  0.7397),
        ("1Y",  1.0),
        ("2Y",  2.0),
        ("3Y",  3.0),
        ("5Y",  5.0),
        ("7Y",  7.0),
        ("10Y", 10.0),
        ("15Y", 15.0),
        ("20Y", 20.0),
        ("30Y", 30.0),
    };

    public CurveSnapshot GenerateBaseline(string tag = "original")
    {
        // Generate deterministic baseline
        // ...
    }

    public CurveSnapshot GenerateNext()
    {
        // Previous snapshot + random perturbation (correlated across tenors)
        // ...
    }

    public CurveSnapshot ApplyOverride(CurveSnapshot baseline, (int tenorIndex, double newRate)[] overrides)
    {
        // Replace specified tenor rates + re-interpolate
        // ...
    }

    /// <summary>
    /// Nelson-Siegel instantaneous forward rate at time t (years)
    /// </summary>
    public double ForwardRate(double t) =>
        _beta0 + _beta1 * Math.Exp(-t / _tau) + _beta2 * (t / _tau) * Math.Exp(-t / _tau);

    /// <summary>
    /// Nelson-Siegel zero-coupon rate at time t (years)
    /// </summary>
    public double ZeroRate(double t) =>
        _beta0 + (_beta1 + _beta2) * (1 - Math.Exp(-t / _tau)) / (t / _tau) - _beta2 * Math.Exp(-t / _tau);
}
```

### Step 4: Write a console test

Create `Tests/TestMockCurveGenerator.cs` — a standalone console that:
1. Generates baseline curve
2. Prints all tenors with InstantForward + ZeroRate
3. Generates 10 "updates" (random walk) and prints the last one
4. Applies an override and prints override result

### Step 5: Verify

```bash
dotnet run --project Tests/TestMockCurveGenerator.csproj
```

Expected output: 15 tenors with plausible rates (between 2-6%), incremental random walk.

---

## Task 4: InProcessMockWorker

**Objective:** A dev-mode worker that runs in-process (no named pipe), generates mock data, and pushes CurveSnapshots for the UI to consume.

**Files:**
- Create: `Workers/IWorkerClient.cs`
- Create: `Workers/InProcessMockWorker.cs`
- Modify: `WpfPlotMvp.csproj` (if needed)

### Step 1: IWorkerClient interface

```csharp
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
```

### Step 2: InProcessMockWorker

```csharp
using System;
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
    private CurveSnapshot? _overrideSnapshot;
    private bool _overrideActive;

    public async Task StartAsync(string symbol, string configXml, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        EmitStatus(WorkerStatus.ConfigLoading);
        await Task.Delay(200);  // simulate load time

        _baseline = _generator.GenerateBaseline("original");
        EmitStatus(WorkerStatus.Subscribed);

        _ = Task.Run(() => StreamLoop(_cts.Token));
    }

    private async Task StreamLoop(CancellationToken ct)
    {
        EmitStatus(WorkerStatus.Streaming);

        while (!ct.IsCancellationRequested)
        {
            var snapshot = _generator.GenerateNext();
            snapshot.Tag = _overrideActive ? "overridden" : "original";
            SnapshotReceived?.Invoke(this, snapshot);

            // Price tick
            PriceTickReceived?.Invoke(this, snapshot.Series[0].Values[^1]);

            await Task.Delay(200, ct);
        }

        EmitStatus(WorkerStatus.Stopped);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        await Task.Delay(50);
    }

    public Task ApplyOverrideAsync(ApplyOverrideRequest request)
    {
        _overrideActive = true;
        // Parse override from request and re-generate curve
        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync()
    {
        _overrideActive = false;
        _overrideSnapshot = null;
        return Task.CompletedTask;
    }

    private void EmitStatus(WorkerStatus status, string detail = "")
    {
        StatusChanged?.Invoke(this, new StatusMessage { Status = status, Detail = detail });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
```

---

## Verification

Complete Phase 1 verification:

1. `dotnet build` — entire project compiles
2. Console test outputs plausible curve data
3. All curve logic follows Nelson-Siegel math, no WPF dependency leaks into Models/Protocols/Services

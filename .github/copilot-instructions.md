# WPF Quant Desktop — Copilot Custom Instructions

> VS Code reads `.github/copilot-instructions.md` automatically.
> VS 2026 Agent Mode also supports this.
> Use **Agent Mode** (not Chat) for best results.

## Project Context

.NET 8.0 WPF desktop app for FX curve pricing & analysis.
MVVM via CommunityToolkit.Mvvm 8.4.2, charting via ScottPlot.WPF 5.1.58.

## Architecture

```
MainWindow (TabControl)
  ├── 🏠 Home Tab (hardcoded XAML, always first, not closable)
  │     └── MainPage → MainPageViewModel
  │           └── OpenChildPage event → MainWindow creates dynamic tab
  └── FX Curve #N (dynamic, closable)
        └── PricingPage → PricingPageViewModel
              └── IWorkerClient (InProcessMockWorker for dev)
```

## Code Generation Rules

### MVVM & CommunityToolkit

- ViewModel classes MUST be `partial` for source generators to work.
- `[ObservableProperty]` on `_camelCase` field → public `CamelCase` property.
- `[RelayCommand]` on async method → `XxxCommand` binding property.
- Do NOT manually implement `INotifyPropertyChanged` — use source generators.

```csharp
// ✅ CORRECT pattern:
public partial class FooViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private bool _isStreaming;

    [RelayCommand]
    private async Task StartStream() { ... IsStreaming = true; ... }
}
```

- Constructor MUST receive `IWorkerClient` for testability.
- Parameterless constructor calls `this(new InProcessMockWorker())` for dev.
- Use `Application.Current.Dispatcher.Invoke()` to marshal worker events to UI thread.

### Tab Management

- Home tab is hardcoded in `MainWindow.xaml`. Do NOT generate it in code-behind.
- Dynamic tabs created in `MainWindow.xaml.cs` `OnOpenChildPage()`.
- Use `MainTabControl.Items.Insert(MainTabControl.Items.Count, tabItem)` — NOT `.Add()`.
- CloseTab MUST check `tab.Parent is TabControl tc && tc.Items.Contains(tab)` before removing.
- Each tab has its own DataContext. Do NOT set global DataContext on MainWindow.

### XAML Bindings

- Every UserControl MUST set `DataContext` in constructor (code-behind).
- Override TextBox bindings: `UpdateSourceTrigger=PropertyChanged` is REQUIRED.
- ScottPlot namespace: `xmlns:wpf="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"`, control is `WpfPlot`.
- Start/Stop buttons use `DataTrigger` on `IsStreaming` for enable/disable toggle.
- Design-time: use `d:DataContext="{d:DesignInstance vm:ViewModelType}"` not runtime.

### Chart Wiring (CRITICAL)

- `PricingPageViewModel` has `UpdateChartAction` delegate — do NOT put ScottPlot types in ViewModel.
- `PricingPage.xaml.cs` constructor MUST set `viewModel.UpdateChartAction = CurveChart.UpdateChart`.
- Without this, the chart stays blank.

### Override Panel

- Three tabs: Params (structured), XML (view-only), XML* (editable).
- Tenor list driven by `ObservableCollection<TenorOverrideItem>`.
- Override payload format: `"1M=4.50;10Y=2.80"` — label=value pairs separated by `;`.
- Removed tenors tracked in `_removedTenorLabels` HashSet; restored on Clear.

## Tech Stack

| Component | Package | Version |
|-----------|---------|---------|
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.4.2 |
| Charts | ScottPlot.WPF | 5.1.58 |
| JSON | Newtonsoft.Json | 13.0.3 |
| Target | .NET 8.0-windows | — |

## DO NOT

- Put business logic in code-behind (`.xaml.cs`).
- Add WPF dependencies to Protocols/ classes.
- Refactor the TabControl tab-create logic.
- Use `System.Drawing.Color` — ScottPlot 5 uses `ScottPlot.Color`.
- Remove the `CloseTab` safety check (`tc.Items.Contains(tab)`).
- Create ViewModels that are not `partial`.
- Use `model="l2"` in ruptures — it's for mean-shift, not slope detection.

## Common Errors Quick Fix

| Symptom | Fix |
|---------|-----|
| `XxxCommand not found` | ViewModel not `partial` or NuGet missing |
| Tab doesn't appear | `MainTabControl` missing `x:Name` |
| Apply button does nothing | TextBox missing `UpdateSourceTrigger=PropertyChanged` |
| Chart blank | `UpdateChartAction` not assigned in constructor |
| "WpfPlot not found" | Wrong ScottPlot namespace in XAML |
| Double-close crash | Missing `tc.Items.Contains(tab)` guard |

## References

- `docs/implementation-guide.md` — full walkthrough (1286 lines)
- `AGENTS.md` — Copilot Agent Mode instructions
- `.vscode/wpf.code-snippets` — type `wpf-vm`, `wpf-page-xaml`, `wpf-page-cs` for boilerplate
- `https://github.com/github/awesome-copilot/blob/main/instructions/dotnet-wpf.instructions.md` — official WPF guidance

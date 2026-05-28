# WPF Quant Desktop — Agent Instructions for GitHub Copilot

## Project
.NET 8.0 WPF desktop app for FX curve pricing & analysis.
MVVM via CommunityToolkit.Mvvm, charting via ScottPlot 5.

## Architecture (CRITICAL)

```
MainWindow (TabControl)
  ├── Home Tab (hardcoded in XAML, always first, not closable)
  │     └── MainPage (UserControl) → MainPageViewModel
  │           └── OpenChildPage event → MainWindow creates new tab
  └── PricingPage #1 (dynamic, closable)
        └── PricingPage (UserControl) → PricingPageViewModel
              └── IWorkerClient (data source)
```

## Rules (DO NOT VIOLATE)

### Tab Management
- Home tab is written IN XAML (`MainWindow.xaml`). Do NOT add it in code-behind.
- Dynamic tabs are created in `MainWindow.xaml.cs` `OnOpenChildPage()`. Do NOT create tabs in ViewModels.
- Dynamic tabs use `MainTabControl.Items.Insert(MainTabControl.Items.Count, tabItem)` — NOT `.Add()`.
- CloseTab MUST check `tab.Parent is TabControl tc && tc.Items.Contains(tab)` before removal.

### ViewModel Rules
- ViewModel classes MUST be `partial` for CommunityToolkit source generators to work.
- `[RelayCommand]` on async methods generates `XxxCommand` automatically.
- `[ObservableProperty]` on a field generates a public property. Field naming: `_camelCase` → property `CamelCase`.
- PricingPageViewModel constructor MUST set `UpdateChartAction = CurveChart.UpdateChart` in code-behind.

### XAML Rules
- Every UserControl MUST have its `DataContext` set explicitly in code-behind constructor.
- Override TextBox bindings MUST use `UpdateSourceTrigger=PropertyChanged` or Apply button won't see the value.
- When using `d:DataContext` for design-time, use `d:DataContext="{d:DesignInstance ...}"` NOT runtime binding.
- ScottPlot namespace: `xmlns:wpf="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"`, control name is `WpfPlot`.

### Worker Pattern
- Each PricingPageViewModel owns its IWorkerClient. Call Dispose on tab close.
- Worker events MUST be marshalled to UI thread via `Application.Current.Dispatcher.Invoke()`.
- For dev, use InProcessMockWorker. No real backend needed.
- To connect real backend: implement IWorkerClient with NamedPipeWorkerClient.

## Common Mistakes (CHECK THESE FIRST)

| # | Symptom | Likely Cause |
|---|---------|-------------|
| 1 | "OpenPricingPageCommand not found" | ViewModel not `partial`, or CommunityToolkit.Mvvm NuGet missing |
| 2 | Tab doesn't appear when clicking Open | `MainTabControl` missing `x:Name` in XAML |
| 3 | Clicking Apply does nothing | Override TextBox missing `UpdateSourceTrigger=PropertyChanged` |
| 4 | Chart stays blank after Start | `UpdateChartAction` not assigned in PricingPage constructor |
| 5 | Double-click closes tab twice | `CloseTab` missing `tc.Items.Contains(tab)` guard |
| 6 | "WpfPlot not found" | Missing/wrong ScottPlot namespace in XAML |
| 7 | Start button never enables | `IsStreaming` not wired to button's DataTrigger correctly |
| 8 | New tab inherits wrong DataContext | MainWindow must NOT set a global DataContext |

## Key Files

| File | Purpose |
|------|---------|
| `MainWindow.xaml(.cs)` | TabControl + dynamic tab create/close |
| `ViewModels/MainPageViewModel.cs` | Home page actions, fires OpenChildPage event |
| `ViewModels/PricingPageViewModel.cs` | Chart worker lifecycle, override handling |
| `ViewModels/OverridePanelViewModel.cs` | Tenor table, XML editor, add/remove |
| `Views/Controls/InteractiveCurveChart.xaml.cs` | ScottPlot 5 dual Y-axis rendering |
| `Protocols/CurveSnapshot.cs` | Data contract — DO NOT add WPF dependencies |
| `Workers/IWorkerClient.cs` | Worker abstraction — implement for real backend |
| `Workers/InProcessMockWorker.cs` | Mock worker for dev |

## DO NOT
- Do NOT add new packages without asking.
- Do NOT refactor the TabControl logic.
- Do NOT put business logic in code-behind (`.xaml.cs`).
- Do NOT change Protocols/ classes to depend on WPF or ViewModels.
- Do NOT remove the `CloseTab` safety check.
- Do NOT create ViewModels that are not `partial`.

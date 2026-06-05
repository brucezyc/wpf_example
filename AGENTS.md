# WPF Quant Desktop — Agent Instructions (GPT-5 mini)

## Architecture in 3 lines

```
MainWindow (TabControl)
  ├── 🏠 Home Tab — hardcoded XAML, always first
  └── PricingPage #N — dynamic, OnOpenChildPage() creates tab
        └── PricingPageViewModel → IWorkerClient → CurveChart
```

## 5 rules (break these → code breaks)

1. **`partial class`** — Every ViewModel MUST be `partial` for CommunityToolkit source generators.
2. **Tab safety guard** — CloseTab: check `tab.Parent is TabControl tc && tc.Items.Contains(tab)` before removing.
3. **Chart must be wired** — code-behind constructor: `viewModel.UpdateChartAction = CurveChart.UpdateChart`.
4. **TextBox = PropertyChanged** — Override inputs need `UpdateSourceTrigger=PropertyChanged` or Apply is dead.
5. **Protocols are clean** — NO WPF/CommunityToolkit deps in Protocols/. Pure data only.

## 6 DO NOTs (GPT-5 mini's most common mistakes)

| # | DO NOT | Instead |
|---|--------|---------|
| 1 | Create ViewModel w/o `partial` | Hit `wpf-vm` snippet |
| 2 | Use `.Items.Add()` for dynamic tabs | `.Insert(Items.Count, tabItem)` — home tab must stay first |
| 3 | Chart logic in ViewModel | Expose `UpdateChartAction` delegate, wire in code-behind |
| 4 | Global DataContext on MainWindow | Each tab = own DataContext, set in code-behind ctor |
| 5 | `System.Drawing.Color` | ScottPlot 5 → `ScottPlot.Color` |
| 6 | Manual `INotifyPropertyChanged` | `[ObservableProperty]` + `partial class`, auto-generated |

## Pre-flight check (answer before writing)

1. Which file(s) will you touch? List each path.
2. Data flow: XAML binding → ViewModel property → Worker event → chart update — each link verified?
3. Does it compile? When unsure, check with `dotnet build`.

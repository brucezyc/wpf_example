# Quick Ref — WPF Quant Desktop

## Main instructions → `AGENTS.md` (root of project)
## Snippets → type `wpf-` in VS Code

## Tech Stack
- .NET 8.0-windows, CommunityToolkit.Mvvm 8.4.2
- ScottPlot.WPF 5.1.58, Newtonsoft.Json 13.0.3

## DO NOT
- ViewModels without `partial`
- Protocols/ with WPF dependencies
- `System.Drawing.Color` → use `ScottPlot.Color`
- Manual `INotifyPropertyChanged`
- Global DataContext on MainWindow

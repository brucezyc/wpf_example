# WPF Quant Desktop — 前端实现完全指南

> 面向 **Copilot/GPT-5 mini** 级 AI 的逐步骤实现手册。
> 不要跳过任何步骤，不要"简化"。每一步都写了就一定能编译运行。

---

## 目录

1. [项目结构](#1-项目结构)
2. [NuGet 包](#2-nuget-包)
3. [Protocols 数据层](#3-protocols-数据层)
4. [IWorkerClient 接口 + InProcessMockWorker](#4-iworkerclient-接口--inprocessmockworker)
5. [MainWindow: TabControl + 动态标签页](#5-mainwindow-tabcontrol--动态标签页)
6. [MainPage: 主页 + Action Cards](#6-mainpage-主页--action-cards)
7. [PricingPage: 曲线图 + Override Panel](#7-pricingpage-曲线图--override-panel)
8. [OverridePanel: 参数/XML 覆盖面板](#8-overridepanel-参数xml-覆盖面板)
9. [InteractiveCurveChart: 双 Y 轴 ScottPlot 5](#9-interactive-curvechart-双-y-轴-scottplot-5)
10. [Wiring 全链路](#10-wiring-全链路)
11. [构建与运行](#11-构建与运行)

---

## 1. 项目结构

```
WpfPlotMvp/
├── App.xaml / App.xaml.cs          # 应用入口, StartupUri="MainWindow.xaml"
├── AssemblyInfo.cs
├── WpfPlotMvp.csproj               # 项目文件 (.NET 8.0 WPF)
├── MainWindow.xaml(.cs)            # 主窗口: TabControl + 动态标签管理
│
├── Protocols/                      # ★ 纯数据类,不依赖WPF — 先写这个
│   ├── CurveSnapshot.cs            # 曲线数据结构
│   ├── WorkerMessages.cs           # UI↔Worker 消息类型
│   ├── WorkerStatus.cs             # 状态枚举
│   └── MessageSerializer.cs        # JSON 序列化(可选)
│
├── Workers/                        # Worker 抽象 + mock 实现
│   ├── IWorkerClient.cs            # Worker 接口
│   └── InProcessMockWorker.cs      # 进程内 Mock（开发用）
│
├── Services/                       # Mock 数据生成
│   ├── MockCurveGenerator.cs       # Nelson-Siegel 曲线生成
│   └── MockDataStreamService.cs    # Mock 数据流（备选方案）
│
├── Models/
│   └── MarketData.cs               # 备用数据模型
│
├── ViewModels/                     # MVVM ViewModel 层
│   ├── MainViewModel.cs            # 主窗口 VM（备用）
│   ├── MainPageViewModel.cs        # 主页 VM — 触发打开子页面
│   ├── PricingPageViewModel.cs     # 定价页 VM — worker 生命周期 + override
│   └── OverridePanelViewModel.cs   # Override 面板 VM
│
└── Views/
    ├── MainPage.xaml(.cs)          # 主页 UserControl（Action Cards）
    ├── PricingPage.xaml(.cs)       # 定价页 UserControl（Chart + OverridePanel）
    └── Controls/
        ├── InteractiveCurveChart.xaml(.cs)  # ScottPlot 5 图表控件
        └── OverridePanel.xaml(.cs)          # Override 面板控件
```

**关键约束：** Protocols/ 里的类 **不能引用任何 WPF 或 ViewModel 类型**。它们是纯数据合约。

---

## 2. NuGet 包

```xml
<!-- WpfPlotMvp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="ScottPlot.WPF" Version="5.1.58" />
  </ItemGroup>
</Project>
```

`dotnet restore` 后验证三个包都已安装。

---

## 3. Protocols 数据层

### 3.1 CurveSnapshot.cs

```csharp
// Protocols/CurveSnapshot.cs
namespace WpfPlotMvp.Protocols;

public class CurveSnapshot
{
    public string Tag { get; set; } = "original";  // "original" | "overridden"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<string> TenorLabels { get; set; } = new();
    public List<double> TenorYearFractions { get; set; } = new();
    public List<CurveSeries> Series { get; set; } = new();
}

public class CurveSeries
{
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "%";
    public List<double> Values { get; set; } = new();
}
```

### 3.2 WorkerMessages.cs

```csharp
// Protocols/WorkerMessages.cs
namespace WpfPlotMvp.Protocols;

// ─── UI → Worker ───
public class StartStreamRequest
{
    public string Symbol { get; set; } = "";
    public string ConfigXml { get; set; } = "";
}

public class StopStreamRequest { }

public class ApplyOverrideRequest
{
    public string OverrideType { get; set; } = "";  // "XmlSnippet" | "ParamOverride"
    public string Payload { get; set; } = "";
}

public class ClearOverrideRequest { }

// ─── Worker → UI ───
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

### 3.3 WorkerStatus.cs

```csharp
// Protocols/WorkerStatus.cs
namespace WpfPlotMvp.Protocols;

public enum WorkerStatus
{
    Idle, ConfigLoading, ConfigLoaded,
    Subscribing, Subscribed, Streaming,
    OverrideActive, Error, Stopped
}
```

---

## 4. IWorkerClient 接口 + InProcessMockWorker

### 4.1 IWorkerClient.cs

```csharp
// Workers/IWorkerClient.cs
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

### 4.2 InProcessMockWorker（简化版）

核心逻辑：

```csharp
// Workers/InProcessMockWorker.cs
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
    private (int tenorIndex, double newRate)[]? _overrideRate;
    private int[]? _excludeIndices;

    public async Task StartAsync(string symbol, string configXml, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        EmitStatus(WorkerStatus.ConfigLoading);

        // 模拟加载延迟
        await Task.Delay(200);
        EmitStatus(WorkerStatus.ConfigLoaded);
        await Task.Delay(100);
        EmitStatus(WorkerStatus.Subscribing);
        await Task.Delay(100);
        EmitStatus(WorkerStatus.Subscribed);
        await Task.Delay(50);

        // 启动流循环（每200ms发一条数据）
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
        // 解析"1M=4.50;2Y=2.80"格式的 payload
        var parts = request.Payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var overrideList = new List<(int idx, double rate)>();
        foreach (var part in parts)
        {
            var kv = part.Split('=');
            int? idx = FindTenorIndex(kv[0].Trim());
            if (idx == null) continue;
            if (kv.Length == 2 && double.TryParse(kv[1], out double rate))
                overrideList.Add(((int)idx, rate));
        }
        _overrideRate = overrideList.ToArray();
        _excludeIndices = Array.Empty<int>();
        EmitStatus(WorkerStatus.OverrideActive, $"Override: {_overrideRate.Length} rates");
        return Task.CompletedTask;
    }

    public Task ClearOverrideAsync()
    {
        _overrideRate = null;
        _excludeIndices = null;
        EmitStatus(WorkerStatus.Streaming, "Override cleared");
        return Task.CompletedTask;
    }

    public void Dispose() { _cts?.Cancel(); _cts?.Dispose(); }

    // ─── 内部 ───
    private async Task StreamLoop(CancellationToken ct)
    {
        EmitStatus(WorkerStatus.Streaming);
        while (!ct.IsCancellationRequested)
        {
            var snapshot = _generator.GenerateNext("original");
            SnapshotReceived?.Invoke(this, snapshot);

            if (_overrideRate != null && _overrideRate.Length > 0)
            {
                var overridden = _generator.ApplyOverride(snapshot, _overrideRate, _excludeIndices);
                SnapshotReceived?.Invoke(this, overridden);
            }

            double lastRate = snapshot.Series[0].Values[^1];
            PriceTickReceived?.Invoke(this, Math.Round(lastRate, 4));

            try { await Task.Delay(200, ct); }
            catch (OperationCanceledException) { break; }
        }
        EmitStatus(WorkerStatus.Stopped);
    }

    private int? FindTenorIndex(string label)
    {
        for (int i = 0; i < MockCurveGenerator.DefaultTenors.Length; i++)
            if (MockCurveGenerator.DefaultTenors[i].Label
                .Equals(label, StringComparison.OrdinalIgnoreCase))
                return i;
        return null;
    }

    private void EmitStatus(WorkerStatus status, string detail = "")
        => StatusChanged?.Invoke(this, new StatusMessage { Status = status, Detail = detail });
}
```

---

## 5. MainWindow: TabControl + 动态标签页

**这是 Copilot/GPT-5 mini 最容易搞错的地方。严格按照以下步骤。**

### 5.1 XAML

```xml
<!-- MainWindow.xaml -->
<Window x:Class="WpfPlotMvp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:WpfPlotMvp.Views"
        xmlns:vm="clr-namespace:WpfPlotMvp.ViewModels"
        Title="Quant Desktop" Height="700" Width="1200">
    <Grid>
        <TabControl x:Name="MainTabControl" Margin="4">
            <TabControl.Resources>
                <Style TargetType="TabItem">
                    <Setter Property="FontSize" Value="13"/>
                    <Setter Property="FontWeight" Value="SemiBold"/>
                    <Setter Property="Padding" Value="10,4"/>
                </Style>
            </TabControl.Resources>

            <!-- Home tab is hardcoded in XAML, always first -->
            <TabItem Header="🏠 Home" IsSelected="True">
                <views:MainPage x:Name="MainPageControl"/>
            </TabItem>
        </TabControl>
    </Grid>
</Window>
```

**关键点：**
- `MainTabControl` 必须有 `x:Name`，因为后台代码通过这个名字操作它
- Home tab 写在 XAML 里固定位第一个
- 子页面（PricingPage）不写在 XAML 里，在 `OnOpenChildPage` 里动态创建

### 5.2 Code-behind

```csharp
// MainWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfPlotMvp.ViewModels;
using WpfPlotMvp.Views;

namespace WpfPlotMvp;

public partial class MainWindow : Window
{
    private int _pricingTabCounter;  // 每个新标签的唯一编号

    public MainWindow()
    {
        InitializeComponent();

        // ★ 订阅 MainPage 的 OpenChildPage 事件
        if (MainPageControl.DataContext is MainPageViewModel mainVm)
        {
            mainVm.OpenChildPage += OnOpenChildPage;
        }
    }

    private void OnOpenChildPage(string header, Type viewModelType)
    {
        // 第1步: 递增计数器,使每个标签名唯一
        _pricingTabCounter++;
        string tabHeader = $"{header} #{_pricingTabCounter}";

        // 第2步: 根据 ViewModel 类型创建对应的页面
        UserControl? page = viewModelType switch
        {
            // ★ 如果增加新页面类型,在这里加分支
            Type t when t == typeof(PricingPageViewModel) => new PricingPage(),
            _ => null
        };
        if (page == null) return;

        // 第3步: 构建可关闭标签
        var tabItem = BuildClosableTabItem(tabHeader, page);

        // 第4步: 插入到 Home tab 前面（Home 保持最后一个/最左边）
        // Items.Count = 当前总数(含Home), 插入到 Items.Count-1 即 Home 之前
        MainTabControl.Items.Insert(MainTabControl.Items.Count, tabItem);
        MainTabControl.SelectedItem = tabItem;
    }

    private static TabItem BuildClosableTabItem(string header, UserControl content)
    {
        var tab = new TabItem();

        // Header: 水平 StackPanel = 标签文字 + ×关闭按钮
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock
        {
            Text = header,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Background = null,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Foreground = Brushes.Gray,
            Width = 20, Height = 20,
            Padding = new Thickness(0),
            ToolTip = "Close"
        };

        // 关闭按钮点击事件
        closeBtn.Click += (_, _) => CloseTab(tab, content);
        // 中键点击标签文字也关闭
        label.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle
                && e.ButtonState == MouseButtonState.Pressed)
                CloseTab(tab, content);
        };

        headerPanel.Children.Add(label);
        headerPanel.Children.Add(closeBtn);
        tab.Header = headerPanel;
        tab.Content = content;

        return tab;
    }

    private static void CloseTab(TabItem tab, UserControl content)
    {
        // ★ 安全检查: 防止双击×和单击中键同时触发两次
        if (tab.Parent is TabControl tc && tc.Items.Contains(tab))
        {
            tc.Items.Remove(tab);
            // 释放 ViewModel 资源(Stop worker)
            if (content.DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
```

**⚠️ GPT-5 mini 常见错误：**
1. 忘记给 `MainTabControl` 加 `x:Name` → 后台代码无法引用
2. 把动态标签写在 XAML 里 → 应该用代码动态创建
3. `Insert(Items.Count, tabItem)` 写成 `Add(tabItem)` → 会加在最后（Home 后面），不对
4. `CloseTab` 里不检查 `tab.Parent` → 双击时会抛异常
5. 新标签的 DataContext 意外继承了 MainWindow 的 DataContext → 不要在 MainWindow 级别设 DataContext

---

## 6. MainPage: 主页 + Action Cards

### 6.1 ViewModel

```csharp
// ViewModels/MainPageViewModel.cs
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfPlotMvp.ViewModels;

// 主页上的操作卡片
public class MainPageActionCard
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAvailable { get; set; } = true;
}

public partial class MainPageViewModel : ObservableObject
{
    // ★ 事件: 通知 MainWindow 打开新子页面
    public event Action<string, Type>? OpenChildPage;

    public string AppVersion { get; } = "v0.1";

    // 卡片列表 — 可用/不可用的卡片都列在这
    public System.Collections.ObjectModel.ObservableCollection<MainPageActionCard> Actions { get; } = new()
    {
        new MainPageActionCard
        {
            Title = "FX Curve Pricing",
            Description = "Load a model, subscribe to Solace topics, and view real-time stripped curves..."
        },
        new MainPageActionCard
        {
            Title = "Config Viewer",
            Description = "View and inspect model configuration XML files.",
            IsAvailable = false  // ★ 置灰
        },
        new MainPageActionCard
        {
            Title = "Topic Inspector",
            Description = "Look up Solace topics for a given instrument.",
            IsAvailable = false
        }
    };

    [RelayCommand]
    private void OpenPricingPage()
    {
        // ★ 触发事件, MainWindow 收到后创建新标签
        OpenChildPage?.Invoke("FX Curve", typeof(PricingPageViewModel));
    }
}
```

**⚠️ 针对 GPT-5 mini：**
- `OpenChildPage` 事件是被 MainWindow 订阅的。**不要在 MainPageViewModel 内部创建标签页。**
- `[RelayCommand]` 由 CommunityToolkit.Mvvm 的 Source Generator 生成 `OpenPricingPageCommand` 属性。如果没生成，检查项目是不是 `net8.0-windows` 并且安装了 CommunityToolkit.Mvvm 包。
- 新加页面时加新的 `[RelayCommand]` 方法和对应的 XAML 按钮。

### 6.2 XAML

```xml
<!-- Views/MainPage.xaml -->
<UserControl x:Class="WpfPlotMvp.Views.MainPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:WpfPlotMvp.ViewModels"
             d:DataContext="{d:DesignInstance vm:MainPageViewModel, IsDesignTimeCreatable=True}">
    <Grid Background="#0a0a1a">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <Border Grid.Row="0" Background="#1a1a2e" Padding="20" Margin="30,30,30,0" CornerRadius="8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column="0">
                    <TextBlock Text="Quant Desktop" FontSize="28" FontWeight="Bold" Foreground="White"/>
                    <TextBlock Text="FX Curve Analysis &amp; Pricing Platform" FontSize="14" Foreground="#667" Margin="0,4,0,0"/>
                </StackPanel>
                <TextBlock Grid.Column="1" Text="{Binding AppVersion}" Foreground="#445" FontSize="12"
                           VerticalAlignment="Bottom" Margin="0,0,0,6"/>
            </Grid>
        </Border>

        <!-- Action Cards -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Center"
                    VerticalAlignment="Center" Margin="30,20,30,0">
            <!-- ★ Card 1: FX Curve Pricing (只有这个是active的) -->
            <Border Background="#1a1a2e" CornerRadius="8" Padding="0" Margin="8"
                    Width="240" Height="180" BorderBrush="#2a2a4e" BorderThickness="1">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <StackPanel Grid.Row="0" Margin="16,20,16,0">
                        <TextBlock Text="FX Curve Pricing" FontSize="18" FontWeight="Bold"
                                   Foreground="White" TextWrapping="Wrap"/>
                        <TextBlock Text="Load a model, subscribe to Solace topics..."
                                   FontSize="12" Foreground="#667" Margin="0,8,0,0" TextWrapping="Wrap"/>
                    </StackPanel>
                    <!-- ★ 按钮绑定 OpenPricingPageCommand -->
                    <Button Grid.Row="1" Content="Open" Command="{Binding OpenPricingPageCommand}"
                            Background="#007bff" Foreground="White" Padding="10,6"
                            Margin="16,0,16,14" BorderThickness="0" FontSize="13"
                            HorizontalAlignment="Stretch" Cursor="Hand"/>
                </Grid>
            </Border>
            <!-- ★ 不可用的卡片用 Opacity=0.5 + IsEnabled=False -->
            <Border Background="#1a1a2e" CornerRadius="8" Padding="0" Margin="8"
                    Width="240" Height="180" BorderBrush="#2a2a4e" BorderThickness="1"
                    Opacity="0.5">
                <Button Content="Coming Soon" IsEnabled="False" .../>
            </Border>
        </StackPanel>

        <!-- Footer -->
        <TextBlock Grid.Row="2" Text="Select an action above to open a new workspace tab."
                   Foreground="#334" FontSize="12" HorizontalAlignment="Center" Margin="0,0,0,20"/>
    </Grid>
</UserControl>
```

### 6.3 Code-behind

```csharp
// Views/MainPage.xaml.cs
using System.Windows.Controls;
namespace WpfPlotMvp.Views;
public partial class MainPage : UserControl
{
    public MainPage() => InitializeComponent();
}
```

---

## 7. PricingPage: 曲线图 + Override Panel

### 7.1 ViewModel（核心）

```csharp
// ViewModels/PricingPageViewModel.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfPlotMvp.Protocols;
using WpfPlotMvp.Workers;

namespace WpfPlotMvp.ViewModels;

public partial class PricingPageViewModel : ObservableObject, IDisposable
{
    private readonly IWorkerClient _worker;
    private CancellationTokenSource? _cts;

    // ─── 界面绑定属性 ───
    [ObservableProperty] private string _symbol = "EURUSD";
    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _currentPrice = "—";
    [ObservableProperty] private bool _isStreaming;

    public List<string> AvailableSeries { get; } = new()
    {
        "Instantaneous Forward Rate", "Zero Rate",
        "Discount Factor", "Par Rate"
    };

    [ObservableProperty] private string _leftYSeries = "Instantaneous Forward Rate";
    [ObservableProperty] private string _rightYSeries = "None";
    public List<string> RightAxisOptions { get; } = new()
    {
        "None", "Instantaneous Forward Rate", "Zero Rate",
        "Discount Factor", "Par Rate"
    };

    // Override 面板的 ViewModel
    public OverridePanelViewModel OverridePanel { get; } = new();

    // 最新曲线数据
    private CurveSnapshot? _latestOriginal;
    private CurveSnapshot? _latestOverridden;

    // ★ 构造函数: 默认使用 InProcessMockWorker（可替换为 NamedPipeWorkerClient）
    public PricingPageViewModel()
        : this(new InProcessMockWorker()) { }

    public PricingPageViewModel(IWorkerClient worker)
    {
        _worker = worker;
        _worker.SnapshotReceived += OnSnapshotReceived;
        _worker.StatusChanged += OnStatusChanged;
        _worker.PriceTickReceived += OnPriceTick;

        // 连接 OverridePanel 的事件
        OverridePanel.OverrideSubmitted += OnOverrideSubmitted;
        OverridePanel.OverrideCleared += OnOverrideCleared;
    }

    // ─── Start / Stop ───
    [RelayCommand]
    private async Task StartStream()
    {
        if (IsStreaming) return;
        _cts = new CancellationTokenSource();
        IsStreaming = true;
        try
        {
            await _worker.StartAsync(Symbol, "<mock-config-xml/>", _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsStreaming = false;
        }
    }

    [RelayCommand]
    private async Task StopStream()
    {
        if (!IsStreaming) return;
        IsStreaming = false;
        _cts?.Cancel();
        await _worker.StopAsync();
        StatusText = "Stopped";
    }

    // ─── Override ───
    private async void OnOverrideSubmitted(ApplyOverrideRequest request)
    {
        await _worker.ApplyOverrideAsync(request);
        StatusText = $"Override active ({request.OverrideType})";
    }
    private async void OnOverrideCleared()
    {
        await _worker.ClearOverrideAsync();
        _latestOverridden = null;
        RequestChartUpdate();
        StatusText = "Override cleared";
    }

    // ─── Worker 事件处理 ───
    private void OnSnapshotReceived(object? sender, CurveSnapshot snapshot)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (snapshot.Tag == "original")
            {
                _latestOriginal = snapshot;
                OverridePanel.UpdateRates(snapshot);  // 更新 Override 面板的当前值
            }
            else if (snapshot.Tag == "overridden")
                _latestOverridden = snapshot;

            RequestChartUpdate();
        });
    }

    private void OnStatusChanged(object? sender, StatusMessage msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = !string.IsNullOrEmpty(msg.Detail)
                ? $"{msg.Status}: {msg.Detail}"
                : msg.Status.ToString();
        });
    }

    private void OnPriceTick(object? sender, double price)
    {
        Application.Current.Dispatcher.Invoke(() =>
            CurrentPrice = price.ToString("F4"));
    }

    // ─── Chart 更新（节流） ───
    // ★ View 设这个 Action, 用于更新图表
    public Action<List<CurveSnapshot>, string, string>? UpdateChartAction { get; set; }

    private bool _chartUpdatePending;
    private void RequestChartUpdate()
    {
        if (_chartUpdatePending) return;
        _chartUpdatePending = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _chartUpdatePending = false;
            PushChartUpdate();
        }));
    }

    private void PushChartUpdate()
    {
        if (UpdateChartAction == null) return;
        var snapshots = new List<CurveSnapshot>();
        if (_latestOriginal != null) snapshots.Add(_latestOriginal);
        if (_latestOverridden != null) snapshots.Add(_latestOverridden);
        if (snapshots.Count == 0) return;

        UpdateChartAction(snapshots, LeftYSeries,
            RightYSeries == "None" ? "" : RightYSeries);
    }

    // 下拉框切换时更新图表
    partial void OnLeftYSeriesChanged(string value) => PushChartUpdate();
    partial void OnRightYSeriesChanged(string value) => PushChartUpdate();

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _worker.Dispose();
    }
}
```

### 7.2 XAML

```xml
<!-- Views/PricingPage.xaml -->
<!-- ★ 顶层 Grid: Row0=Header, Row1=Chart+Override, Row2=StatusBar -->
<UserControl x:Class="WpfPlotMvp.Views.PricingPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ctrls="clr-namespace:WpfPlotMvp.Views.Controls"
             d:DataContext="{d:DesignInstance vm:PricingPageViewModel}">
    <Grid Margin="8">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- ═══ Row 0: Header Bar ═══ -->
        <Border Grid.Row="0" Background="#1a1a2e" CornerRadius="6" Padding="10" Margin="0,0,0,8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>   <!-- Symbol -->
                    <ColumnDefinition Width="Auto"/>   <!-- Price -->
                    <ColumnDefinition Width="*"/>      <!-- Spacer -->
                    <ColumnDefinition Width="Auto"/>   <!-- Status -->
                    <ColumnDefinition Width="Auto"/>   <!-- Start/Stop -->
                </Grid.ColumnDefinitions>
                <!-- Symbol -->
                <TextBlock Grid.Column="0" Text="{Binding Symbol}" FontSize="20" FontWeight="Bold"
                           Foreground="White" VerticalAlignment="Center" Margin="0,0,16,0"/>
                <!-- Price -->
                <Border Grid.Column="1" Background="#16213e" CornerRadius="4" Padding="10,4" Margin="0,0,16,0">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="Price: " Foreground="#8899aa" FontSize="14" VerticalAlignment="Center"/>
                        <TextBlock Text="{Binding CurrentPrice}" Foreground="#00d4aa"
                                   FontSize="16" FontWeight="Bold" VerticalAlignment="Center"/>
                    </Grid>
                </Border>
                <!-- Status -->
                <Border Grid.Column="3" Background="#16213e" CornerRadius="4" Padding="10,4" Margin="0,0,10,0">
                    <TextBlock Text="{Binding StatusText}" Foreground="#8899aa" FontSize="12" VerticalAlignment="Center"/>
                </Border>
                <!-- Start / Stop Buttons -->
                <StackPanel Grid.Column="4" Orientation="Horizontal">
                    <!-- ★ Start button: IsStreaming=true 时禁用 -->
                    <Button Content="Start Stream" Command="{Binding StartStreamCommand}"
                            Background="#007bff" Foreground="White" Padding="12,6"
                            Margin="0,0,6,0" FontSize="13" FontWeight="SemiBold"
                            BorderThickness="0" Cursor="Hand">
                        <Button.Style>
                            <Style TargetType="Button">
                                <Setter Property="IsEnabled" Value="True"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsStreaming}" Value="True">
                                        <Setter Property="IsEnabled" Value="False"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                    </Button>
                    <!-- ★ Stop button: IsStreaming=false 时禁用 -->
                    <Button Content="Stop" Command="{Binding StopStreamCommand}"
                            Background="#dc3545" Foreground="White" Padding="12,6"
                            FontSize="13" FontWeight="SemiBold" BorderThickness="0" Cursor="Hand">
                        <Button.Style>
                            <Style TargetType="Button">
                                <Setter Property="IsEnabled" Value="False"/>
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding IsStreaming}" Value="True">
                                        <Setter Property="IsEnabled" Value="True"/>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Button.Style>
                    </Button>
                </StackPanel>
            </Grid>
        </Border>

        <!-- ═══ Row 1: Chart + Override Panel ═══ -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>       <!-- Chart -->
                <ColumnDefinition Width="Auto"/>    <!-- Override -->
            </Grid.ColumnDefinitions>
            <Border Grid.Column="0" Background="#0f0f23" CornerRadius="6" Padding="6" Margin="0,0,8,0">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>  <!-- Axis dropdowns -->
                        <RowDefinition Height="*"/>     <!-- Chart -->
                    </Grid.RowDefinitions>
                    <!-- Axis Selection -->
                    <Grid Grid.Row="0" Margin="0,0,0,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/><ColumnDefinition Width="160"/>
                            <ColumnDefinition Width="20"/>
                            <ColumnDefinition Width="Auto"/><ColumnDefinition Width="160"/>
                        </Grid.ColumnDefinitions>
                        <!-- ★ Dropdowns bound to LeftYSeries / RightYSeries -->
                        <TextBlock Text="◀ Left Y:" Foreground="#8899aa" FontSize="12"
                                   VerticalAlignment="Center" Margin="0,0,6,0"/>
                        <ComboBox Grid.Column="1" ItemsSource="{Binding AvailableSeries}"
                                  SelectedItem="{Binding LeftYSeries, UpdateSourceTrigger=PropertyChanged}"
                                  Background="#1a1a2e" Foreground="White" FontSize="12" BorderBrush="#333" MinHeight="24"/>
                        <TextBlock Grid.Column="3" Text="Right Y: ▶" Foreground="#8899aa" FontSize="12"
                                   VerticalAlignment="Center" Margin="0,0,6,0"/>
                        <ComboBox Grid.Column="4" ItemsSource="{Binding RightAxisOptions}"
                                  SelectedItem="{Binding RightYSeries, UpdateSourceTrigger=PropertyChanged}"
                                  Background="#1a1a2e" Foreground="White" FontSize="12" BorderBrush="#333" MinHeight="24"/>
                    </Grid>
                    <!-- ★★ InteractiveCurveChart -- name is CURVECHART, used in .xaml.cs -->
                    <ctrls:InteractiveCurveChart x:Name="CurveChart" Grid.Row="1"/>
                </Grid>
            </Border>
            <!-- Override Panel (right) -->
            <Border Grid.Column="1" Background="#0f0f23" CornerRadius="6" Padding="10"
                    MinWidth="280" MaxWidth="360">
                <ctrls:OverridePanel DataContext="{Binding OverridePanel}"/>
            </Border>
        </Grid>

        <!-- ═══ Row 2: Status Bar ═══ -->
        <Border Grid.Row="2" Background="#1a1a2e" CornerRadius="4" Padding="8,4" Margin="0,8,0,0">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding StatusText, StringFormat='Status: {0}'}" Foreground="#667" FontSize="11"/>
                <TextBlock Grid.Column="1" Text="{Binding Symbol, StringFormat='Symbol: {0}'}" Foreground="#667" FontSize="11"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

### 7.3 Code-behind（关键！）

```csharp
// Views/PricingPage.xaml.cs
using System.Windows.Controls;
using WpfPlotMvp.ViewModels;

namespace WpfPlotMvp.Views;

public partial class PricingPage : UserControl
{
    public PricingPage()
        : this(new PricingPageViewModel())  // 默认使用 mock
    { }

    public PricingPage(PricingPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // ★★ 关键绑定: 把 Chart 的 UpdateChart 方法 Register 到 ViewModel
        viewModel.UpdateChartAction = CurveChart.UpdateChart;
    }
}
```

**⚠️ 这是 GPT-5 mini 最容易遗漏的地方：** `PricingPage.xaml.cs` 里有两步必须做：
1. `DataContext = viewModel;` — 否则 XAML 里的 `{Binding ...}` 都不工作
2. `viewModel.UpdateChartAction = CurveChart.UpdateChart;` — 否则 VM 推图表数据时 Chart 不会刷新

---

## 8. OverridePanel: 参数/XML 覆盖面板

### 8.1 ViewModel

包含三个 tab:
- Tab 0 (Params): 十五个 tenors 的表格，每个 tenor 一行，显示当前值 + 可编辑覆盖值
- Tab 1 (XML): 只读 XML
- Tab 2 (XML\*): 可编辑 XML

```csharp
// ViewModels/OverridePanelViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.ViewModels;

public class TenorOverrideItem : ObservableObject
{
    public string Label { get; set; } = "";
    public bool IsCustom { get; set; }
    private double _currentRate;
    public double CurrentRate { get => _currentRate; set => SetProperty(ref _currentRate, value); }
    private double? _overrideRate;
    public double? OverrideRate
    {
        get => _overrideRate;
        set { if (SetProperty(ref _overrideRate, value)) OnPropertyChanged(nameof(IsOverridden)); }
    }
    public bool IsOverridden => OverrideRate.HasValue;
    public void ClearOverride() => OverrideRate = null;
}

public partial class OverridePanelViewModel : ObservableObject
{
    private readonly string[] _defaultTenorLabels =
        { "1W", "1M", "2M", "3M", "6M", "9M", "1Y", "2Y", "3Y", "5Y", "7Y", "10Y", "15Y", "20Y", "30Y" };
    private readonly HashSet<string> _removedTenorLabels = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ApplyOverrideRequest>? OverrideSubmitted;
    public event Action? OverrideCleared;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _originalXml = "";
    [ObservableProperty] private string _overrideXml = "";
    [ObservableProperty] private bool _isOverrideActive;
    [ObservableProperty] private string _newTenorLabel = "";
    [ObservableProperty] private string _newTenorRate = "";

    public ObservableCollection<TenorOverrideItem> Tenors { get; } = new();

    public OverridePanelViewModel()
    {
        foreach (var label in _defaultTenorLabels)
            Tenors.Add(new TenorOverrideItem { Label = label });
    }

    // PriceingPageViewModel 每收到一个 snapshot 就调用此方法
    public void UpdateRates(CurveSnapshot snapshot)
    {
        var fwdSeries = snapshot.Series.FirstOrDefault(s => s.Name == "Instantaneous Forward Rate");
        if (fwdSeries == null) return;

        var rateMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _defaultTenorLabels.Length && i < fwdSeries.Values.Count; i++)
            rateMap[_defaultTenorLabels[i]] = fwdSeries.Values[i];

        foreach (var tenor in Tenors)
        {
            if (rateMap.TryGetValue(tenor.Label, out double rate))
                tenor.CurrentRate = rate;
        }
    }

    // ─── Add / Remove Tenor ───
    [RelayCommand] private void AddTenor() { /* ... 省略（见源文件）... */ }
    [RelayCommand] private void RemoveTenor(TenorOverrideItem? tenor) { /* ... */ }

    // ─── Apply / Clear ───
    [RelayCommand] private void ApplyOverride() { /* ... */ }
    [RelayCommand] private void ReloadOriginalXml() { /* ... */ }
    [RelayCommand] private void ClearOverride() { /* ... */ }
}
```

### 8.2 XAML (OverridePanel.xaml)

结构：三层嵌套 `Grid` → `TabControl` (3 tabs) → 底部按钮。

**Params Tab (索引0):** `ScrollViewer` 内含 `ItemsControl` 绑定 `Tenors`，每一行 = `TenorOverrideItem`。关键绑定：

```xml
<!-- ★ 每行显示: ×删除按钮 | Tenor名称 | 当前值 | 覆盖值输入框 -->
<DataTemplate DataType="{x:Type vm:TenorOverrideItem}">
    <Border BorderBrush="#1a1a3e" BorderThickness="0,0,0,1" Padding="2,2">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="46"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <!-- × Remove -->
            <Button Grid.Column="0" Content="×"
                    Command="{Binding DataContext.RemoveTenorCommand,
                        RelativeSource={RelativeSource AncestorType=TabControl}}"
                    CommandParameter="{Binding}" .../>
            <!-- Tenor label -->
            <TextBlock Grid.Column="1" Text="{Binding Label}" .../>
            <!-- Current rate (read-only) -->
            <TextBlock Grid.Column="2" Text="{Binding CurrentRate, StringFormat={}{0:F4}}" .../>
            <!-- ★ Override input (PropertyChanged trigger!) -->
            <TextBox Grid.Column="3"
                     Text="{Binding OverrideRate, TargetNullValue='',
                        UpdateSourceTrigger=PropertyChanged}"
                     .../>
        </Grid>
    </Border>
</DataTemplate>
```

**⚠️ `UpdateSourceTrigger=PropertyChanged`** 必须加在 Override 输入框上，否则用户在输入框打字后点击 Apply 时 TextBox 还没更新 ViewModel。

**XML tab (索引1):** `IsReadOnly="True"` 的 TextBox
**XML\* tab (索引2):** 可编辑 TextBox + "↺ Reload Original" 按钮

底部按钮：Apply Override / Clear

---

## 9. InteractiveCurveChart: 双 Y 轴 ScottPlot 5

```csharp
// Views/Controls/InteractiveCurveChart.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using ScottPlot;
using WpfPlotMvp.Protocols;

namespace WpfPlotMvp.Views.Controls;

public partial class InteractiveCurveChart : UserControl
{
    public InteractiveCurveChart()
    {
        InitializeComponent();
        InitializePlot();
    }

    // ★ 被 PricingPageViewModel.UpdateChartAction 调用
    public void UpdateChart(
        List<CurveSnapshot> snapshots,
        string leftSeriesName,
        string rightSeriesName)
    {
        var plot = PlotControl.Plot;
        plot.Clear();

        bool hasRightSeries = !string.IsNullOrEmpty(rightSeriesName) &&
                              !rightSeriesName.Equals("None", StringComparison.OrdinalIgnoreCase);

        // 设置 Y 轴标签
        plot.Axes.Right.IsVisible = hasRightSeries;
        if (hasRightSeries) plot.Axes.Right.Label.Text = SeriesToAxisLabel(rightSeriesName);
        plot.Axes.Left.Label.Text = SeriesToAxisLabel(leftSeriesName);

        foreach (var snapshot in snapshots)
        {
            if (snapshot.TenorYearFractions.Count == 0) continue;
            double[] xs = snapshot.TenorYearFractions.ToArray();

            // Left Y
            var leftSer = snapshot.Series.FirstOrDefault(s => s.Name == leftSeriesName);
            if (leftSer?.Values.Count == xs.Length)
            {
                double[] ys = leftSer.Values.Select(v => (double)v).ToArray();
                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Axes.YAxis = plot.Axes.Left;
                bool isOrig = snapshot.Tag == "original";
                scatter.Color = isOrig
                    ? new ScottPlot.Color(30, 144, 255)   // Blue
                    : new ScottPlot.Color(255, 69, 0);     // OrangeRed
                scatter.LineWidth = 2;
                scatter.LegendText = isOrig
                    ? $"{leftSeriesName} (original)"
                    : $"{leftSeriesName} ({snapshot.Tag})";
                scatter.MarkerSize = isOrig ? 4 : 6;
                scatter.MarkerShape = isOrig
                    ? MarkerShape.FilledCircle
                    : MarkerShape.FilledDiamond;
            }

            // Right Y (same pattern, different Y axis)
            if (hasRightSeries) { /* ... 省略，跟 left 类似 ... */ }
        }

        // X 轴: Tenor 标签
        ConfigureXAxis(plot, snapshots);
        PlotControl.Refresh();
    }

    private static string SeriesToAxisLabel(string name) => name switch
    {
        "Instantaneous Forward Rate" => "Forward Rate (%)",
        "Zero Rate" => "Zero Rate (%)",
        "Discount Factor" => "Discount Factor",
        "Par Rate" => "Par Rate (%)",
        _ => name
    };

    private static void ConfigureXAxis(Plot plot, List<CurveSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return;
        var first = snapshots[0];
        double[] positions = first.TenorYearFractions.ToArray();
        string[] labels = first.TenorLabels.ToArray();
        plot.Axes.Bottom.SetTicks(positions, labels);
        plot.Axes.Bottom.TickLabelStyle.Rotation = -45;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
    }
}
```

**XAML：**
```xml
<wpf:WpfPlot x:Name="PlotControl" />
<!-- 在 WpfPlotMvp.Views.Controls 命名空间下 -->
<!-- xmlns:wpf="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF" -->
```

注意：ScottPlot.WPF 的命名空间是 `ScottPlot.WPF`，控件名是 `WpfPlot`。

---

## 10. Wiring 全链路

从用户点击"Open"到图表显示数据的完整数据流：

```
User clicks "Open" on MainPage
    → MainPageViewModel.OpenPricingPageCommand
    → fires OpenChildPage("FX Curve", typeof(PricingPageViewModel))
    → MainWindow.OnOpenChildPage
    → new PricingPage()  →  new PricingPageViewModel()
    → BuildClosableTabItem("FX Curve #1", pricingPage)
    → MainTabControl.Items.Insert(...)
    → Tab shown!

User clicks "Start Stream"
    → PricingPageViewModel.StartStreamCommand
    → InProcessMockWorker.StartAsync
    → StreamLoop: 每200ms:
        1. MockCurveGenerator.GenerateNext("original")  → CurveSnapshot
        2. fires SnapshotReceived event
        3. PricingPageViewModel.OnSnapshotReceived
        4. 写入 _latestOriginal / _latestOverridden
        5. OverridePanel.UpdateRates(snapshot)  → 更新各位tenor的当前值
        6. RequestChartUpdate()
        7. [Dispatcher.BeginInvoke] PushChartUpdate()
        8. UpdateChartAction(snapshots, left, right)
        9. InteractiveCurveChart.UpdateChart  → 清空+重绘
```

---

## 11. 构建与运行

```bash
# Windows 上 (必须有 .NET 8 SDK)
cd WpfPlotMvp
dotnet restore
dotnet build
dotnet run
```

**常见编译错误与修复：**

| 错误 | 原因 | 修复 |
|------|------|------|
| `The type 'WpfPlot' is not found` | ScottPlot.WPF 命名空间未正确引用 | 确认 XAML 里有 `xmlns:wpf="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"` |
| `Cannot find source generator 'CommunityToolkit.Mvvm.SourceGenerators'` | 忘装 CommunityToolkit.Mvvm NuGet 包 | `dotnet add package CommunityToolkit.Mvvm --version 8.4.2` |
| `OpenPricingPageCommand` not found | Source Generator 没生成 | 确认 ViewModel 类声明为 `partial`，方法有 `[RelayCommand]` |
| `DataContext` binding 无效 | 父级设置了全局 DataContext 或者没有 Set | MainWindow 不要设 DataContext；PricingPage 构造函数里必须 `DataContext = viewModel` |
| 关闭 tab 时抛出异常 | 双击关闭触发两次 | 加上 `tab.Parent is TabControl tc && tc.Items.Contains(tab)` 检查 |
| 输入覆盖值后点 Apply 没反应 | 缺少 `UpdateSourceTrigger=PropertyChanged` | 在 Override 输入框的 Binding 上加这个 |
| 图标不更新 | 没注册 `UpdateChartAction` | `PricingPage.xaml.cs` 构造函数里加 `viewModel.UpdateChartAction = CurveChart.UpdateChart` |
| Start 按钮点了没反应 | `StartStream` 是 async Task 但按钮没等 | `[RelayCommand]` 在异步方法上自动生成 `StartStreamCommand`，确认绑定的是这个 |

---

## 总结: 关键设计决策

1. **Protocol First** — 先写 Protocols/ 数据类，再写 Worker 接口，最后写 UI
2. **Tab 管理在 MainWindow，不在 ViewModel** — MainWindow.xaml.cs 负责创建/关闭标签页
3. **Worker 隔离** — 每个 ViewModel 有独立的 IWorkerClient 实例，关闭 tab 时 Dispose
4. **Chart 通过 Action 回调** — ViewModel 不依赖 ScottPlot 类型，通过 `UpdateChartAction` 委托更新图表
5. **Mock 优先** — 开发时用 InProcessMockWorker，不需要真实后端也能运行
6. **替换后端只需要换 IWorkerClient 实现** — 把 `InProcessMockWorker` 替换为 `NamedPipeWorkerClient` 即可接入真实 Solace/后台

using System;
using System.Windows;
using System.Windows.Threading;
using WpfPlotMvp.Models;
using WpfPlotMvp.ViewModels;

namespace WpfPlotMvp;

public partial class MainWindow : Window
{
    private DispatcherTimer _renderTimer;
    private ScottPlot.Plottables.DataLogger _dataLogger;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize ScottPlot DataLogger
        _dataLogger = WpfPlot1.Plot.Add.DataLogger();
        WpfPlot1.Plot.Axes.DateTimeTicksBottom();
        WpfPlot1.Plot.Title("Live Stream Data MVP");
        WpfPlot1.Plot.YLabel("Value");
        WpfPlot1.Plot.XLabel("Time");

        // Set up a timer to pull data from ViewModel and update plot at 20 FPS
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _renderTimer.Tick += RenderTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _renderTimer.Start();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _renderTimer.Stop();
        if (DataContext is MainViewModel vm)
        {
            vm.StopStream();
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            bool hasNewData = false;
            
            // Drain the queue
            while (vm.NewDataQueue.TryDequeue(out MarketData? data))
            {
                if (data != null)
                {
                    _dataLogger.Add(data.Timestamp.ToOADate(), data.Value);
                    hasNewData = true;
                }
            }

            if (hasNewData)
            {
                // Request a redraw
                WpfPlot1.Refresh();
            }
        }
    }
}
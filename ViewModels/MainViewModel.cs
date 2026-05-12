using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Concurrent;
using System.Windows;
using WpfPlotMvp.Models;
using WpfPlotMvp.Services;

namespace WpfPlotMvp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MockDataStreamService _streamService;
    
    // Thread-safe queue for the UI timer to pull from
    public ConcurrentQueue<MarketData> NewDataQueue { get; } = new();

    [ObservableProperty]
    private double _currentPrice;

    public MainViewModel()
    {
        _streamService = new MockDataStreamService();
        _streamService.DataReceived += OnDataReceived;
    }

    private void OnDataReceived(object? sender, MarketData data)
    {
        // Queue data for the View to plot
        NewDataQueue.Enqueue(data);

        // Update current price label on UI thread
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CurrentPrice = Math.Round(data.Value, 2);
        });
    }

    [RelayCommand]
    public void StartStream()
    {
        _streamService.Start();
    }

    [RelayCommand]
    public void StopStream()
    {
        _streamService.Stop();
    }
}

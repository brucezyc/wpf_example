using System;
using System.Threading.Tasks;
using WpfPlotMvp.Models;

namespace WpfPlotMvp.Services;

public class MockDataStreamService
{
    public event EventHandler<MarketData>? DataReceived;
    private bool _isRunning;
    private readonly Random _random = new();
    private double _currentValue = 100.0;

    public void Start()
    {
        _isRunning = true;
        Task.Run(async () =>
        {
            while (_isRunning)
            {
                // Simulate stream delay (100ms)
                await Task.Delay(100);

                // Generate mock random walk data
                _currentValue += (_random.NextDouble() - 0.5) * 2;
                var data = new MarketData
                {
                    Timestamp = DateTime.Now,
                    Value = _currentValue
                };

                DataReceived?.Invoke(this, data);
            }
        });
    }

    public void Stop()
    {
        _isRunning = false;
    }
}

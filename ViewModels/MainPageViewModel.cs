using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace WpfPlotMvp.ViewModels;

public class MainPageActionCard
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAvailable { get; set; } = true;
}

/// <summary>
/// ViewModel for the main landing page.
/// Fires OpenChildPage when the user clicks a button to launch a new child tab.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    public event Action<string, Type>? OpenChildPage;

    public string AppVersion { get; } = "v0.1";

    public ObservableCollection<MainPageActionCard> Actions { get; } = new()
    {
        new MainPageActionCard
        {
            Title = "FX Curve Pricing",
            Description = "Load a model, subscribe to Solace topics, and view real-time stripped curves with interactive chart and override panel."
        },
        new MainPageActionCard
        {
            Title = "Config Viewer",
            Description = "View and inspect model configuration XML files.",
            IsAvailable = false
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
        OpenChildPage?.Invoke("FX Curve", typeof(PricingPageViewModel));
    }
}

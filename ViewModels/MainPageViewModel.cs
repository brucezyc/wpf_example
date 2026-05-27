using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfPlotMvp.ViewModels;

/// <summary>
/// ViewModel for the main landing page.
/// Fires OpenChildPage when the user clicks a button to launch a new child tab.
/// </summary>
public partial class MainPageViewModel : ObservableObject
{
    // Fired when the user wants to open a child page in a new tab.
    // string = tab header text, Type = ViewModel type to instantiate
    public event Action<string, Type>? OpenChildPage;

    public string AppVersion { get; } = "v0.1";

    [RelayCommand]
    private void OpenPricingPage()
    {
        OpenChildPage?.Invoke("FX Curve", typeof(PricingPageViewModel));
    }

    [RelayCommand]
    private void OpenConfigViewer()
    {
        // Placeholder — will be implemented later
    }

    [RelayCommand]
    private void OpenTopicInspector()
    {
        // Placeholder — will be implemented later
    }
}

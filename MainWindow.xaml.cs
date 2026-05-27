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
    private int _pricingTabCounter;

    public MainWindow()
    {
        InitializeComponent();

        if (MainPageControl.DataContext is MainPageViewModel mainVm)
        {
            mainVm.OpenChildPage += OnOpenChildPage;
        }
    }

    private void OnOpenChildPage(string header, Type viewModelType)
    {
        _pricingTabCounter++;
        string tabHeader = $"{header} #{_pricingTabCounter}";

        UserControl? page = viewModelType switch
        {
            Type t when t == typeof(PricingPageViewModel) => new PricingPage(),
            _ => null
        };

        if (page == null) return;

        var tabItem = BuildClosableTabItem(tabHeader, page);

        MainTabControl.Items.Insert(MainTabControl.Items.Count, tabItem);
        MainTabControl.SelectedItem = tabItem;
    }

    private static TabItem BuildClosableTabItem(string header, UserControl content)
    {
        var tab = new TabItem();

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
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            ToolTip = "Close"
        };

        closeBtn.Click += (_, _) => CloseTab(tab, content);
        label.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
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
        // Validate the tab is still in the TabControl before removing
        // (prevents double-close from label click + close button)
        if (tab.Parent is TabControl tc && tc.Items.Contains(tab))
        {
            tc.Items.Remove(tab);
            if (content.DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}

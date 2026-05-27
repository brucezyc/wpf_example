using System.Windows;
using System.Windows.Input;
using WpfPlotMvp.ViewModels;

namespace WpfPlotMvp.Views;

/// <summary>
/// Model for main page action cards — used as ItemsControl items.
/// </summary>
public class MainPageActionCard : DependencyObject
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MainPageActionCard));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(MainPageActionCard));

    public static readonly DependencyProperty OpenCommandProperty =
        DependencyProperty.Register(nameof(OpenCommand), typeof(ICommand), typeof(MainPageActionCard));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }
}

public partial class MainPage : UserControl
{
    public MainPage()
    {
        InitializeComponent();
        DataContext = new MainPageViewModel();
    }

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

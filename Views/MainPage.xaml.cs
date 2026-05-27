using System.Windows.Controls;
using WpfPlotMvp.ViewModels;

namespace WpfPlotMvp.Views;

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

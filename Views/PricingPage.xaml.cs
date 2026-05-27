using System.Windows.Controls;
using WpfPlotMvp.ViewModels;

namespace WpfPlotMvp.Views;

public partial class PricingPage : UserControl
{
    public PricingPage(PricingPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Wire chart update action
        viewModel.UpdateChartAction = CurveChart.UpdateChart;
    }

    public PricingPage()
        : this(new PricingPageViewModel())
    {
    }
}

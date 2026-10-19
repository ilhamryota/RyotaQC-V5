using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class DriverPackPage : Page
{
    public DriverPackPage(DriverPackPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

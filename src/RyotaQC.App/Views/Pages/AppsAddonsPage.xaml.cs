using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class AppsAddonsPage : Page
{
    public AppsAddonsPage(AppsAddonsPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

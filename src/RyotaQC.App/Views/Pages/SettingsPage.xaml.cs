using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

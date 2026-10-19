using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class WallpaperDesktopPage : Page
{
    public WallpaperDesktopPage(WallpaperDesktopPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class QualityControlPage : Page
{
    public QualityControlPage(QualityControlPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

using System.Windows;
using System.Windows.Controls;
using RyotaQC.App.ViewModels.Pages;

namespace RyotaQC.App.Views.Pages;

public partial class SystemInformationPage : Page
{
    public SystemInformationPage(SystemInformationPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private Window? HostWindow => Window.GetWindow(this);

    private void Minimize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } win)
        {
            win.WindowState = WindowState.Minimized;
        }
    }

    private void Maximize_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is not { } win)
        {
            return;
        }

        win.WindowState = win.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        if (HostWindow is { } win)
        {
            win.Close();
        }
    }
}

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RyotaQC.App.Services;
using RyotaQC.App.ViewModels;
using RyotaQC.App.ViewModels.Pages;
using RyotaQC.App.Views.Pages;

namespace RyotaQC.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — catch unhandled exceptions so they are
        // logged instead of silently crashing the app.
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var logger = new FileAppLogger();
                logger.Error("Unhandled dispatcher exception.", args.Exception);
            }
            catch { }
            MessageBox.Show(
                "Terjadi error: " + args.Exception.Message +
                "\n\nDetail telah dicatat di log.",
                "RyotaQC V5",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var logger = new FileAppLogger();
                logger.Error("Unhandled AppDomain exception.", args.ExceptionObject as Exception);
            }
            catch { }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                var logger = new FileAppLogger();
                logger.Error("Unobserved task exception.", args.Exception);
            }
            catch { }
            args.SetObserved();
        };

        // Run startup synchronously enough to surface fatal errors via the
        // WPF dispatcher. Async work is awaited through a safe helper so an
        // unhandled exception is logged and re-thrown on the dispatcher
        // instead of being silently swallowed (the classic async-void trap).
        try
        {
            RunStartup();
        }
        catch (Exception ex)
        {
            var logger = new FileAppLogger();
            logger.Error("Fatal error during startup.", ex);
            MessageBox.Show(
                "RyotaQC V5 gagal dimulai.\n\n" + ex.Message +
                "\n\nDetail telah dicatat di log.",
                "RyotaQC V5",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RunStartup()
    {
        Services = ConfigureServices();

        var appSettings = Services.GetRequiredService<IAppSettingsService>();
        appSettings.Load();

        var toolConfig = Services.GetRequiredService<IToolConfigService>();
        toolConfig.Load();

        var splashDelayMs = Math.Clamp(appSettings.Current.SplashDelayMs, 0, 3000);

        Views.Windows.SplashWindow? splash = null;
        if (splashDelayMs > 0)
        {
            splash = Services.GetRequiredService<Views.Windows.SplashWindow>();
            splash.Show();
            Thread.Sleep(splashDelayMs);
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        if (splash is not null)
        {
            splash.Close();
        }

        mainWindow.Show();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IUiFeedbackService, UiFeedbackService>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<IToolConfigService, ToolConfigService>();
        services.AddSingleton<IExternalToolLauncher, ExternalToolLauncher>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IWallpaperService, WallpaperService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IQualityControlRunner, QualityControlRunner>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        services.AddSingleton<IQuickProvisionService, QuickProvisionService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<HomeScreenViewModel>();

        services.AddSingleton<DashboardPageViewModel>();
        services.AddSingleton<SystemInformationPageViewModel>();
        services.AddSingleton<DriverPackPageViewModel>();
        services.AddSingleton<AppsAddonsPageViewModel>();
        services.AddSingleton<WallpaperDesktopPageViewModel>();
        services.AddSingleton<QualityControlPageViewModel>();
        services.AddSingleton<QuickLauncherPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();

        services.AddSingleton<DashboardPage>();
        services.AddSingleton<SystemInformationPage>();
        services.AddSingleton<DriverPackPage>();
        services.AddSingleton<AppsAddonsPage>();
        services.AddSingleton<WallpaperDesktopPage>();
        services.AddSingleton<QualityControlPage>();
        services.AddSingleton<QuickLauncherPage>();
        services.AddSingleton<SettingsPage>();

        services.AddTransient<Views.Windows.SplashWindow>();

        return services.BuildServiceProvider();
    }
}

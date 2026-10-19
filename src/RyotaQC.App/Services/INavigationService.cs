using RyotaQC.App.Enums;

namespace RyotaQC.App.Services;

public interface INavigationService
{
    event Action<AppPage>? NavigationRequested;

    AppPage CurrentPage { get; }

    bool CanGoBack { get; }

    void Navigate(AppPage page);

    bool NavigateBack();
}

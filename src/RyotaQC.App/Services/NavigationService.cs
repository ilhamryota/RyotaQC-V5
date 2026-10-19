using RyotaQC.App.Enums;

namespace RyotaQC.App.Services;

public sealed class NavigationService : INavigationService
{
    private readonly Stack<AppPage> _history = new();
    private bool _hasNavigated;

    public event Action<AppPage>? NavigationRequested;

    public AppPage CurrentPage { get; private set; } = AppPage.Dashboard;

    public bool CanGoBack => _history.Count > 0;

    public void Navigate(AppPage page)
    {
        if (page == CurrentPage && _hasNavigated)
        {
            return;
        }

        if (_hasNavigated)
        {
            _history.Push(CurrentPage);
        }

        CurrentPage = page;
        _hasNavigated = true;
        NavigationRequested?.Invoke(page);
    }

    public bool NavigateBack()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        CurrentPage = _history.Pop();
        NavigationRequested?.Invoke(CurrentPage);
        return true;
    }
}

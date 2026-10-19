namespace RyotaQC.App.Services;

public sealed class UiFeedbackService : IUiFeedbackService
{
    public event Action<bool, string>? LoadingStateChanged;

    public event Action<string, bool>? ToastRequested;

    public void ShowLoading(string message)
    {
        LoadingStateChanged?.Invoke(true, message);
    }

    public void HideLoading()
    {
        LoadingStateChanged?.Invoke(false, string.Empty);
    }

    public void ShowToast(string message, bool isSuccess)
    {
        ToastRequested?.Invoke(message, isSuccess);
    }
}

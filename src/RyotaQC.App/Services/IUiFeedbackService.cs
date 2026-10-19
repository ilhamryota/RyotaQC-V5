namespace RyotaQC.App.Services;

public interface IUiFeedbackService
{
    event Action<bool, string>? LoadingStateChanged;

    event Action<string, bool>? ToastRequested;

    void ShowLoading(string message);

    void HideLoading();

    void ShowToast(string message, bool isSuccess);
}

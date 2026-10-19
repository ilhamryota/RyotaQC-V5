namespace RyotaQC.App.Services;

public interface IAppLogger
{
    void Info(string message);

    void Error(string message, Exception? ex = null);
}

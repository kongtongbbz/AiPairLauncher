namespace AiPairLauncher.App.Services;

public interface INotificationService : IDisposable
{
    void Notify(string title, string message);
}

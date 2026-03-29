namespace AiPairLauncher.App.Services;

public interface INotificationService : IDisposable
{
    event EventHandler<string?>? Activated;

    void Notify(string title, string message, string? activationContext = null);
}

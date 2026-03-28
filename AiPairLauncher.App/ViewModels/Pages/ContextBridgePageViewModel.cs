namespace AiPairLauncher.App.ViewModels.Pages;

public sealed class ContextBridgePageViewModel
{
    public ContextBridgePageViewModel(MainWindowViewModel core, SharedSessionState sharedState)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        SharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
    }

    public MainWindowViewModel Core { get; }

    public SharedSessionState SharedState { get; }
}

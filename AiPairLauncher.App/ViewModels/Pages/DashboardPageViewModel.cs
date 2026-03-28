namespace AiPairLauncher.App.ViewModels.Pages;

public sealed class DashboardPageViewModel
{
    public DashboardPageViewModel(MainWindowViewModel core, SharedSessionState sharedState)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        SharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
    }

    public MainWindowViewModel Core { get; }

    public SharedSessionState SharedState { get; }
}

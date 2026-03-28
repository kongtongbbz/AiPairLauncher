namespace AiPairLauncher.App.ViewModels.Pages;

public sealed class SettingsPageViewModel
{
    public SettingsPageViewModel(MainWindowViewModel core, SharedSessionState sharedState)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        SharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
    }

    public MainWindowViewModel Core { get; }

    public SharedSessionState SharedState { get; }

    public string GuiTaskChecklistPath => @"docs\gui-redesign-task.md";
}

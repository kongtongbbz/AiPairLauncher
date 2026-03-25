using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IWezTermService
{
    Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default);

    Task<string> ReadPaneTextAsync(LauncherSession session, int paneId, int lastLines, CancellationToken cancellationToken = default);

    Task SendTextToPaneAsync(LauncherSession session, int paneId, string text, bool submit, CancellationToken cancellationToken = default);

    Task SendAutomationPromptAsync(
        LauncherSession session,
        AgentRole role,
        string prompt,
        bool submit,
        CancellationToken cancellationToken = default);

    Task<ContextTransferResult> SendContextAsync(LauncherSession session, SendContextRequest request, CancellationToken cancellationToken = default);
}

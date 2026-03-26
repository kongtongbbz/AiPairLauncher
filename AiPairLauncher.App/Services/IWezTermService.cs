using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IWezTermService
{
    Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedWorkspaceInfo>> ListManagedWorkspacesAsync(CancellationToken cancellationToken = default);

    Task<SessionReconnectResult> TryReconnectSessionAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default);

    Task FocusPaneAsync(LauncherSession session, int paneId, CancellationToken cancellationToken = default);

    Task<WorktreeLaunchContext> CreateWorktreeLaunchContextAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    Task<WorktreeMaintenanceResult> CleanupWorktreeAsync(string workingDirectory, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> CleanupOrphanedWorktreesAsync(string workingDirectory, CancellationToken cancellationToken = default);

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

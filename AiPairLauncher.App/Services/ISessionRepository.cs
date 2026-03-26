using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface ISessionRepository
{
    string StateDatabasePath { get; }

    string LegacySessionFilePath { get; }

    Task<IReadOnlyList<ManagedSessionRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task<ManagedSessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task UpsertAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default);

    Task RenameSessionAsync(string sessionId, string displayName, CancellationToken cancellationToken = default);

    Task SetPinnedAsync(string sessionId, bool isPinned, CancellationToken cancellationToken = default);

    Task MoveToGroupAsync(string sessionId, string groupName, CancellationToken cancellationToken = default);

    Task ArchiveAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RestoreAsync(string sessionId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<string?> GetSelectedSessionIdAsync(CancellationToken cancellationToken = default);

    Task SetSelectedSessionIdAsync(string? sessionId, CancellationToken cancellationToken = default);

    Task<string?> GetAppStateAsync(string key, CancellationToken cancellationToken = default);

    Task SetAppStateAsync(string key, string? value, CancellationToken cancellationToken = default);

    Task UpdateRuntimeBindingAsync(SessionRuntimeBinding runtimeBinding, CancellationToken cancellationToken = default);

    Task DeleteRuntimeBindingAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveStatusSnapshotAsync(SessionStatusSnapshot statusSnapshot, CancellationToken cancellationToken = default);

    Task<SessionStatusSnapshot?> GetStatusSnapshotAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveLaunchProfileAsync(LaunchProfile launchProfile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LaunchProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<LaunchProfile?> DuplicateSessionConfigAsync(string sessionId, string profileName, CancellationToken cancellationToken = default);

    Task SaveAutomationEventAsync(AutomationEventRecord eventRecord, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomationEventRecord>> ListAutomationEventsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default);

    Task SaveAutomationSnapshotAsync(PersistedAutomationSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<PersistedAutomationSnapshot?> GetAutomationSnapshotAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ClearAutomationSnapshotAsync(string sessionId, CancellationToken cancellationToken = default);

    Task ClearAllAsync(CancellationToken cancellationToken = default);
}

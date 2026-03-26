namespace AiPairLauncher.App.Models;

public sealed class PersistedAutomationSnapshot
{
    public string SessionId { get; init; } = string.Empty;

    public AutomationSettings Settings { get; init; } = new();

    public AutomationRunState State { get; init; } = new();

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool CanResume => State.IsActive || State.HasPendingApproval;
}

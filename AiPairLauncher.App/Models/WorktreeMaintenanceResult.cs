namespace AiPairLauncher.App.Models;

public sealed class WorktreeMaintenanceResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string GitRoot { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string? BranchName { get; init; }

    public bool WorktreeRemoved { get; init; }

    public bool BranchDeleted { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];
}

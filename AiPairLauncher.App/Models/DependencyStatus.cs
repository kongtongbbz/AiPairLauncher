namespace AiPairLauncher.App.Models;

public sealed class DependencyStatus
{
    public required string Name { get; init; }

    public required bool IsAvailable { get; init; }

    public string? ResolvedPath { get; init; }

    public string? Version { get; init; }

    public string? Message { get; init; }
}


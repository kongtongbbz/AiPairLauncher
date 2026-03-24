namespace AiPairLauncher.App.Infrastructure;

public sealed class ProcessCommand
{
    public required string FileName { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public string? StandardInput { get; init; }

    public IReadOnlyDictionary<string, string?>? EnvironmentVariables { get; init; }

    public bool WaitForExit { get; init; } = true;
}

namespace AiPairLauncher.App.Models;

public sealed class SessionReconnectResult
{
    public bool Success { get; init; }

    public string? FailureReason { get; init; }

    public LauncherSession? Session { get; init; }

    public SessionRuntimeBinding? RuntimeBinding { get; init; }
}

namespace AiPairLauncher.App.Models;

public sealed class AutomationSettings
{
    public bool IsEnabled { get; init; } = true;

    public string InitialTaskPrompt { get; init; } = string.Empty;

    public int PollIntervalMilliseconds { get; init; } = 1500;

    public int CaptureLines { get; init; } = 220;

    public bool SubmitOnSend { get; init; } = true;

    public int NoProgressTimeoutSeconds { get; init; } = 600;
}

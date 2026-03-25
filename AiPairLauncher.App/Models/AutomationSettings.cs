namespace AiPairLauncher.App.Models;

public sealed class AutomationSettings
{
    public bool IsEnabled { get; init; } = true;

    public string InitialTaskPrompt { get; init; } = string.Empty;

    public AutomationAdvancePolicy AdvancePolicy { get; init; } = AutomationAdvancePolicy.FullAutoLoop;

    public int PollIntervalMilliseconds { get; init; } = 1500;

    public int CaptureLines { get; init; } = 220;

    public bool SubmitOnSend { get; init; } = true;

    public int NoProgressTimeoutSeconds { get; init; } = 600;

    public int MaxAutoStages { get; init; } = 8;

    public int MaxRetryPerStage { get; init; } = 2;
}

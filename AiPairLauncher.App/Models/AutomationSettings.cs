namespace AiPairLauncher.App.Models;

public sealed class AutomationSettings
{
    public bool IsEnabled { get; init; } = true;

    public string InitialTaskPrompt { get; init; } = string.Empty;

    public string AutomationTemplateKey { get; init; } = "feature";

    public AgentRole Phase1Executor { get; init; } = AgentRole.Claude;

    public AgentRole Phase2Executor { get; init; } = AgentRole.Claude;

    public AgentRole Phase3Executor { get; init; } = AgentRole.Codex;

    public AgentRole Phase4Executor { get; init; } = AgentRole.Claude;

    public AutomationAdvancePolicy AdvancePolicy { get; init; } = AutomationAdvancePolicy.FullAutoLoop;

    public int PollIntervalMilliseconds { get; init; } = 1500;

    public int CaptureLines { get; init; } = 220;

    public bool SubmitOnSend { get; init; } = true;

    public int NoProgressTimeoutSeconds { get; init; } = 600;

    public int MaxAutoStages { get; init; } = 8;

    public int MaxRetryPerStage { get; init; } = 2;

    public AutomationParallelismPolicy ParallelismPolicy { get; init; } = AutomationParallelismPolicy.Auto;

    public int MaxParallelSubagents { get; init; } = 4;
}

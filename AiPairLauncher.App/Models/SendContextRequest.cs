namespace AiPairLauncher.App.Models;

public sealed class SendContextRequest
{
    public int? SourcePaneId { get; init; }

    public int? TargetPaneId { get; init; }

    public string? Workspace { get; init; }

    public int LastLines { get; init; } = 120;

    public string Instruction { get; init; } = "请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。";

    public bool Submit { get; init; }

    public bool FromLeftToRight { get; init; } = true;
}


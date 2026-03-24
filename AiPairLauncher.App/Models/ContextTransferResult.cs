namespace AiPairLauncher.App.Models;

public sealed class ContextTransferResult
{
    public required int SourcePaneId { get; init; }

    public required int TargetPaneId { get; init; }

    public required int LastLines { get; init; }

    public required bool Submitted { get; init; }

    public required int CapturedLength { get; init; }
}


namespace AiPairLauncher.App.Models;

public sealed class ModeOption
{
    public required string Value { get; init; }

    public required string Label { get; init; }

    public override string ToString()
    {
        return Label;
    }
}

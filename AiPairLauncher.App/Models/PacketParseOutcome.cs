namespace AiPairLauncher.App.Models;

public sealed class PacketParseOutcome
{
    public PacketParseStatus Status { get; init; } = PacketParseStatus.NoPacket;

    public AgentPacket? Packet { get; init; }

    public string? ErrorMessage { get; init; }
}

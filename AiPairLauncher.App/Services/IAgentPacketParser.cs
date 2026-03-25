using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IAgentPacketParser
{
    PacketParseOutcome ParseLatest(string paneText);
}

namespace AiPairLauncher.App.Services;

public sealed class AutoCollaborationCoordinatorFactory : IAutoCollaborationCoordinatorFactory
{
    private readonly IWezTermService _wezTermService;
    private readonly IAgentPacketParser _packetParser;

    public AutoCollaborationCoordinatorFactory(IWezTermService wezTermService, IAgentPacketParser packetParser)
    {
        _wezTermService = wezTermService ?? throw new ArgumentNullException(nameof(wezTermService));
        _packetParser = packetParser ?? throw new ArgumentNullException(nameof(packetParser));
    }

    public IAutoCollaborationCoordinator Create()
    {
        return new AutoCollaborationCoordinator(_wezTermService, _packetParser);
    }
}

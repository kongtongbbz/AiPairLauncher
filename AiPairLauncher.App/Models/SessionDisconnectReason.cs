namespace AiPairLauncher.App.Models;

public enum SessionDisconnectReason
{
    None,
    Timeout,
    ProcessExited,
    PermissionDenied,
    TransportError,
    Unknown,
}


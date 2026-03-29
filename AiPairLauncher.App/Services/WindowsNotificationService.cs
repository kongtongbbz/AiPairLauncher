using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AiPairLauncher.App.Services;

public sealed class WindowsNotificationService : INotificationService
{
    private readonly NotifyIcon _notifyIcon;
    private string? _lastActivationContext;

    public event EventHandler<string?>? Activated;

    public WindowsNotificationService(string iconFilePath)
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Icon = File.Exists(iconFilePath) ? new Icon(iconFilePath) : SystemIcons.Information,
            Text = "AiPairLauncher",
        };
        _notifyIcon.BalloonTipClicked += NotifyIconOnBalloonTipClicked;
    }

    public void Notify(string title, string message, string? activationContext = null)
    {
        _lastActivationContext = activationContext;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _notifyIcon.BalloonTipClicked -= NotifyIconOnBalloonTipClicked;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void NotifyIconOnBalloonTipClicked(object? sender, EventArgs e)
    {
        Activated?.Invoke(this, _lastActivationContext);
    }
}

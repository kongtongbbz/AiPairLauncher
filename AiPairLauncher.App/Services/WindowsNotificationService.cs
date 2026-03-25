using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AiPairLauncher.App.Services;

public sealed class WindowsNotificationService : INotificationService
{
    private readonly NotifyIcon _notifyIcon;

    public WindowsNotificationService(string iconFilePath)
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Icon = File.Exists(iconFilePath) ? new Icon(iconFilePath) : SystemIcons.Information,
            Text = "AiPairLauncher",
        };
    }

    public void Notify(string title, string message)
    {
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

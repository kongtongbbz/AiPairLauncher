using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<ModeOption> ClaudeModes =
    [
        new() { Value = "default", Label = "Default Mode" },
        new() { Value = "plan", Label = "Plan Mode" },
    ];

    private static readonly IReadOnlyList<ModeOption> CodexModes =
    [
        new() { Value = "standard", Label = "Standard" },
        new() { Value = "full-auto", Label = "Full Auto" },
        new() { Value = "never-ask", Label = "Never Ask" },
    ];

    private readonly StringBuilder _logBuilder = new();
    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _workspaceName = "aipair-main";
    private string _transferInstruction = "请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。";
    private string _claudePermissionMode = "default";
    private string _codexMode = "standard";
    private string _sessionWorkspace = "暂无";
    private string _sessionCreatedAt = "暂无";
    private string _sessionPaneInfo = "暂无";
    private string _sessionSocketPath = "暂无";
    private string _statusMessage = "就绪";
    private string _footerMessage = "等待操作。";
    private string _logText = "日志初始化完成。";
    private int _rightPanePercent = 60;
    private int _lastLines = 120;
    private bool _submitAfterSend = true;
    private bool _isBusy;
    private bool _hasSession;

    public MainWindowViewModel()
    {
        if (!Directory.Exists(_workingDirectory))
        {
            _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    public string WorkspaceName
    {
        get => _workspaceName;
        set => SetField(ref _workspaceName, value);
    }

    public string ClaudePermissionMode
    {
        get => _claudePermissionMode;
        set
        {
            var normalized = NormalizeClaudePermissionMode(value);
            if (!SetField(ref _claudePermissionMode, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(ClaudeModeDisplayText));
        }
    }

    public string CodexMode
    {
        get => _codexMode;
        set
        {
            var normalized = NormalizeCodexMode(value);
            if (!SetField(ref _codexMode, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(CodexModeDisplayText));
        }
    }

    public int RightPanePercent
    {
        get => _rightPanePercent;
        set => SetField(ref _rightPanePercent, Math.Clamp(value, 10, 90));
    }

    public int LastLines
    {
        get => _lastLines;
        set => SetField(ref _lastLines, Math.Max(10, value));
    }

    public string TransferInstruction
    {
        get => _transferInstruction;
        set => SetField(ref _transferInstruction, value);
    }

    public bool SubmitAfterSend
    {
        get => _submitAfterSend;
        set => SetField(ref _submitAfterSend, value);
    }

    public string SessionWorkspace
    {
        get => _sessionWorkspace;
        private set => SetField(ref _sessionWorkspace, value);
    }

    public string SessionCreatedAt
    {
        get => _sessionCreatedAt;
        private set => SetField(ref _sessionCreatedAt, value);
    }

    public string SessionPaneInfo
    {
        get => _sessionPaneInfo;
        private set => SetField(ref _sessionPaneInfo, value);
    }

    public string SessionSocketPath
    {
        get => _sessionSocketPath;
        private set => SetField(ref _sessionSocketPath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string FooterMessage
    {
        get => _footerMessage;
        set => SetField(ref _footerMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanSend));
        }
    }

    public bool HasSession
    {
        get => _hasSession;
        private set
        {
            if (!SetField(ref _hasSession, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSend));
        }
    }

    public bool CanStart => !IsBusy;

    public bool CanSend => !IsBusy && HasSession;

    public IReadOnlyList<ModeOption> ClaudeModeOptions => ClaudeModes;

    public IReadOnlyList<ModeOption> CodexModeOptions => CodexModes;

    public string ClaudeModeDisplayText => ClaudePermissionMode switch
    {
        "plan" => "Plan Mode",
        _ => "Default Mode",
    };

    public string CodexModeDisplayText => CodexMode switch
    {
        "full-auto" => "Full Auto",
        "never-ask" => "Never Ask",
        _ => "Standard",
    };

    public void ReplaceDependencies(IEnumerable<DependencyStatus> dependencies)
    {
        Dependencies.Clear();
        foreach (var dependency in dependencies)
        {
            Dependencies.Add(dependency);
        }
    }

    public void ApplySession(LauncherSession? session)
    {
        if (session is null)
        {
            SessionWorkspace = "暂无";
            SessionCreatedAt = "暂无";
            SessionPaneInfo = "暂无";
            SessionSocketPath = "暂无";
            HasSession = false;
            return;
        }

        SessionWorkspace = session.Workspace;
        SessionCreatedAt = session.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        SessionPaneInfo = $"Left={session.LeftPaneId}, Right={session.RightPaneId}, Width={session.RightPanePercent}%";
        SessionSocketPath = session.SocketPath;
        HasSession = true;
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(line);
        LogText = _logBuilder.ToString();
    }

    public void ClearLog()
    {
        _logBuilder.Clear();
        LogText = string.Empty;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string NormalizeClaudePermissionMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "plan" => "plan",
            _ => "default",
        };
    }

    private static string NormalizeCodexMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "full-auto" => "full-auto",
            "never-ask" => "never-ask",
            _ => "standard",
        };
    }
}

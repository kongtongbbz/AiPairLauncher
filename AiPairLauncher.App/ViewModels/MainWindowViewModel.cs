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
    private string _savedClaudePermissionMode = "default";
    private string _savedCodexMode = "standard";
    private string _sessionWorkspace = "暂无";
    private string _sessionCreatedAt = "暂无";
    private string _sessionPaneInfo = "暂无";
    private string _sessionSocketPath = "暂无";
    private string _statusMessage = "就绪";
    private string _footerMessage = "等待操作。";
    private string _logText = "日志初始化完成。";
    private string _automationStatusLabel = "Idle";
    private string _automationStatusDetail = "自动编排未启动。";
    private string _automationLastPacketSummary = "暂无";
    private string _automationLastError = "暂无";
    private string _automationUpdatedAt = "暂无";
    private string _automationTaskPrompt = "请围绕当前工作目录中的项目需求推进开发，先拆成可审批的阶段计划，再驱动 Codex 执行与验证，直到完成。";
    private string _approvalNote = string.Empty;
    private string _pendingApprovalStage = "暂无";
    private string _pendingApprovalTitle = "暂无";
    private string _pendingApprovalSummary = "暂无";
    private string _pendingApprovalScope = "暂无";
    private string _pendingApprovalSteps = "暂无";
    private string _pendingApprovalAcceptance = "暂无";
    private string _pendingApprovalCodexBrief = "暂无";
    private int _rightPanePercent = 60;
    private int _lastLines = 120;
    private int _automationPollIntervalMilliseconds = 1500;
    private int _automationCaptureLines = 220;
    private int _automationTimeoutSeconds = 600;
    private bool _submitAfterSend = true;
    private bool _automationSubmitOnSend = true;
    private bool _automationObserverEnabled = true;
    private bool _autoModeEnabled;
    private bool _isBusy;
    private bool _hasSession;
    private bool _hasPendingApproval;
    private bool _isAutomationActive;

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

    public bool AutoModeEnabled
    {
        get => _autoModeEnabled;
        set
        {
            if (!SetField(ref _autoModeEnabled, value))
            {
                return;
            }

            ApplyModeLock(value);
            OnPropertyChanged(nameof(AreModeSelectorsEnabled));
            OnPropertyChanged(nameof(ModeLockHint));
            OnPropertyChanged(nameof(CanStartAutomation));
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

    public int AutomationPollIntervalMilliseconds
    {
        get => _automationPollIntervalMilliseconds;
        set => SetField(ref _automationPollIntervalMilliseconds, Math.Max(200, value));
    }

    public int AutomationCaptureLines
    {
        get => _automationCaptureLines;
        set => SetField(ref _automationCaptureLines, Math.Max(20, value));
    }

    public int AutomationTimeoutSeconds
    {
        get => _automationTimeoutSeconds;
        set => SetField(ref _automationTimeoutSeconds, Math.Max(30, value));
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

    public bool AutomationSubmitOnSend
    {
        get => _automationSubmitOnSend;
        set => SetField(ref _automationSubmitOnSend, value);
    }

    public bool AutomationObserverEnabled
    {
        get => _automationObserverEnabled;
        set => SetField(ref _automationObserverEnabled, value);
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

    public string AutomationStatusLabel
    {
        get => _automationStatusLabel;
        private set => SetField(ref _automationStatusLabel, value);
    }

    public string AutomationStatusDetail
    {
        get => _automationStatusDetail;
        private set => SetField(ref _automationStatusDetail, value);
    }

    public string AutomationLastPacketSummary
    {
        get => _automationLastPacketSummary;
        private set => SetField(ref _automationLastPacketSummary, value);
    }

    public string AutomationLastError
    {
        get => _automationLastError;
        private set => SetField(ref _automationLastError, value);
    }

    public string AutomationUpdatedAt
    {
        get => _automationUpdatedAt;
        private set => SetField(ref _automationUpdatedAt, value);
    }

    public string ApprovalNote
    {
        get => _approvalNote;
        set => SetField(ref _approvalNote, value);
    }

    public string AutomationTaskPrompt
    {
        get => _automationTaskPrompt;
        set => SetField(ref _automationTaskPrompt, value);
    }

    public string PendingApprovalStage
    {
        get => _pendingApprovalStage;
        set => SetField(ref _pendingApprovalStage, value);
    }

    public string PendingApprovalTitle
    {
        get => _pendingApprovalTitle;
        set => SetField(ref _pendingApprovalTitle, value);
    }

    public string PendingApprovalSummary
    {
        get => _pendingApprovalSummary;
        set => SetField(ref _pendingApprovalSummary, value);
    }

    public string PendingApprovalScope
    {
        get => _pendingApprovalScope;
        set => SetField(ref _pendingApprovalScope, value);
    }

    public string PendingApprovalSteps
    {
        get => _pendingApprovalSteps;
        set => SetField(ref _pendingApprovalSteps, value);
    }

    public string PendingApprovalAcceptance
    {
        get => _pendingApprovalAcceptance;
        set => SetField(ref _pendingApprovalAcceptance, value);
    }

    public string PendingApprovalCodexBrief
    {
        get => _pendingApprovalCodexBrief;
        set => SetField(ref _pendingApprovalCodexBrief, value);
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
            OnPropertyChanged(nameof(AreModeSelectorsEnabled));
            OnPropertyChanged(nameof(CanStartAutomation));
            OnPropertyChanged(nameof(CanApproveAutomation));
            OnPropertyChanged(nameof(CanRejectAutomation));
            OnPropertyChanged(nameof(CanStopAutomation));
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
            OnPropertyChanged(nameof(CanStartAutomation));
        }
    }

    public bool HasPendingApproval
    {
        get => _hasPendingApproval;
        private set
        {
            if (!SetField(ref _hasPendingApproval, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanApproveAutomation));
            OnPropertyChanged(nameof(CanRejectAutomation));
        }
    }

    public bool IsAutomationActive
    {
        get => _isAutomationActive;
        private set
        {
            if (!SetField(ref _isAutomationActive, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanStartAutomation));
            OnPropertyChanged(nameof(CanStopAutomation));
        }
    }

    public bool CanStart => !IsBusy;

    public bool CanSend => !IsBusy && HasSession;

    public bool AreModeSelectorsEnabled => !IsBusy && !AutoModeEnabled;

    public bool CanStartAutomation => !IsBusy && HasSession && AutoModeEnabled && !IsAutomationActive;

    public bool CanApproveAutomation => !IsBusy && HasPendingApproval;

    public bool CanRejectAutomation => !IsBusy && HasPendingApproval;

    public bool CanStopAutomation => !IsBusy && IsAutomationActive;

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

    public string ModeLockHint => AutoModeEnabled
        ? "自动模式已锁定为 Claude=Plan Mode / Codex=Full Auto"
        : "关闭自动模式后可自由选择 Claude / Codex 启动参数";

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
        SessionPaneInfo = BuildSessionPaneInfo(session);
        SessionSocketPath = session.SocketPath;
        HasSession = true;
    }

    public void ApplyAutomationState(AutomationRunState state)
    {
        AutomationStatusLabel = state.Status.ToString();
        AutomationStatusDetail = state.StatusDetail;
        AutomationLastPacketSummary = string.IsNullOrWhiteSpace(state.LastPacketSummary) ? "暂无" : state.LastPacketSummary;
        AutomationLastError = string.IsNullOrWhiteSpace(state.LastError) ? "暂无" : state.LastError;
        AutomationUpdatedAt = state.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsAutomationActive = state.IsActive;

        if (state.PendingApproval is null)
        {
            ClearPendingApproval();
            return;
        }

        HasPendingApproval = true;
        PendingApprovalStage = $"阶段 {state.PendingApproval.StageId}";
        PendingApprovalTitle = DisplayOrPlaceholder(state.PendingApproval.Title);
        PendingApprovalSummary = DisplayOrPlaceholder(state.PendingApproval.Summary);
        PendingApprovalScope = DisplayOrPlaceholder(state.PendingApproval.Scope);
        PendingApprovalSteps = JoinLines(state.PendingApproval.Steps);
        PendingApprovalAcceptance = JoinLines(state.PendingApproval.AcceptanceCriteria);
        PendingApprovalCodexBrief = DisplayOrPlaceholder(state.PendingApproval.CodexBrief);
    }

    public void ResetAutomationState()
    {
        ApplyAutomationState(new AutomationRunState
        {
            Status = AutomationStageStatus.Idle,
            StatusDetail = "自动编排未启动。",
            LastPacketSummary = "暂无",
        });
        ApprovalNote = string.Empty;
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

    private void ApplyModeLock(bool autoModeEnabled)
    {
        if (autoModeEnabled)
        {
            _savedClaudePermissionMode = _claudePermissionMode;
            _savedCodexMode = _codexMode;
            ClaudePermissionMode = "plan";
            CodexMode = "full-auto";
            return;
        }

        ClaudePermissionMode = _savedClaudePermissionMode;
        CodexMode = _savedCodexMode;
    }

    private void ClearPendingApproval()
    {
        HasPendingApproval = false;
        PendingApprovalStage = "暂无";
        PendingApprovalTitle = "暂无";
        PendingApprovalSummary = "暂无";
        PendingApprovalScope = "暂无";
        PendingApprovalSteps = "暂无";
        PendingApprovalAcceptance = "暂无";
        PendingApprovalCodexBrief = "暂无";
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        var items = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => $"• {line.Trim()}")
            .ToArray();

        return items.Length == 0 ? "暂无" : string.Join(Environment.NewLine, items);
    }

    private static string DisplayOrPlaceholder(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "暂无" : value.Trim();
    }

    private static string BuildSessionPaneInfo(LauncherSession session)
    {
        var baseText = $"Left={session.LeftPaneId}, Right={session.RightPaneId}, Width={session.RightPanePercent}%, Claude={session.ClaudePermissionMode}, Codex={session.CodexMode}";
        if (!session.AutomationObserverEnabled)
        {
            return baseText;
        }

        return $"{baseText}, ClaudeView={session.ClaudeObserverPaneId?.ToString() ?? "无"}, CodexView={session.CodexObserverPaneId?.ToString() ?? "无"}";
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

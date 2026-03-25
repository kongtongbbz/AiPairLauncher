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

    private static readonly IReadOnlyList<ModeOption> AutomationAdvanceModes =
    [
        new() { Value = "manual-each-stage", Label = "逐轮审批" },
        new() { Value = "manual-first-then-auto", Label = "首轮审批后自动" },
        new() { Value = "full-auto-loop", Label = "全自动闭环" },
    ];

    private static readonly IReadOnlyList<ModeOption> WorktreeStrategies =
    [
        new() { Value = "none", Label = "禁用" },
        new() { Value = "subdirectory", Label = "仓库内 .worktrees/" },
    ];

    private static readonly IReadOnlyList<ModeOption> ThemeModes =
    [
        new() { Value = "dark", Label = "黑色" },
        new() { Value = "light", Label = "白色" },
        new() { Value = "eye-care", Label = "护眼" },
    ];

    private readonly StringBuilder _logBuilder = new();
    private readonly List<ManagedSessionRecord> _allSessionRecords = [];

    private ManagedSessionRecord? _selectedSessionRecord;
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
    private string _automationAdvancePolicy = "full-auto-loop";
    private string _automationAutoAdvanceStatusText = "未启用";
    private string _automationInterventionReason = "暂无";
    private string _approvalNote = string.Empty;
    private string _pendingApprovalStage = "暂无";
    private string _pendingApprovalTitle = "暂无";
    private string _pendingApprovalSummary = "暂无";
    private string _pendingApprovalScope = "暂无";
    private string _pendingApprovalSteps = "暂无";
    private string _pendingApprovalAcceptance = "暂无";
    private string _pendingApprovalCodexBrief = "暂无";
    private string _sessionSearchText = string.Empty;
    private string _sessionCounterSummary = "暂无会话";
    private string _selectedSessionDisplayName = "暂无";
    private string _selectedSessionStatus = "未选择";
    private string _selectedSessionStatusDetail = "从左侧选择一个会话";
    private string _selectedSessionWorkingDirectory = "暂无";
    private string _selectedSessionLastSeen = "暂无";
    private string _selectedSessionLastSummary = "暂无";
    private string _selectedSessionModeSummary = "暂无";
    private string _selectedSessionLastError = "暂无";
    private string _selectedSessionLaunchProfileSummary = "未使用模板";
    private string _selectedSessionReconnectSummary = "尚未重连";
    private string _selectedLaunchProfileId = string.Empty;
    private string _newLaunchProfileName = string.Empty;
    private string _launchProfileSummary = "使用右侧表单配置创建会话";
    private string _selectedThemeMode = "light";
    private string _themeSummary = "当前主题：白色";
    private string _worktreeStrategy = "none";
    private int _rightPanePercent = 60;
    private int _lastLines = 120;
    private int _automationPollIntervalMilliseconds = 1500;
    private int _automationCaptureLines = 220;
    private int _automationTimeoutSeconds = 600;
    private int _automationMaxAutoStages = 8;
    private int _automationMaxRetryPerStage = 2;
    private int _automationAutoApprovedStageCount;
    private int _automationCurrentStageRetryCount;
    private bool _submitAfterSend = true;
    private bool _automationSubmitOnSend = true;
    private bool _automationObserverEnabled = true;
    private bool _autoModeEnabled;
    private bool _useWorktree;
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

    public ObservableCollection<ManagedSessionRecord> SessionRecords { get; } = [];

    public ObservableCollection<LaunchProfile> LaunchProfiles { get; } = [];

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

    public string SessionSearchText
    {
        get => _sessionSearchText;
        set
        {
            if (!SetField(ref _sessionSearchText, value))
            {
                return;
            }

            ApplySessionFilter();
        }
    }

    public string SessionCounterSummary
    {
        get => _sessionCounterSummary;
        private set => SetField(ref _sessionCounterSummary, value);
    }

    public ManagedSessionRecord? SelectedSessionRecord
    {
        get => _selectedSessionRecord;
        set
        {
            if (!SetField(ref _selectedSessionRecord, value))
            {
                return;
            }

            ApplySelectedSessionRecord(value);
            OnPropertyChanged(nameof(HasSelectedSession));
        }
    }

    public string SelectedLaunchProfileId
    {
        get => _selectedLaunchProfileId;
        set
        {
            if (!SetField(ref _selectedLaunchProfileId, value))
            {
                return;
            }

            UpdateLaunchProfileSummary();
            OnPropertyChanged(nameof(CanApplyLaunchProfile));
        }
    }

    public string NewLaunchProfileName
    {
        get => _newLaunchProfileName;
        set
        {
            if (!SetField(ref _newLaunchProfileName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSaveLaunchProfile));
        }
    }

    public string LaunchProfileSummary
    {
        get => _launchProfileSummary;
        private set => SetField(ref _launchProfileSummary, value);
    }

    public string SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            var normalized = NormalizeThemeMode(value);
            if (!SetField(ref _selectedThemeMode, normalized))
            {
                return;
            }

            ThemeSummary = normalized switch
            {
                "light" => "当前主题：白色",
                "eye-care" => "当前主题：护眼",
                _ => "当前主题：黑色",
            };
        }
    }

    public string ThemeSummary
    {
        get => _themeSummary;
        private set => SetField(ref _themeSummary, value);
    }

    public bool UseWorktree
    {
        get => _useWorktree;
        set => SetField(ref _useWorktree, value);
    }

    public string WorktreeStrategy
    {
        get => _worktreeStrategy;
        set => SetField(ref _worktreeStrategy, NormalizeWorktreeStrategy(value));
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

    public string SelectedAutomationAdvancePolicyKey
    {
        get => _automationAdvancePolicy;
        set => SetField(ref _automationAdvancePolicy, NormalizeAutomationAdvancePolicy(value));
    }

    public int AutomationMaxAutoStages
    {
        get => _automationMaxAutoStages;
        set => SetField(ref _automationMaxAutoStages, Math.Max(1, value));
    }

    public int AutomationMaxRetryPerStage
    {
        get => _automationMaxRetryPerStage;
        set => SetField(ref _automationMaxRetryPerStage, Math.Max(0, value));
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

    public string SelectedSessionDisplayName
    {
        get => _selectedSessionDisplayName;
        private set => SetField(ref _selectedSessionDisplayName, value);
    }

    public string SelectedSessionStatus
    {
        get => _selectedSessionStatus;
        private set => SetField(ref _selectedSessionStatus, value);
    }

    public string SelectedSessionStatusDetail
    {
        get => _selectedSessionStatusDetail;
        private set => SetField(ref _selectedSessionStatusDetail, value);
    }

    public string SelectedSessionWorkingDirectory
    {
        get => _selectedSessionWorkingDirectory;
        private set => SetField(ref _selectedSessionWorkingDirectory, value);
    }

    public string SelectedSessionLastSeen
    {
        get => _selectedSessionLastSeen;
        private set => SetField(ref _selectedSessionLastSeen, value);
    }

    public string SelectedSessionLastSummary
    {
        get => _selectedSessionLastSummary;
        private set => SetField(ref _selectedSessionLastSummary, value);
    }

    public string SelectedSessionModeSummary
    {
        get => _selectedSessionModeSummary;
        private set => SetField(ref _selectedSessionModeSummary, value);
    }

    public string SelectedSessionLastError
    {
        get => _selectedSessionLastError;
        private set => SetField(ref _selectedSessionLastError, value);
    }

    public string SelectedSessionLaunchProfileSummary
    {
        get => _selectedSessionLaunchProfileSummary;
        private set => SetField(ref _selectedSessionLaunchProfileSummary, value);
    }

    public string SelectedSessionReconnectSummary
    {
        get => _selectedSessionReconnectSummary;
        set => SetField(ref _selectedSessionReconnectSummary, value);
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

    public string AutomationAutoAdvanceStatusText
    {
        get => _automationAutoAdvanceStatusText;
        private set => SetField(ref _automationAutoAdvanceStatusText, value);
    }

    public int AutomationAutoApprovedStageCount
    {
        get => _automationAutoApprovedStageCount;
        private set => SetField(ref _automationAutoApprovedStageCount, value);
    }

    public int AutomationCurrentStageRetryCount
    {
        get => _automationCurrentStageRetryCount;
        private set => SetField(ref _automationCurrentStageRetryCount, value);
    }

    public string AutomationInterventionReason
    {
        get => _automationInterventionReason;
        private set => SetField(ref _automationInterventionReason, value);
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

    public bool HasSelectedSession => SelectedSessionRecord is not null;

    public bool CanStart => !IsBusy;

    public bool CanSend => !IsBusy && HasSelectedSession;

    public bool AreModeSelectorsEnabled => !IsBusy && !AutoModeEnabled;

    public bool CanStartAutomation => !IsBusy && HasSelectedSession && !IsAutomationActive;

    public bool CanApproveAutomation => !IsBusy && HasPendingApproval;

    public bool CanRejectAutomation => !IsBusy && HasPendingApproval;

    public bool CanStopAutomation => !IsBusy && IsAutomationActive;

    public bool CanApplyLaunchProfile => !IsBusy && SelectedLaunchProfile is not null;

    public bool CanSaveLaunchProfile => !IsBusy && !string.IsNullOrWhiteSpace(NewLaunchProfileName);

    public bool CanReconnectSession => !IsBusy && HasSelectedSession;

    public bool CanFocusSelectedSession => !IsBusy && HasSelectedSession;

    public IReadOnlyList<ModeOption> ClaudeModeOptions => ClaudeModes;

    public IReadOnlyList<ModeOption> CodexModeOptions => CodexModes;

    public IReadOnlyList<ModeOption> AutomationAdvancePolicyOptions => AutomationAdvanceModes;

    public IReadOnlyList<ModeOption> WorktreeStrategyOptions => WorktreeStrategies;

    public IReadOnlyList<ModeOption> ThemeOptions => ThemeModes;

    public AutomationAdvancePolicy SelectedAutomationAdvancePolicy => ParseAutomationAdvancePolicy(_automationAdvancePolicy);

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
        ? "自动模式已锁定为 Claude=Plan Mode / Codex=Full Auto，推进策略与保护阈值在右侧配置"
        : "关闭自动模式后可自由选择 Claude / Codex 启动参数";

    public void ReplaceDependencies(IEnumerable<DependencyStatus> dependencies)
    {
        Dependencies.Clear();
        foreach (var dependency in dependencies)
        {
            Dependencies.Add(dependency);
        }
    }

    public void ApplySessionCatalog(IEnumerable<ManagedSessionRecord> sessionRecords, string? preferredSessionId = null)
    {
        _allSessionRecords.Clear();
        _allSessionRecords.AddRange(sessionRecords.Select(static record => record.Clone()));
        ApplySessionFilter(preferredSessionId);
    }

    public void ReplaceLaunchProfiles(IEnumerable<LaunchProfile> profiles)
    {
        LaunchProfiles.Clear();
        foreach (var profile in profiles.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            LaunchProfiles.Add(profile);
        }

        if (LaunchProfiles.Count == 0)
        {
            SelectedLaunchProfileId = string.Empty;
            LaunchProfileSummary = "尚未配置 Launch Profile";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedLaunchProfileId) ||
            LaunchProfiles.All(profile => !string.Equals(profile.ProfileId, SelectedLaunchProfileId, StringComparison.Ordinal)))
        {
            SelectedLaunchProfileId = LaunchProfiles[0].ProfileId;
            return;
        }

        UpdateLaunchProfileSummary();
        if (_selectedSessionRecord is not null)
        {
            ApplySelectedSessionRecord(_selectedSessionRecord);
        }
    }

    public LaunchProfile? ApplyLaunchProfile()
    {
        var profile = SelectedLaunchProfile;
        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            WorkingDirectory = profile.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkspacePrefix))
        {
            WorkspaceName = profile.WorkspacePrefix;
        }

        ClaudePermissionMode = profile.ClaudePermissionMode;
        CodexMode = profile.CodexMode;
        RightPanePercent = profile.RightPanePercent;
        AutoModeEnabled = profile.AutomationEnabled;
        AutomationObserverEnabled = profile.AutomationObserverEnabled;
        SelectedAutomationAdvancePolicyKey = profile.AutomationAdvancePolicy;
        AutomationPollIntervalMilliseconds = profile.AutomationPollIntervalMilliseconds;
        AutomationCaptureLines = profile.AutomationCaptureLines;
        AutomationTimeoutSeconds = profile.AutomationTimeoutSeconds;
        AutomationMaxAutoStages = profile.AutomationMaxAutoStages;
        AutomationMaxRetryPerStage = profile.AutomationMaxRetryPerStage;
        AutomationSubmitOnSend = profile.AutomationSubmitOnSend;
        UseWorktree = profile.DefaultUseWorktree;
        WorktreeStrategy = string.IsNullOrWhiteSpace(profile.DefaultWorktreeStrategy)
            ? profile.WorktreeStrategy
            : profile.DefaultWorktreeStrategy;

        return profile;
    }

    public LaunchProfile CaptureCurrentFormAsProfile()
    {
        return new LaunchProfile
        {
            Name = NewLaunchProfileName.Trim(),
            Description = "从当前新建会话表单保存",
            WorkingDirectory = WorkingDirectory,
            WorkspacePrefix = WorkspaceName,
            ClaudePermissionMode = ClaudePermissionMode,
            CodexMode = CodexMode,
            RightPanePercent = RightPanePercent,
            AutomationEnabled = AutoModeEnabled,
            AutomationObserverEnabled = AutomationObserverEnabled,
            AutomationAdvancePolicy = SelectedAutomationAdvancePolicyKey,
            AutomationPollIntervalMilliseconds = AutomationPollIntervalMilliseconds,
            AutomationCaptureLines = AutomationCaptureLines,
            AutomationTimeoutSeconds = AutomationTimeoutSeconds,
            AutomationMaxAutoStages = AutomationMaxAutoStages,
            AutomationMaxRetryPerStage = AutomationMaxRetryPerStage,
            AutomationSubmitOnSend = AutomationSubmitOnSend,
            DefaultUseWorktree = UseWorktree,
            WorktreeStrategy = WorktreeStrategy,
            DefaultWorktreeStrategy = WorktreeStrategy,
        };
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
        AutomationAutoAdvanceStatusText = state.AutoAdvanceEnabled ? "已启用" : "未启用";
        AutomationAutoApprovedStageCount = state.AutoApprovedStageCount;
        AutomationCurrentStageRetryCount = state.CurrentStageRetryCount;
        AutomationInterventionReason = DisplayOrPlaceholder(state.InterventionReason);
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

    private void ApplySessionFilter(string? preferredSessionId = null)
    {
        var keyword = _sessionSearchText.Trim();
        var filteredRecords = _allSessionRecords
            .Where(record => MatchesSessionFilter(record, keyword))
            .OrderByDescending(static record => record.IsPinned)
            .ThenByDescending(static record => !record.IsArchived)
            .ThenByDescending(static record => record.LastSeenAt)
            .ToArray();

        SessionRecords.Clear();
        foreach (var record in filteredRecords)
        {
            SessionRecords.Add(record);
        }

        SessionCounterSummary = _allSessionRecords.Count == 0
            ? "暂无会话"
            : $"{filteredRecords.Length} / {_allSessionRecords.Count} 个会话";

        var currentSessionId = preferredSessionId ?? _selectedSessionRecord?.SessionId;
        var selectedRecord = filteredRecords.FirstOrDefault(record =>
            !string.IsNullOrWhiteSpace(currentSessionId) &&
            string.Equals(record.SessionId, currentSessionId, StringComparison.Ordinal));

        SelectedSessionRecord = selectedRecord ?? filteredRecords.FirstOrDefault();
    }

    private void ApplySelectedSessionRecord(ManagedSessionRecord? record)
    {
        if (record is null)
        {
            SelectedSessionDisplayName = "暂无";
            SelectedSessionStatus = "未选择";
            SelectedSessionStatusDetail = "从左侧选择一个会话";
            SelectedSessionWorkingDirectory = "暂无";
            SelectedSessionLastSeen = "暂无";
            SelectedSessionLastSummary = "暂无";
            SelectedSessionModeSummary = "暂无";
            SelectedSessionLastError = "暂无";
            ApplySession(null);
            return;
        }

        SelectedSessionDisplayName = DisplayOrPlaceholder(record.DisplayName);
        SelectedSessionStatus = record.HealthDisplayText;
        SelectedSessionStatusDetail = DisplayOrPlaceholder(record.HealthDetail);
        SelectedSessionWorkingDirectory = DisplayOrPlaceholder(record.Session.WorkingDirectory);
        SelectedSessionLastSeen = record.LastSeenDisplay;
        SelectedSessionLastSummary = DisplayOrPlaceholder(record.LastSummary);
        SelectedSessionModeSummary = record.ModeSummary;
        SelectedSessionLastError = DisplayOrPlaceholder(record.LastError);
        SelectedSessionLaunchProfileSummary = ResolveLaunchProfileSummary(record.LaunchProfileId);
        SelectedSessionReconnectSummary = record.RuntimeBinding.IsAlive
            ? "当前绑定在线"
            : "当前绑定已失效，等待手动重连";
        ApplySession(record.Session);
    }

    private static bool MatchesSessionFilter(ManagedSessionRecord record, string keyword)
    {
        if (record.IsArchived)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return ContainsIgnoreCase(record.DisplayName, keyword)
               || ContainsIgnoreCase(record.GroupName, keyword)
               || ContainsIgnoreCase(record.Session.Workspace, keyword)
               || ContainsIgnoreCase(record.Session.WorkingDirectory, keyword)
               || ContainsIgnoreCase(record.LastSummary, keyword)
               || ContainsIgnoreCase(record.HealthDetail, keyword);
    }

    private static bool ContainsIgnoreCase(string? source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source)
               && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeAutomationAdvancePolicy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "manual-each-stage" => "manual-each-stage",
            "manual-first-then-auto" => "manual-first-then-auto",
            _ => "full-auto-loop",
        };
    }

    private static string NormalizeWorktreeStrategy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "subdirectory" => "subdirectory",
            _ => "none",
        };
    }

    private static string NormalizeThemeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dark" => "dark",
            "light" => "light",
            "eye-care" => "eye-care",
            _ => "light",
        };
    }

    private static AutomationAdvancePolicy ParseAutomationAdvancePolicy(string value)
    {
        return NormalizeAutomationAdvancePolicy(value) switch
        {
            "manual-each-stage" => AutomationAdvancePolicy.ManualEachStage,
            "manual-first-then-auto" => AutomationAdvancePolicy.ManualFirstStageThenAuto,
            _ => AutomationAdvancePolicy.FullAutoLoop,
        };
    }

    private LaunchProfile? SelectedLaunchProfile => LaunchProfiles
        .FirstOrDefault(profile => string.Equals(profile.ProfileId, SelectedLaunchProfileId, StringComparison.Ordinal));

    private void UpdateLaunchProfileSummary()
    {
        var profile = SelectedLaunchProfile;
        LaunchProfileSummary = profile is null
            ? "使用右侧表单配置创建会话"
            : $"{profile.Name} / Claude {profile.ClaudePermissionMode} / Codex {profile.CodexMode}";
    }

    private string ResolveLaunchProfileSummary(string? launchProfileId)
    {
        if (string.IsNullOrWhiteSpace(launchProfileId))
        {
            return "未使用模板";
        }

        var profile = LaunchProfiles.FirstOrDefault(item =>
            string.Equals(item.ProfileId, launchProfileId, StringComparison.Ordinal));
        return profile is null ? $"模板 {launchProfileId}" : $"模板 {profile.Name}";
    }
}

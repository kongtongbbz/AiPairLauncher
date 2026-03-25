using System.IO;
using System.Windows;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using AiPairLauncher.App.ViewModels;

namespace AiPairLauncher.App;

public partial class MainWindow : Window
{
    private readonly IDependencyService _dependencyService;
    private readonly ISessionStore _sessionStore;
    private readonly IAppCacheService _appCacheService;
    private readonly IWezTermService? _wezTermService;
    private readonly IAutoCollaborationCoordinator? _autoCollaborationCoordinator;
    private readonly MainWindowViewModel _viewModel;
    private bool _hasInitialized;
    private LauncherSession? _currentSession;

    public MainWindow(
        IDependencyService dependencyService,
        ISessionStore sessionStore,
        IAppCacheService appCacheService,
        IWezTermService? wezTermService = null,
        IAutoCollaborationCoordinator? autoCollaborationCoordinator = null)
    {
        _dependencyService = dependencyService ?? throw new ArgumentNullException(nameof(dependencyService));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _appCacheService = appCacheService ?? throw new ArgumentNullException(nameof(appCacheService));
        _wezTermService = wezTermService;
        _autoCollaborationCoordinator = autoCollaborationCoordinator;
        _viewModel = new MainWindowViewModel();

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;

        if (_autoCollaborationCoordinator is not null)
        {
            _autoCollaborationCoordinator.StateChanged += AutoCollaborationCoordinator_StateChanged;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;
        _viewModel.ResetAutomationState();
        await RefreshDependenciesAsync();
        await RestoreLastSessionAsync();
        _viewModel.AppendLog("UI 壳层已就绪，可执行启动、审批和自动编排。");
    }

    private async void RefreshDependencies_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDependenciesAsync();
    }

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("启动会话", async () =>
        {
            EnsureWezTermService();
            EnsureWorkingDirectory();
            await StopAutomationIfRunningAsync().ConfigureAwait(true);

            var request = new LaunchRequest
            {
                Workspace = string.IsNullOrWhiteSpace(_viewModel.WorkspaceName) ? null : _viewModel.WorkspaceName.Trim(),
                WorkingDirectory = _viewModel.WorkingDirectory.Trim(),
                ClaudePermissionMode = _viewModel.ClaudePermissionMode,
                CodexMode = _viewModel.CodexMode,
                AutomationEnabled = _viewModel.AutoModeEnabled,
                AutomationObserverEnabled = _viewModel.AutomationObserverEnabled,
                RightPanePercent = _viewModel.RightPanePercent,
                StartupTimeoutSeconds = 20,
            };

            _currentSession = await _wezTermService!
                .StartAiPairAsync(request)
                .ConfigureAwait(true);

            await _sessionStore.SaveAsync(_currentSession).ConfigureAwait(true);
            _viewModel.ApplySession(_currentSession);
            _viewModel.AppendLog($"会话启动成功，工作区: {_currentSession.Workspace}，Claude 模式: {_viewModel.ClaudeModeDisplayText}，Codex 模式: {_viewModel.CodexModeDisplayText}。");
            _viewModel.FooterMessage = $"最近操作: 已启动工作区 {_currentSession.Workspace}";

            if (_viewModel.AutoModeEnabled)
            {
                await StartAutomationCoreAsync(true).ConfigureAwait(true);
            }
        });
    }

    private async void StartAutomation_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("启动自动模式", async () =>
        {
            await StartAutomationCoreAsync(false).ConfigureAwait(true);
        });
    }

    private async void StopAutomation_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("停止自动模式", async () =>
        {
            await StopAutomationIfRunningAsync().ConfigureAwait(true);
        });
    }

    private async void ApprovePlan_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("批准计划", async () =>
        {
            EnsureAutomationCoordinator();
            var note = string.IsNullOrWhiteSpace(_viewModel.ApprovalNote) ? null : _viewModel.ApprovalNote.Trim();
            await _autoCollaborationCoordinator!
                .ApproveAsync(note)
                .ConfigureAwait(true);

            _viewModel.AppendLog("已批准待执行计划，并发送给 Codex。");
            _viewModel.FooterMessage = "最近操作: 已批准计划并发送给 Codex";
            _viewModel.ApprovalNote = string.Empty;
        });
    }

    private async void RejectPlan_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("退回计划", async () =>
        {
            EnsureAutomationCoordinator();
            var note = string.IsNullOrWhiteSpace(_viewModel.ApprovalNote) ? null : _viewModel.ApprovalNote.Trim();
            await _autoCollaborationCoordinator!
                .RejectAsync(note)
                .ConfigureAwait(true);

            _viewModel.AppendLog("已退回待执行计划，要求 Claude 重拟。");
            _viewModel.FooterMessage = "最近操作: 已退回计划给 Claude";
            _viewModel.ApprovalNote = string.Empty;
        });
    }

    private async void SendLeftToRight_Click(object sender, RoutedEventArgs e)
    {
        await SendContextAsync(fromLeftToRight: true);
    }

    private async void SendRightToLeft_Click(object sender, RoutedEventArgs e)
    {
        await SendContextAsync(fromLeftToRight: false);
    }

    private async void ReloadLastSession_Click(object sender, RoutedEventArgs e)
    {
        await RestoreLastSessionAsync();
    }

    private async void ClearAppCache_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteActionAsync("清理程序缓存", async () =>
        {
            var choice = MessageBox.Show(
                "将清理 AiPairLauncher 生成的最近会话记录和自动提示临时文件。\n不会删除安装文件、项目代码或已打开的终端窗口。\n\n是否继续？",
                "清理程序缓存",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
            {
                _viewModel.AppendLog("已取消清理程序缓存。");
                _viewModel.FooterMessage = "最近操作: 已取消清理程序缓存";
                return;
            }

            await StopAutomationIfRunningAsync().ConfigureAwait(true);

            var cleanupResult = await _appCacheService
                .ClearAsync()
                .ConfigureAwait(true);

            _currentSession = null;
            _viewModel.ApplySession(null);
            _viewModel.ResetAutomationState();
            _viewModel.AppendLog($"程序缓存清理完成: {cleanupResult.Summary}");
            _viewModel.FooterMessage = "最近操作: 已清理程序缓存";
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLog();
        _viewModel.AppendLog("日志已清空。");
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var initialFolder = Directory.Exists(_viewModel.WorkingDirectory)
            ? _viewModel.WorkingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择工作目录",
            InitialDirectory = initialFolder,
            Multiselect = false,
        };

        var result = dialog.ShowDialog(this);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _viewModel.WorkingDirectory = dialog.FolderName;
            _viewModel.AppendLog($"工作目录已切换: {_viewModel.WorkingDirectory}");
        }
    }

    private async Task RefreshDependenciesAsync()
    {
        await ExecuteActionAsync("检查依赖", async () =>
        {
            var dependencies = await _dependencyService.CheckAllAsync().ConfigureAwait(true);
            _viewModel.ReplaceDependencies(dependencies);

            var missingCount = dependencies.Count(x => !x.IsAvailable);
            if (missingCount == 0)
            {
                _viewModel.AppendLog("依赖检查完成，全部可用。");
                _viewModel.FooterMessage = "依赖状态: 全部可用";
                return;
            }

            _viewModel.AppendLog($"依赖检查完成，缺失 {missingCount} 项。");
            _viewModel.FooterMessage = $"依赖状态: 缺失 {missingCount} 项";
        });
    }

    private async Task RestoreLastSessionAsync()
    {
        await ExecuteActionAsync("加载会话", async () =>
        {
            _currentSession = await _sessionStore.LoadAsync().ConfigureAwait(true);

            if (_currentSession is null)
            {
                _viewModel.ApplySession(null);
                _viewModel.AppendLog("未找到最近会话记录。");
                return;
            }

            if (!await TryEnsureSessionAliveAsync(_currentSession, "最近会话已失效，已清空本地记录，请重新点击“启动 Ai Pair”。").ConfigureAwait(true))
            {
                return;
            }

            _viewModel.ApplySession(_currentSession);
            _viewModel.AppendLog($"已恢复最近会话: {_currentSession.Workspace}");
            _viewModel.FooterMessage = $"最近会话: {_currentSession.Workspace} / pane {_currentSession.LeftPaneId}->{_currentSession.RightPaneId}";
        });
    }

    private async Task SendContextAsync(bool fromLeftToRight)
    {
        await ExecuteActionAsync(fromLeftToRight ? "左向右发送" : "右向左发送", async () =>
        {
            EnsureWezTermService();
            await EnsureSessionReadyAsync().ConfigureAwait(true);

            var request = new SendContextRequest
            {
                Workspace = _currentSession!.Workspace,
                LastLines = _viewModel.LastLines,
                Instruction = _viewModel.TransferInstruction,
                Submit = _viewModel.SubmitAfterSend,
                FromLeftToRight = fromLeftToRight,
            };

            var transferResult = await _wezTermService!
                .SendContextAsync(_currentSession!, request)
                .ConfigureAwait(true);

            var direction = $"{transferResult.SourcePaneId}->{transferResult.TargetPaneId}";
            _viewModel.AppendLog($"上下文发送成功: {direction}, 字符数 {transferResult.CapturedLength}");
            _viewModel.FooterMessage = $"最近发送: {direction}, 抓取 {transferResult.LastLines} 行";
        });
    }

    private async Task StartAutomationCoreAsync(bool autoStartedByLaunch)
    {
        EnsureAutomationCoordinator();
        await EnsureSessionReadyAsync().ConfigureAwait(true);

        if (_currentSession is null)
        {
            throw new InvalidOperationException("没有可用会话，无法启动自动编排。");
        }

        if (!_currentSession.AutomationEnabledAtLaunch)
        {
            throw new InvalidOperationException("当前会话不是自动模式会话，请勾选“自动交互模式”后重新启动 Ai Pair。");
        }

        if (!autoStartedByLaunch &&
            (!string.Equals(_currentSession.ClaudePermissionMode, "plan", StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(_currentSession.CodexMode, "full-auto", StringComparison.OrdinalIgnoreCase)))
        {
            _viewModel.AppendLog("提示: 当前自动模式会话的代理模式与推荐值不一致，程序仍会继续尝试编排。");
        }

        var settings = new AutomationSettings
        {
            IsEnabled = _viewModel.AutoModeEnabled,
            InitialTaskPrompt = _viewModel.AutomationTaskPrompt.Trim(),
            AdvancePolicy = _viewModel.SelectedAutomationAdvancePolicy,
            PollIntervalMilliseconds = _viewModel.AutomationPollIntervalMilliseconds,
            CaptureLines = _viewModel.AutomationCaptureLines,
            SubmitOnSend = _viewModel.AutomationSubmitOnSend,
            NoProgressTimeoutSeconds = _viewModel.AutomationTimeoutSeconds,
            MaxAutoStages = _viewModel.AutomationMaxAutoStages,
            MaxRetryPerStage = _viewModel.AutomationMaxRetryPerStage,
        };

        await _autoCollaborationCoordinator!
            .StartAsync(_currentSession, settings)
            .ConfigureAwait(true);

        _viewModel.AppendLog($"自动编排已启动，推进策略: {_viewModel.SelectedAutomationAdvancePolicyKey}。");
        _viewModel.FooterMessage = "自动编排已启动";
    }

    private async Task StopAutomationIfRunningAsync()
    {
        if (_autoCollaborationCoordinator is null)
        {
            return;
        }

        var state = _autoCollaborationCoordinator.GetCurrentState();
        if (state.Status is AutomationStageStatus.Idle
            or AutomationStageStatus.Stopped
            or AutomationStageStatus.Completed
            or AutomationStageStatus.PausedOnError)
        {
            return;
        }

        await _autoCollaborationCoordinator.StopAsync().ConfigureAwait(true);
        _viewModel.AppendLog("已停止当前自动编排。");
        _viewModel.FooterMessage = "自动编排已停止";
    }

    private async void AutoCollaborationCoordinator_StateChanged(object? sender, AutomationRunState state)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _viewModel.ApplyAutomationState(state);
            _viewModel.AppendLog($"自动编排状态: {state.Status} / {state.StatusDetail}");
            if (!string.IsNullOrWhiteSpace(state.LastError))
            {
                _viewModel.AppendLog($"自动编排错误: {state.LastError}");
            }

            if (!string.IsNullOrWhiteSpace(state.InterventionReason))
            {
                _viewModel.AppendLog($"自动编排转人工接管: {state.InterventionReason}");
            }

            _viewModel.FooterMessage = state.LastError is null
                ? $"自动编排: {state.StatusDetail}"
                : $"自动编排暂停: {state.LastError}";
        });
    }

    private async Task EnsureSessionReadyAsync()
    {
        if (_currentSession is not null)
        {
            await EnsureLiveSessionOrThrowAsync(_currentSession).ConfigureAwait(true);
            return;
        }

        _currentSession = await _sessionStore.LoadAsync().ConfigureAwait(true);

        if (_currentSession is null)
        {
            _viewModel.ApplySession(null);
            throw new InvalidOperationException("当前没有可用会话，请先点击“启动 Ai Pair”。");
        }

        await EnsureLiveSessionOrThrowAsync(_currentSession).ConfigureAwait(true);
        _viewModel.ApplySession(_currentSession);
    }

    private void EnsureWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.WorkingDirectory))
        {
            throw new InvalidOperationException("工作目录不能为空。");
        }

        if (!Directory.Exists(_viewModel.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在: {_viewModel.WorkingDirectory}");
        }
    }

    private void EnsureWezTermService()
    {
        if (_wezTermService is not null)
        {
            return;
        }

        throw new InvalidOperationException("当前未注入 IWezTermService，无法执行终端操作。");
    }

    private void EnsureAutomationCoordinator()
    {
        if (_autoCollaborationCoordinator is not null)
        {
            return;
        }

        throw new InvalidOperationException("当前未注入 IAutoCollaborationCoordinator，无法执行自动编排。");
    }

    private async Task EnsureLiveSessionOrThrowAsync(LauncherSession session)
    {
        var message = "最近会话对应的 WezTerm 已失效，请重新点击“启动 Ai Pair”创建新会话。";
        if (await TryEnsureSessionAliveAsync(session, message).ConfigureAwait(true))
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private async Task<bool> TryEnsureSessionAliveAsync(LauncherSession session, string userMessage)
    {
        try
        {
            EnsureWezTermService();
            var panes = await _wezTermService!
                .GetWorkspacePanesAsync(session, session.Workspace)
                .ConfigureAwait(true);

            LauncherSessionValidator.EnsurePaneTopology(session, panes);
            return true;
        }
        catch (Exception ex)
        {
            _currentSession = null;
            await _sessionStore.ClearAsync().ConfigureAwait(true);
            _viewModel.ApplySession(null);
            _viewModel.AppendLog($"{userMessage} 详细原因: {ex.Message}");
            _viewModel.FooterMessage = userMessage;
            return false;
        }
    }

    private async Task ExecuteActionAsync(string actionName, Func<Task> action)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.IsBusy = true;
        _viewModel.StatusMessage = $"{actionName}中...";

        try
        {
            await action().ConfigureAwait(true);
            _viewModel.StatusMessage = $"{actionName}完成";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"{actionName}失败";
            _viewModel.AppendLog($"{actionName}失败: {ex.Message}");
            _viewModel.FooterMessage = $"{actionName}失败，请检查日志。";
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

}

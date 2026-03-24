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
    private readonly IWezTermService? _wezTermService;
    private readonly MainWindowViewModel _viewModel;
    private bool _hasInitialized;
    private LauncherSession? _currentSession;

    public MainWindow(
        IDependencyService dependencyService,
        ISessionStore sessionStore,
        IWezTermService? wezTermService = null)
    {
        _dependencyService = dependencyService ?? throw new ArgumentNullException(nameof(dependencyService));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _wezTermService = wezTermService;
        _viewModel = new MainWindowViewModel();

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;
        await RefreshDependenciesAsync();
        await RestoreLastSessionAsync();
        _viewModel.AppendLog("UI 壳层已就绪，可执行启动和上下文转发。");
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

            var request = new LaunchRequest
            {
                Workspace = string.IsNullOrWhiteSpace(_viewModel.WorkspaceName) ? null : _viewModel.WorkspaceName.Trim(),
                WorkingDirectory = _viewModel.WorkingDirectory.Trim(),
                ClaudePermissionMode = _viewModel.ClaudePermissionMode,
                CodexMode = _viewModel.CodexMode,
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
            _viewModel.ApplySession(_currentSession);

            if (_currentSession is null)
            {
                _viewModel.AppendLog("未找到最近会话记录。");
                return;
            }

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

    private async Task EnsureSessionReadyAsync()
    {
        if (_currentSession is not null)
        {
            return;
        }

        _currentSession = await _sessionStore.LoadAsync().ConfigureAwait(true);
        _viewModel.ApplySession(_currentSession);

        if (_currentSession is null)
        {
            throw new InvalidOperationException("当前没有可用会话，请先点击“启动 Ai Pair”。");
        }
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

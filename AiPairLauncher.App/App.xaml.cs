using System.IO;
using System.Windows;
using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Services;

namespace AiPairLauncher.App;

public partial class App : System.Windows.Application
{
    private IAppThemeService? _appThemeService;
    private INotificationService? _notificationService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // 在应用启动阶段集中构建依赖，后续可替换为完整 DI 容器。
            var commandLocator = new CommandLocator();
            var processRunner = new ProcessRunner();
            var wezTermConfigPath = Path.Combine(AppContext.BaseDirectory, "config", "app.wezterm.lua");
            var iconFilePath = Path.Combine(AppContext.BaseDirectory, "assets", "AiPairLauncher.ico");

            IDependencyService dependencyService = new DependencyService(commandLocator, processRunner);
            ISessionStore sessionStore = new SessionStore();
            IAppCacheService appCacheService = new AppCacheService(sessionStore);
            IWezTermService wezTermService = new WezTermService(commandLocator, processRunner, wezTermConfigPath);
            _appThemeService = new AppThemeService(Resources, sessionStore);
            var currentTheme = _appThemeService.LoadThemePreferenceAsync().GetAwaiter().GetResult();
            IAgentPacketParser packetParser = new AgentPacketParser();
            IAutoCollaborationCoordinatorFactory coordinatorFactory = new AutoCollaborationCoordinatorFactory(wezTermService, packetParser);
            ISessionRuntimeRegistry sessionRuntimeRegistry = new SessionRuntimeRegistry(coordinatorFactory);
            _notificationService = new WindowsNotificationService(iconFilePath);
            ISessionMonitorService sessionMonitorService = new SessionMonitorService(wezTermService, _notificationService);

            var window = new MainWindow(
                dependencyService,
                sessionStore,
                appCacheService,
                sessionMonitorService,
                sessionRuntimeRegistry,
                wezTermService,
                _appThemeService,
                currentTheme);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"应用启动失败: {ex.Message}",
                "AiPairLauncher",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notificationService?.Dispose();
        base.OnExit(e);
    }
}

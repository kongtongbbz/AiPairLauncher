using System.IO;
using System.Text;
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

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

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
                _notificationService,
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

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        System.Windows.MessageBox.Show(
            $"应用发生未处理异常，详情已写入日志。\n\n{e.Exception.Message}",
            "AiPairLauncher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteCrashLog("AppDomainUnhandledException", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskSchedulerUnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiPairLauncher",
                "logs");
            Directory.CreateDirectory(root);

            var filePath = Path.Combine(root, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            var builder = new StringBuilder();
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"Time: {DateTime.Now:O}");
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }
            else
            {
                builder.AppendLine("Exception: <null>");
            }

            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 崩溃日志写入失败时不再二次抛错，避免覆盖原始异常。
        }
    }
}

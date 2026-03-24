using System.IO;
using System.Windows;
using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Services;

namespace AiPairLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // 在应用启动阶段集中构建依赖，后续可替换为完整 DI 容器。
            var commandLocator = new CommandLocator();
            var processRunner = new ProcessRunner();
            var wezTermConfigPath = Path.Combine(AppContext.BaseDirectory, "config", "app.wezterm.lua");

            IDependencyService dependencyService = new DependencyService(commandLocator, processRunner);
            ISessionStore sessionStore = new SessionStore();
            IWezTermService wezTermService = new WezTermService(commandLocator, processRunner, wezTermConfigPath);

            var window = new MainWindow(dependencyService, sessionStore, wezTermService);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"应用启动失败: {ex.Message}",
                "AiPairLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }
}

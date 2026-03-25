using AiPairLauncher.App.Infrastructure;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class DependencyServiceTests
{
    [Fact(DisplayName = "test_dependency_service_sets_success_message_for_available_command")]
    public async Task DependencyServiceSetsSuccessMessageForAvailableCommandAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "AiPairLauncher.DependencyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var fakeCommandPath = Path.Combine(tempRoot, "wezterm.exe");
            await File.WriteAllTextAsync(fakeCommandPath, string.Empty);
            var locator = new CommandLocator();
            var runner = new FakeProcessRunner();
            var service = new DependencyService(locator, runner);

            var result = await runner.InvokeCheckAsync(service, "wezterm", fakeCommandPath);

            Assert.True(result.IsAvailable);
            Assert.Equal("命令可执行，版本探测成功。", result.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "1.0.0",
                StandardError = string.Empty,
            });
        }

        public async Task<AiPairLauncher.App.Models.DependencyStatus> InvokeCheckAsync(DependencyService service, string commandName, string commandPath)
        {
            var method = typeof(DependencyService)
                .GetMethod("CheckOneAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var task = (Task<AiPairLauncher.App.Models.DependencyStatus>)method.Invoke(
                service,
                [commandName, (IReadOnlyList<string>)new[] { commandPath }, (IReadOnlyList<string>)new[] { "--version" }, CancellationToken.None])!;

            return await task.ConfigureAwait(false);
        }
    }
}

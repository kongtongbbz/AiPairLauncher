using System.Diagnostics;
using System.IO;
using System.Text;
using AiPairLauncher.App.Infrastructure;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class ProcessRunnerTests
{
    [Fact(DisplayName = "test_process_runner_writes_stdin_as_utf8")]
    public async Task ProcessRunnerWritesStdinAsUtf8Async()
    {
        var runner = new ProcessRunner();
        var command = new ProcessCommand
        {
            FileName = "powershell.exe",
            Arguments =
            [
                "-NoLogo",
                "-NoProfile",
                "-Command",
                "$inputStream = [Console]::OpenStandardInput(); " +
                "$buffer = New-Object byte[] 64; " +
                "$read = $inputStream.Read($buffer, 0, $buffer.Length); " +
                "[BitConverter]::ToString($buffer, 0, $read)"
            ],
            StandardInput = "中文审批",
            Timeout = TimeSpan.FromSeconds(10),
        };

        var result = await runner.RunAsync(command);
        var expected = Convert.ToHexString(Encoding.UTF8.GetBytes("中文审批"));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.StandardOutput.Replace("-", string.Empty, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "test_process_runner_timeout_kills_process")]
    public async Task ProcessRunnerTimeoutKillsProcessAsync()
    {
        var runner = new ProcessRunner();
        var pidFile = Path.Combine(Path.GetTempPath(), $"aipair-runner-{Guid.NewGuid():N}.pid");
        var command = new ProcessCommand
        {
            FileName = "powershell.exe",
            Arguments =
            [
                "-NoLogo",
                "-NoProfile",
                "-Command",
                $"$PID | Out-File -Encoding ascii -LiteralPath '{pidFile}'; Start-Sleep -Seconds 10"
            ],
            Timeout = TimeSpan.FromMilliseconds(400),
        };

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(command));
        Assert.Contains("进程执行超时", exception.Message);

        var pidText = await WaitForPidFileAsync(pidFile);
        Assert.False(string.IsNullOrWhiteSpace(pidText));
        Assert.True(int.TryParse(pidText, out var pid));

        await Task.Delay(1200);
        Assert.False(IsProcessRunning(pid));
    }

    private static async Task<string?> WaitForPidFileAsync(string pidFile)
    {
        for (var i = 0; i < 20; i += 1)
        {
            if (File.Exists(pidFile))
            {
                return (await File.ReadAllTextAsync(pidFile)).Trim();
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

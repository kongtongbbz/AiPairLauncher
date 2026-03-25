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
}

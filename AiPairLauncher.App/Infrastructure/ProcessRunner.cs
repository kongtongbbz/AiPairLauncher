using System.Diagnostics;
using System.Text;

namespace AiPairLauncher.App.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);

        using var process = new Process();
        process.StartInfo = BuildStartInfo(command);

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程: {command.FileName}");
        }

        if (!command.WaitForExit)
        {
            return new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
            };
        }

        using var timeoutCts = new CancellationTokenSource(command.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            if (!string.IsNullOrEmpty(command.StandardInput))
            {
                await process.StandardInput.WriteAsync(command.StandardInput).ConfigureAwait(false);
            }

            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = (await stdoutTask.ConfigureAwait(false)).TrimEnd(),
                StandardError = (await stderrTask.ConfigureAwait(false)).TrimEnd(),
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            var killConfirmed = TryKillProcess(process, TimeSpan.FromMilliseconds(500), out var cleanupWarning);
            var timeoutMessage = $"进程执行超时: {command.FileName}";
            if (!killConfirmed && !string.IsNullOrWhiteSpace(cleanupWarning))
            {
                timeoutMessage = $"{timeoutMessage}；进程清理未完全确认: {cleanupWarning}";
            }

            throw new TimeoutException(timeoutMessage);
        }
    }

    private static ProcessStartInfo BuildStartInfo(ProcessCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            RedirectStandardError = command.WaitForExit,
            RedirectStandardOutput = command.WaitForExit,
            RedirectStandardInput = command.WaitForExit,
            CreateNoWindow = true,
        };

        if (command.WaitForExit)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        foreach (var arg in command.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (command.EnvironmentVariables is null)
        {
            return startInfo;
        }

        foreach (var entry in command.EnvironmentVariables)
        {
            if (entry.Value is null)
            {
                startInfo.Environment.Remove(entry.Key);
            }
            else
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        return startInfo;
    }

    private static bool TryKillProcess(Process process, TimeSpan waitForExit, out string? warning)
    {
        warning = null;
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit((int)waitForExit.TotalMilliseconds))
            {
                warning = "Kill 后未在预期时间内退出。";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            return false;
        }
    }
}

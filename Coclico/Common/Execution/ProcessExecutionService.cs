using System.Diagnostics;
using System.Text;

namespace Coclico.Services;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly ISecurityPolicy _securityPolicy;

    public ProcessExecutionService(ISecurityPolicy securityPolicy)
    {
        _securityPolicy = securityPolicy ?? throw new ArgumentNullException(nameof(securityPolicy));
    }

    public async Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        bool requiresElevation = false)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, "Executable path cannot be empty.");
        }

        List<string> argList = arguments?.ToList() ?? [];
        string argString = string.Join(" ", argList);
        string fullCommand = $"{executable} {argString}".Trim();

        if (_securityPolicy.IsCommandBlocked(fullCommand.ToLowerInvariant()))
        {
            LoggingService.LogWarning($"[ProcessExecutionService] Blocked by security policy: {executable} {argString}");
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, "Command blocked by security policy.");
        }

        bool captureOutput = !requiresElevation;

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = argString,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = !captureOutput,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = true,
            StandardOutputEncoding = captureOutput ? Encoding.UTF8 : null,
            StandardErrorEncoding = captureOutput ? Encoding.UTF8 : null
        };

        if (requiresElevation)
        {
            psi.Verb = "runas";
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            linkedCts.CancelAfter(timeout.Value);
        }

        CancellationToken token = linkedCts.Token;

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
            {
                return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, "Failed to start process.");
            }

            Task<string> stdoutTask = captureOutput
                ? proc.StandardOutput.ReadToEndAsync(token)
                : Task.FromResult(string.Empty);
            Task<string> stderrTask = captureOutput
                ? proc.StandardError.ReadToEndAsync(token)
                : Task.FromResult(string.Empty);
            Task exitTask = proc.WaitForExitAsync(token);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask, exitTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception exSwallow)
                {
                    LoggingService.LogException(exSwallow, "ProcessExecutionService.KillOnCancel");
                }

                bool wasTimedOut = timeout.HasValue && !ct.IsCancellationRequested;
                return new ProcessExecutionResult(
                    Success: false,
                    ExitCode: -1,
                    StandardOutput: stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty,
                    StandardError: stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty,
                    ErrorMessage: wasTimedOut ? "Execution timed out." : "Execution cancelled.",
                    TimedOut: wasTimedOut,
                    Cancelled: ct.IsCancellationRequested);
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            int exitCode = proc.ExitCode;

            return new ProcessExecutionResult(
                Success: exitCode == 0,
                ExitCode: exitCode,
                StandardOutput: stdout,
                StandardError: stderr,
                ErrorMessage: exitCode != 0 ? $"Process exited with code {exitCode}" : null);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"ProcessExecutionService.ExecuteAsync({executable})");
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, ex.Message);
        }
    }

    public bool LaunchApplication(string appPath, string? arguments = null, bool asAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(appPath))
        {
            return false;
        }

        string fullCommand = $"{appPath} {arguments}".Trim();
        if (_securityPolicy.IsCommandBlocked(fullCommand.ToLowerInvariant()))
        {
            LoggingService.LogWarning($"[ProcessExecutionService] Launch blocked: {fullCommand}");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            };

            if (asAdmin)
            {
                psi.Verb = "runas";
            }

            _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"ProcessExecutionService.LaunchApplication({appPath})");
            return false;
        }
    }
}
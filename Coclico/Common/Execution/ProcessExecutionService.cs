using System.Diagnostics;
using System.Linq;
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

        if (!IntegrityGate.IsSensitiveOperationAllowed("process-execution", out string integrityReason))
        {
            LoggingService.LogError($"[ProcessExecutionService] Execute blocked: {integrityReason}");
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, integrityReason);
        }

        List<string> argList = arguments?.ToList() ?? [];
        string rawArgString = string.Join(" ", argList);
        string fullCommand = $"{executable} {rawArgString}".Trim();

        if (_securityPolicy.IsCommandBlocked(fullCommand.ToLowerInvariant()))
        {
            LoggingService.LogWarning($"[ProcessExecutionService] Blocked by security policy: {executable} {rawArgString}");
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, "Command blocked by security policy.");
        }
        string argString = string.Join(" ", argList.Select(QuoteArgument));

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
                catch (Exception exSwallow) when (exSwallow is not System.OutOfMemoryException)
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, $"ProcessExecutionService.ExecuteAsync({executable})");
            return new ProcessExecutionResult(false, -1, string.Empty, string.Empty, ex.Message);
        }
    }
    internal static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }
        if (argument.IndexOfAny([' ', '\t', '"']) < 0 && !argument.EndsWith('\\'))
        {
            return argument;
        }

        var sb = new System.Text.StringBuilder(argument.Length + 8);
        _ = sb.Append('"');

        int trailingBackslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                trailingBackslashes++;
                continue;
            }

            if (c == '"')
            {
                _ = sb.Append('\\', trailingBackslashes * 2 + 1);
                _ = sb.Append('"');
            }
            else
            {
                if (trailingBackslashes > 0)
                {
                    _ = sb.Append('\\', trailingBackslashes);
                }
                _ = sb.Append(c);
            }

            trailingBackslashes = 0;
        }

        if (trailingBackslashes > 0)
        {
            _ = sb.Append('\\', trailingBackslashes * 2);
        }

        _ = sb.Append('"');
        return sb.ToString();
    }

    public bool LaunchApplication(string appPath, string? arguments = null, bool asAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(appPath))
        {
            return false;
        }

        if (!IntegrityGate.IsSensitiveOperationAllowed(asAdmin ? "launch-as-admin" : "launch-application", out string launchReason))
        {
            LoggingService.LogError($"[ProcessExecutionService] Launch blocked: {launchReason}");
            return false;
        }

        string safeArguments = RequoteArguments(arguments);

        string fullCommand = string.IsNullOrWhiteSpace(safeArguments) ? appPath : $"{appPath} {safeArguments}";
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
                Arguments = safeArguments,
                UseShellExecute = true
            };

            if (asAdmin)
            {
                psi.Verb = "runas";
            }

            _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, $"ProcessExecutionService.LaunchApplication({appPath})");
            return false;
        }
    }

    internal static string RequoteArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        List<string> tokens = [];
        var current = new StringBuilder(arguments.Length);
        bool inQuotes = false;

        foreach (char c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    _ = current.Clear();
                }
                continue;
            }

            _ = current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return string.Join(" ", tokens.Select(QuoteArgument));
    }
}

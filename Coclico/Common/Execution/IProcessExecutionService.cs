namespace Coclico.Services;

public sealed record ProcessExecutionResult(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? ErrorMessage = null,
    bool TimedOut = false,
    bool Cancelled = false);

public interface IProcessExecutionService
{
    Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        bool requiresElevation = false);

    bool LaunchApplication(string appPath, string? arguments = null, bool asAdmin = false);
}

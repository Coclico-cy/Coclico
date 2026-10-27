using System.Diagnostics;

namespace Coclico.Modules.UpdateCenter.Services;

public sealed class SecureProcessRunner
{
    private static readonly IReadOnlySet<string> AllowedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "winget.exe",
        "usoclient.exe",
        "powershell.exe"
    };

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public async Task<(int ExitCode, string Output)> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        if (!AllowedExecutables.Contains(fileName))
            throw new InvalidOperationException($"Executable not allowed: {fileName}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await stdoutTask.ConfigureAwait(false);
        var errors = await stderrTask.ConfigureAwait(false);

        return (process.ExitCode, string.IsNullOrWhiteSpace(errors) ? output : output + Environment.NewLine + errors);
    }
}

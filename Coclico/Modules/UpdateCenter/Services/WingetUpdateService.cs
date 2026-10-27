using System.Text.RegularExpressions;
using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public partial class WingetUpdateService(SecureProcessRunner runner)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public virtual async Task<IReadOnlyList<UpdateItem>> ScanAsync(CancellationToken cancellationToken)
    {
        runner.Timeout = DefaultTimeout;
        var (exitCode, output) = await runner.RunAsync("winget.exe",
            new[] { "upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity" },
            cancellationToken).ConfigureAwait(false);

        if (exitCode is not (0 or -1978335189))
            throw new InvalidOperationException($"winget exit code {exitCode}");

        return Parse(output);
    }

    public static IReadOnlyList<UpdateItem> Parse(string output)
    {
        return output.Replace("\r", string.Empty)
            .Split('\n')
            .Select(rawLine => rawLine.Trim())
            .Where(raw => raw.Length > 0 && !raw.StartsWith('-'))
            .Select(line => ColumnSeparator().Split(line))
            .Where(cols => cols.Length >= 4 && !cols[0].Equals("Name", StringComparison.OrdinalIgnoreCase))
            .Select(columns => (
                Name: columns[0].Trim(),
                Id: columns[1].Trim(),
                Current: columns[2].Trim(),
                Available: columns[3].Trim()))
            .Where(columns => columns.Id.Length > 0 && columns.Current.Length > 0 && columns.Available.Length > 0)
            .Select(columns => new UpdateItem
            {
                Id = columns.Id,
                Name = columns.Name,
                Kind = UpdateKind.Application,
                Source = UpdateSource.Winget,
                Status = columns.Current.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                    ? UpdateItemStatus.UnknownVersion
                    : UpdateItemStatus.Available,
                CurrentVersion = columns.Current,
                AvailableVersion = columns.Available
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex ColumnSeparator();

    public virtual async Task<bool> UpgradeAsync(string packageId, CancellationToken cancellationToken)
    {
        if (!WingetPackageIdValidator.IsValid(packageId))
            throw new InvalidOperationException("Invalid package id.");

        runner.Timeout = TimeSpan.FromMinutes(15);
        var (exitCode, _) = await runner.RunAsync("winget.exe",
            new[] { "upgrade", "--id", packageId, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" },
            cancellationToken).ConfigureAwait(false);

        return exitCode == 0;
    }
}

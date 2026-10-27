namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class WindowsHealthCheck : IDiagnosticCheck
{
    private const int QuickMaxEventGroups = 8;
    private const int FullMaxEventGroups = 25;
    private const int MaxEventsScanned = 1000;
    private static readonly TimeSpan EventWindow = TimeSpan.FromDays(7);

    public string Id => "windows.health";
    public DiagnosticCategory Category => DiagnosticCategory.Windows;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int maxGroups = context.Request.Mode == DiagnosticMode.Quick ? QuickMaxEventGroups : FullMaxEventGroups;

        List<EventGroupFacts> groups = [];
        int unexpectedShutdowns = 0;
        bool eventLogAccessible = true;

        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            unexpectedShutdowns = CountUnexpectedShutdowns();
            CollectCriticalGroups(maxGroups, context.CancellationToken, groups);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is EventLogNotFoundException or EventLogException or UnauthorizedAccessException)
        {
            eventLogAccessible = false;
            Coclico.Services.LoggingService.LogException(ex, $"{Id}.EventLog");
        }

        WindowsFacts facts = new(
            Environment.OSVersion.VersionString,
            Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            unexpectedShutdowns,
            groups,
            eventLogAccessible);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateWindows(facts)];

        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "WINDOWS_INVENTORY_001",
            DiagnosticCategory.Windows,
            "Diag_Finding_WindowsInventory_Title",
            "Diag_Finding_WindowsInventory_Summary",
            [
                DiagnosticEvidence.Measured("windows.version", "Diag_Evidence_OsVersion", facts.OsVersion, "", "Environment"),
                DiagnosticEvidence.Measured("windows.build", "Diag_Evidence_OsBuild", facts.OsBuild, "", "Environment"),
                DiagnosticEvidence.Measured("windows.uptimeMinutes", "Diag_Evidence_Uptime", Math.Round(facts.Uptime.TotalMinutes, 0), "min", "Environment.TickCount"),
                DiagnosticEvidence.Measured("windows.eventLogAccessible", "Diag_Evidence_EventLog", eventLogAccessible ? "true" : "false", "", "EventLog:System")
            ]));

        return Task.FromResult(DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings));
    }

    private static int CountUnexpectedShutdowns()
    {
        return CountEvents("<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*" +
            "[System[(EventID=41 or EventID=6008)]]</Select></Query></QueryList>");
    }

    private static void CollectCriticalGroups(int maxGroups, CancellationToken ct, List<EventGroupFacts> groups)
    {
        long windowMs = (long)EventWindow.TotalMilliseconds;
        string filter = string.Create(
            CultureInfo.InvariantCulture,
            $"<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) &lt;= {windowMs}]]]</Select></Query></QueryList>");

        Dictionary<string, EventGroupFacts> grouped = [];
        int scanned = 0;

        using EventLogReader reader = new(new EventLogQuery("System", PathType.LogName, filter));
        while (reader.ReadEvent() is { } record && scanned < MaxEventsScanned)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            string provider = record.ProviderName ?? "Unknown";
            string key = $"{provider}/{record.Id}";

            grouped[key] = grouped.TryGetValue(key, out EventGroupFacts? existing)
                ? existing with { Count = existing.Count + 1 }
                : new EventGroupFacts(
                    provider,
                    record.Id,
                    1,
                    record.TimeCreated?.ToUniversalTime(),
                    "Error");
        }

        foreach (EventGroupFacts group in grouped.Values.OrderByDescending(g => g.Count).Take(maxGroups))
        {
            groups.Add(group);
        }
    }

    private static int CountEvents(string filter)
    {
        int count = 0;
        using EventLogReader reader = new(new EventLogQuery("System", PathType.LogName, filter));
        while (reader.ReadEvent() is not null)
        {
            count++;
            if (count > 200)
            {
                break;
            }
        }

        return count;
    }
}

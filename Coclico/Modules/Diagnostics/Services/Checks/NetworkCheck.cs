namespace Coclico.Modules.Diagnostics.Services.Checks;

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;

public sealed class NetworkCheck : IDiagnosticCheck
{
    private static readonly string[] QuickTargets = ["1.1.1.1", "8.8.8.8"];
    private static readonly string[] FullTargets = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
    private const int QuickProbes = 3;
    private const int FullProbes = 4;
    private const int ProbeTimeoutMs = 1000;
    private const string DnsHost = "www.microsoft.com";

    public string Id => "network.probe";
    public DiagnosticCategory Category => DiagnosticCategory.Network;
    public bool IsQuick => true;
    public bool IsSupported(DiagnosticEnvironment environment) => true;

    public async Task<DiagnosticCheckResult> RunAsync(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool quick = context.Request.Mode == DiagnosticMode.Quick;
        string[] targets = quick ? QuickTargets : FullTargets;
        int probesPerTarget = quick ? QuickProbes : FullProbes;

        List<NetworkInterface> upInterfaces = [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up
                && i.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)];

        NetworkInterface? active = upInterfaces
            .FirstOrDefault(i => i.GetIPProperties().GatewayAddresses.Count > 0);

        IPAddress? gateway = active?.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address;
        long? linkSpeed = active is not null && active.Speed > 0 ? active.Speed / 1_000_000 : null;

        GatewayProbeFacts gatewayFacts = await ProbeGatewayAsync(gateway, probesPerTarget, context.CancellationToken)
            .ConfigureAwait(false);

        DnsProbeFacts dnsFacts = ResolveDns(context.CancellationToken);

        List<ExternalProbeFacts> external = [];
        foreach (string target in targets)
        {
            external.Add(await ProbeTargetAsync(target, probesPerTarget, context.CancellationToken).ConfigureAwait(false));
        }

        NetworkFacts facts = new(
            [.. upInterfaces.Select(i => i.Name)],
            upInterfaces.Count > 0,
            active?.Name,
            linkSpeed,
            gatewayFacts,
            dnsFacts,
            external);

        List<DiagnosticFinding> findings = [.. DiagnosticRuleEngine.EvaluateNetwork(facts)];
        findings.Add(DiagnosticCheckUtils.InventoryFinding(
            "NETWORK_INVENTORY_001",
            DiagnosticCategory.Network,
            "Diag_Finding_NetworkInventory_Title",
            "Diag_Finding_NetworkInventory_Summary",
            [
                DiagnosticEvidence.Measured("network.activeAdapter", "Diag_Evidence_ActiveAdapter", active?.Name ?? "-", "", "NetworkInterface"),
                DiagnosticEvidence.Measured("network.linkSpeedMbps", "Diag_Evidence_LinkSpeed", linkSpeed?.ToString() ?? "-", "Mbps", "NetworkInterface"),
                DiagnosticEvidence.Measured("network.gateway", "Diag_Evidence_Gateway", gateway?.ToString() ?? "-", "", "NetworkInterface"),
                DiagnosticEvidence.Measured("network.dns.resolveMs", "Diag_Evidence_DnsDuration", dnsFacts.ResolveMs is null ? "-" : Math.Round(dnsFacts.ResolveMs.Value, 0).ToString(), "ms", "DnsQuery")
            ]));

        return DiagnosticCheckUtils.Complete(Id, Category, IsQuick, findings);
    }

    private static async Task<GatewayProbeFacts> ProbeGatewayAsync(IPAddress? gateway, int probes, CancellationToken ct)
    {
        if (gateway is null)
        {
            return new GatewayProbeFacts(null, 0, 0, null, null);
        }

        (int sent, int received, List<double> times) = await PingAsync(gateway.ToString(), probes, ct).ConfigureAwait(false);
        return new GatewayProbeFacts(gateway.ToString(), sent, received, Median(times), P95(times));
    }

    private static async Task<ExternalProbeFacts> ProbeTargetAsync(string target, int probes, CancellationToken ct)
    {
        (int sent, int received, List<double> times) = await PingAsync(target, probes, ct).ConfigureAwait(false);
        return new ExternalProbeFacts(target, sent, received, Median(times), P95(times));
    }

    private static async Task<(int Sent, int Received, List<double> TimesMs)> PingAsync(
        string host, int probes, CancellationToken ct)
    {
        List<double> times = [];
        int received = 0;

        using Ping ping = new();
        for (int i = 0; i < probes; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PingReply reply = await ping.SendPingAsync(host, ProbeTimeoutMs).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    received++;
                    times.Add(reply.RoundtripTime);
                }
            }
            catch (Exception ex) when (ex is PingException or SocketException or OperationCanceledException)
            {
                if (ex is OperationCanceledException && ct.IsCancellationRequested)
                {
                    throw;
                }

            }
        }

        return (probes, received, times);
    }

    private static DnsProbeFacts ResolveDns(CancellationToken ct)
    {
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            _ = Dns.GetHostEntry(DnsHost);
            watch.Stop();
            return new DnsProbeFacts(DnsHost, true, watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }

            watch.Stop();
            return new DnsProbeFacts(DnsHost, false, watch.Elapsed.TotalMilliseconds);
        }
    }

    private static double? Median(List<double> values)
        => values.Count == 0 ? null : values.OrderBy(v => v).ElementAt(values.Count / 2);

    private static double? P95(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        List<double> sorted = [.. values.OrderBy(v => v)];
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)];
    }
}

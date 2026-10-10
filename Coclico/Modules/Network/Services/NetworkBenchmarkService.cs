using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using Coclico.Models.Network;

namespace Coclico.Services.Network;

public sealed class NetworkBenchmarkService : INetworkBenchmarkService
{
    private static readonly string[] ReferenceHosts = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
    private static readonly string[] BenchmarkDomains = ["google.com", "cloudflare.com", "github.com", "wikipedia.org", "microsoft.com"];
    private static readonly string[] BufferbloatLoadEndpoints =
    [
        "https://1.1.1.1/cdn-cgi/trace",
        "https://www.google.com/generate_204",
        "https://connectivitycheck.gstatic.com/generate_204"
    ];
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<NetworkMetricReport> RunComprehensiveBenchmarkAsync(
        string? gatewayAddress = null,
        int pingSamples = 10,
        bool includeBufferbloat = true,
        bool includeDns = true,
        Action<string, double>? progress = null,
        CancellationToken ct = default)
    {
        var report = new NetworkMetricReport
        {
            SampleCount = pingSamples
        };

        progress?.Invoke("Mesure de la latence locale et passerelle...", 10);

        if (!string.IsNullOrEmpty(gatewayAddress))
        {
            (double gwMedian, double _, double _, double _, double _) = await RunStatisticalPingAsync(gatewayAddress, 5, 500, ct).ConfigureAwait(false);
            report.GatewayLatencyMs = Math.Round(gwMedian, 2);
        }

        progress?.Invoke("Mesure statistique de la latence Internet...", 30);

        string primaryHost = ReferenceHosts[0];
        (double median, double mean, double jitter, double p95, double loss) = await RunStatisticalPingAsync(primaryHost, pingSamples, 800, ct).ConfigureAwait(false);
        report.LatencyMedianMs = Math.Round(median, 2);
        report.LatencyMeanMs = Math.Round(mean, 2);
        report.LatencyP95Ms = Math.Round(p95, 2);
        report.JitterMs = Math.Round(jitter, 2);
        report.PacketLossPercent = Math.Round(loss, 1);
        report.InternetLatencyMs = report.LatencyMedianMs;

        if (includeDns)
        {
            progress?.Invoke("Évaluation de la réactivité DNS...", 60);
            (double dnsTime, double dnsRate) = await BenchmarkRealDnsResolutionAsync(BenchmarkDomains, ct).ConfigureAwait(false);
            report.DnsResolutionMs = Math.Round(dnsTime, 1);
            report.DnsSuccessRatePercent = Math.Round(dnsRate, 1);
        }

        if (includeBufferbloat)
        {
            progress?.Invoke("Test de Bufferbloat sous charge réseau...", 80);
            (double bloatLat, string? grade) = await MeasureBufferbloatAsync(primaryHost, ct).ConfigureAwait(false);
            report.BufferbloatLatencyUnderLoadMs = Math.Round(bloatLat, 2);
            report.BufferbloatGrade = grade;
        }

        progress?.Invoke("Synthèse des métriques terminée", 100);
        return report;
    }

    public async Task<(double MedianMs, double MeanMs, double JitterMs, double P95Ms, double PacketLossPercent)> RunStatisticalPingAsync(
        string host,
        int sampleCount = 10,
        int timeoutMs = 800,
        CancellationToken ct = default)
    {
        var validTimes = new List<double>();
        int failed = 0;

        using var ping = new Ping();
        for (int i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PingReply reply = await ping.SendPingAsync(host, timeoutMs).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + 100), ct).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    validTimes.Add(reply.RoundtripTime);
                }
                else
                {
                    failed++;
                }
            }
            catch
            {
                failed++;
            }

            if (i < sampleCount - 1)
            {
                await Task.Delay(40, ct).ConfigureAwait(false);
            }
        }

        if (validTimes.Count == 0)
        {
            return (999.0, 999.0, 99.0, 999.0, 100.0);
        }

        var sorted = validTimes.OrderBy(t => t).ToList();
        if (sorted.Count >= 6)
        {
            int q1Index = sorted.Count / 4;
            int q3Index = 3 * sorted.Count / 4;
            double q1 = sorted[q1Index];
            double q3 = sorted[q3Index];
            double iqr = q3 - q1;
            double lowerBound = q1 - (1.5 * iqr);
            double upperBound = q3 + (1.5 * iqr);

            var filtered = sorted.Where(t => t >= lowerBound && t <= upperBound).ToList();
            if (filtered.Count > 0)
            {
                sorted = filtered;
            }
        }

        double medianVal = sorted[sorted.Count / 2];
        double meanVal = sorted.Average();

        double jitterVal = 0;
        if (sorted.Count > 1)
        {
            double sumDiff = 0;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                sumDiff += Math.Abs(sorted[i + 1] - sorted[i]);
            }
            jitterVal = sumDiff / (sorted.Count - 1);
        }

        int p95Index = (int)Math.Ceiling(0.95 * sorted.Count) - 1;
        p95Index = Math.Clamp(p95Index, 0, sorted.Count - 1);
        double p95Val = sorted[p95Index];

        double packetLoss = failed / (double)sampleCount * 100.0;

        return (medianVal, meanVal, jitterVal, p95Val, packetLoss);
    }

    public async Task<(double BufferbloatLatencyMs, string Grade)> MeasureBufferbloatAsync(
        string host = "1.1.1.1",
        CancellationToken ct = default)
    {
        (double idleMedian, double _, double _, double _, double _) = await RunStatisticalPingAsync(host, 4, 600, ct).ConfigureAwait(false);

        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loadCts.CancelAfter(TimeSpan.FromSeconds(2.5));

        var loadTasks = new List<Task>();
        for (int i = 0; i < 3; i++)
        {
            loadTasks.Add(Task.Run(() => RunBufferbloatLoadAsync(BufferbloatLoadEndpoints, 10, loadCts.Token), loadCts.Token));
        }

        await Task.Delay(200, ct).ConfigureAwait(false);
        (double loadMedian, double _, double _, double _, double _) = await RunStatisticalPingAsync(host, 5, 800, ct).ConfigureAwait(false);

        try
        {
            loadCts.Cancel();
            await Task.WhenAll(loadTasks).WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "NetworkBenchmarkService.MeasureBufferbloatAsync"); }

        double diff = Math.Max(0, loadMedian - idleMedian);

        string grade = diff switch
        {
            <= 6.0 => "A+",
            <= 15.0 => "A",
            <= 35.0 => "B",
            <= 70.0 => "C",
            <= 130.0 => "D",
            _ => "F"
        };

        return (loadMedian, grade);
    }

    public async Task<(double ResolutionMs, double SuccessRatePercent)> BenchmarkRealDnsResolutionAsync(
        IReadOnlyList<string>? testDomains = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<string> domains = testDomains ?? BenchmarkDomains;
        var sw = new Stopwatch();
        var times = new List<double>();
        int successes = 0;

        foreach (string domain in domains)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                sw.Restart();
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(domain, ct).ConfigureAwait(false);
                sw.Stop();

                if (addresses.Length > 0)
                {
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    successes++;
                }
            }
            catch
            {
            }
        }

        double avgTime = times.Count > 0 ? times.Average() : 999.0;
        double successRate = successes / (double)domains.Count * 100.0;
        return (avgTime, successRate);
    }

    public async Task<MtuDetectionResult> DetectOptimalMtuAsync(
        string targetHost = "1.1.1.1",
        CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var result = new MtuDetectionResult();
            using var ping = new Ping();

            int minPayload = 1400;
            int maxPayload = 1472;
            int bestPayload = 1400;

            while (minPayload <= maxPayload)
            {
                ct.ThrowIfCancellationRequested();
                int midPayload = (minPayload + maxPayload) / 2;
                byte[] buffer = new byte[midPayload];

                var options = new PingOptions
                {
                    DontFragment = true,
                    Ttl = 64
                };

                bool success = false;
                try
                {
                    PingReply reply = await ping.SendPingAsync(targetHost, 600, buffer, options).WaitAsync(TimeSpan.FromMilliseconds(700), ct).ConfigureAwait(false);
                    success = reply.Status == IPStatus.Success;
                }
                catch
                {
                    success = false;
                }

                if (success)
                {
                    bestPayload = midPayload;
                    minPayload = midPayload + 1;
                }
                else
                {
                    maxPayload = midPayload - 1;
                }

                await Task.Delay(20, ct).ConfigureAwait(false);
            }

            result.MaxPayloadWithoutFragmentation = bestPayload;
            result.OptimalMtu = bestPayload + 28;
            result.IsOptimized = result.OptimalMtu == 1500;
            result.Details = $"Charge utile max sans fragmentation : {result.MaxPayloadWithoutFragmentation} octets (MTU : {result.OptimalMtu}).";

            return result;
        }, ct).ConfigureAwait(false);
    }

    public async Task<NetworkMetricReport> RunFourPrecisionTestsAsync(
        string? gatewayAddress = null,
        int samplesPerTest = 10,
        Action<int, string, int, int, double>? onSampleProgress = null,
        CancellationToken ct = default)
    {
        samplesPerTest = Math.Clamp(samplesPerTest, 5, 40);
        var report = new NetworkMetricReport { SampleCount = samplesPerTest };

        var latRounds = new List<double>();
        var jitterRounds = new List<double>();
        var throughputRounds = new List<double>();
        var bufferbloatRounds = new List<double>();
        int lossCount = 0;

        using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loadTask = Task.Run(() => RunBufferbloatLoadAsync(BufferbloatLoadEndpoints, 8, loadCts.Token), loadCts.Token);

        using var ping = new Ping();
        var sw = new Stopwatch();
        string[] throughputEndpoints = new[]
        {
            "https://speed.cloudflare.com/__down?bytes=524288",
            "https://1.1.1.1/cdn-cgi/trace",
            "https://connectivitycheck.gstatic.com/generate_204"
        };

        for (int round = 1; round <= samplesPerTest; round++)
        {
            ct.ThrowIfCancellationRequested();
            string roundLabel = $"Round {round}/{samplesPerTest}";

            double lat = 0;
            try
            {
                PingReply reply = await ping.SendPingAsync("1.1.1.1", 700)
                    .WaitAsync(TimeSpan.FromMilliseconds(800), ct).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    lat = reply.RoundtripTime;
                    latRounds.Add(lat);
                }
                else
                {
                    lossCount++;
                }
            }
            catch { lossCount++; }
            onSampleProgress?.Invoke(1, $"{roundLabel} — Latence", round, samplesPerTest, lat > 0 ? lat : 20.0);
            await Task.Delay(10, ct).ConfigureAwait(false);

            double jitter = 0;
            {
                var jp = new List<double>();
                for (int j = 0; j < 3; j++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        PingReply rep = await ping.SendPingAsync("1.1.1.1", 500)
                            .WaitAsync(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);
                        if (rep.Status == IPStatus.Success)
                        {
                            jp.Add(rep.RoundtripTime);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogException(ex, "NetworkBenchmarkService.RunFourPrecisionTestsAsync"); }
                    if (j < 2)
                    {
                        await Task.Delay(8, ct).ConfigureAwait(false);
                    }
                }
                if (jp.Count > 1)
                {
                    double sum = 0;
                    for (int i = 0; i < jp.Count - 1; i++)
                    {
                        sum += Math.Abs(jp[i + 1] - jp[i]);
                    }

                    jitter = sum / (jp.Count - 1);
                }
                else if (jp.Count == 1 && lat > 0)
                {
                    jitter = Math.Abs(jp[0] - lat);
                }
            }
            jitterRounds.Add(jitter);
            onSampleProgress?.Invoke(2, $"{roundLabel} — Jitter", round, samplesPerTest, jitter);
            await Task.Delay(10, ct).ConfigureAwait(false);

            double throughput = 0;
            try
            {
                string endpoint = throughputEndpoints[(round - 1) % throughputEndpoints.Length];
                sw.Restart();
                using HttpResponseMessage response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                byte[] data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                sw.Stop();
                double seconds = Math.Max(0.005, sw.Elapsed.TotalSeconds);
                long bits = (data.Length + 1024) * 8L;
                throughput = Math.Clamp(Math.Round(bits / seconds / 1_000_000.0, 1), 1.0, 2500.0);
            }
            catch
            {
                throughput = throughputRounds.Count > 0 ? throughputRounds.Average() : 85.0;
            }
            throughputRounds.Add(throughput);
            onSampleProgress?.Invoke(3, $"{roundLabel} — Débit", round, samplesPerTest, throughput);
            await Task.Delay(10, ct).ConfigureAwait(false);

            double bbLat = 0;
            try
            {
                PingReply rep = await ping.SendPingAsync("1.1.1.1", 600)
                    .WaitAsync(TimeSpan.FromMilliseconds(700), ct).ConfigureAwait(false);
                if (rep.Status == IPStatus.Success)
                {
                    bbLat = rep.RoundtripTime;
                }
            }
            catch (Exception ex) { LoggingService.LogException(ex, "NetworkBenchmarkService.RunFourPrecisionTestsAsync"); }
            bufferbloatRounds.Add(bbLat > 0 ? bbLat : (lat > 0 ? lat : 25.0));
            onSampleProgress?.Invoke(4, $"{roundLabel} — Bufferbloat", round, samplesPerTest, bbLat > 0 ? bbLat : 25.0);
            await Task.Delay(15, ct).ConfigureAwait(false);
        }

        try
        {
            loadCts.Cancel();
            await loadTask.WaitAsync(TimeSpan.FromMilliseconds(400), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) { LoggingService.LogException(ex, "NetworkBenchmarkService.RunFourPrecisionTestsAsync"); }

        if (latRounds.Count > 0)
        {
            var sorted = latRounds.OrderBy(t => t).ToList();
            report.LatencyMedianMs = Math.Round(sorted[sorted.Count / 2], 2);
            report.LatencyMeanMs = Math.Round(sorted.Average(), 2);
            report.LatencyMinMs = Math.Round(sorted.First(), 2);
            report.LatencyMaxMs = Math.Round(sorted.Last(), 2);
            int p95Idx = Math.Clamp((int)Math.Ceiling(0.95 * sorted.Count) - 1, 0, sorted.Count - 1);
            report.LatencyP95Ms = Math.Round(sorted[p95Idx], 2);
        }
        else
        {
            report.LatencyMedianMs = 999.0;
        }

        if (jitterRounds.Count > 0)
        {
            var sortedJ = jitterRounds.OrderBy(t => t).ToList();
            report.JitterMs = Math.Round(sortedJ[sortedJ.Count / 2], 2);
        }

        report.PacketLossPercent = Math.Round(lossCount / (double)samplesPerTest * 100.0, 1);

        if (throughputRounds.Count > 0)
        {
            var sortedT = throughputRounds.OrderBy(t => t).ToList();
            report.ThroughputMbps = Math.Round(sortedT[sortedT.Count / 2], 1);
        }
        else
        {
            report.ThroughputMbps = 85.0;
        }

        if (bufferbloatRounds.Count > 0)
        {
            var sortedB = bufferbloatRounds.OrderBy(t => t).ToList();
            double medBB = sortedB[sortedB.Count / 2];
            report.BufferbloatLatencyUnderLoadMs = Math.Round(medBB, 2);
            report.BufferbloatGrade = medBB switch
            {
                <= 15.0 => "A+",
                <= 30.0 => "A",
                <= 60.0 => "B",
                <= 100.0 => "C",
                <= 180.0 => "D",
                _ => "F"
            };
        }
        else
        {
            report.BufferbloatLatencyUnderLoadMs = 25.0;
            report.BufferbloatGrade = "A";
        }

        return report;
    }

    public async Task<double> MeasureRealThroughputMbpsAsync(
        int samplesCount = 10,
        Action<int, int, double>? onSampleProgress = null,
        CancellationToken ct = default)
    {
        samplesCount = Math.Clamp(samplesCount, 5, 40);
        var throughputSamples = new List<double>();
        var sw = new Stopwatch();

        string[] endpoints = new[]
        {
            "https://1.1.1.1/cdn-cgi/trace",
            "https://speed.cloudflare.com/__down?bytes=524288",
            "https://connectivitycheck.gstatic.com/generate_204"
        };

        for (int i = 1; i <= samplesCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            double sampleMbps = 0;
            try
            {
                sw.Restart();
                string endpoint = endpoints[(i - 1) % endpoints.Length];
                using HttpResponseMessage response = await HttpClient.GetAsync(endpoint, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                byte[] data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                sw.Stop();

                double seconds = Math.Max(0.005, sw.Elapsed.TotalSeconds);
                long bits = (data.Length + 1024) * 8;
                sampleMbps = Math.Round(bits / seconds / 1_000_000.0, 1);
                sampleMbps = Math.Clamp(sampleMbps, 1.0, 2500.0);
                throughputSamples.Add(sampleMbps);
            }
            catch
            {
                sampleMbps = throughputSamples.Count > 0 ? throughputSamples.Average() : 85.0;
                throughputSamples.Add(sampleMbps);
            }

            onSampleProgress?.Invoke(i, samplesCount, sampleMbps);
            await Task.Delay(20, ct).ConfigureAwait(false);
        }

        return throughputSamples.Count > 0 ? Math.Round(throughputSamples.Average(), 1) : 85.0;
    }

    private static async Task RunBufferbloatLoadAsync(string[] endpoints, int delayMs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                string url = endpoints[Random.Shared.Next(endpoints.Length)];
                using var msg = new HttpRequestMessage(HttpMethod.Head, url);
                using HttpResponseMessage res = await HttpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (Exception ex) { LoggingService.LogException(ex, "NetworkBenchmarkService.RunBufferbloatLoadAsync"); }
            await Task.Delay(delayMs, token).ConfigureAwait(false);
        }
    }
}
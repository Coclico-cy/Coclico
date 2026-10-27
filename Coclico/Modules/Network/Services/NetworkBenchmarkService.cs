using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using Coclico.Models.Network;

namespace Coclico.Services.Network;

public sealed class NetworkBenchmarkService : INetworkBenchmarkService
{
    private const string DownloadEndpoint = "https://speed.cloudflare.com/__down?bytes=";
    private const string UploadEndpoint = "https://speed.cloudflare.com/__up";
    private const int DownloadBytes = 100_000_000;
    private const int UploadBytes = 25_000_000;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

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
            catch (Exception ex) when (ex is not System.OutOfMemoryException)
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

    public async Task<NetworkTestReport> RunSpeedTestAsync(
        int downloadRuns = 4,
        int uploadRuns = 2,
        Action<string, int>? onRunProgress = null,
        CancellationToken ct = default)
    {
        downloadRuns = Math.Clamp(downloadRuns, 1, 10);
        uploadRuns = Math.Clamp(uploadRuns, 1, 10);

        var report = new NetworkTestReport
        {
            DownloadRuns = downloadRuns,
            UploadRuns = uploadRuns
        };

        var downloadSamples = new List<double>();
        var uploadSamples = new List<double>();

        for (int run = 1; run <= downloadRuns; run++)
        {
            ct.ThrowIfCancellationRequested();
            onRunProgress?.Invoke("down", run);
            double? mbps = await MeasureDownloadMbpsAsync(ct).ConfigureAwait(false);
            if (mbps is > 0)
            {
                downloadSamples.Add(mbps.Value);
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }

        for (int run = 1; run <= uploadRuns; run++)
        {
            ct.ThrowIfCancellationRequested();
            onRunProgress?.Invoke("up", run);
            double? mbps = await MeasureUploadMbpsAsync(ct).ConfigureAwait(false);
            if (mbps is > 0)
            {
                uploadSamples.Add(mbps.Value);
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }

        if (downloadSamples.Count > 0)
        {
            report.DownloadMbps = Math.Round(downloadSamples.Average(), 1);
            report.DownloadSucceeded = true;
        }

        if (uploadSamples.Count > 0)
        {
            report.UploadMbps = Math.Round(uploadSamples.Average(), 1);
            report.UploadSucceeded = true;
        }

        return report;
    }

    private static async Task<double?> MeasureDownloadMbpsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                DownloadEndpoint + DownloadBytes, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long totalBytes = 0;
            var buffer = new byte[1 << 20];
            await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                totalBytes += read;
            }

            sw.Stop();
            double seconds = Math.Max(0.05, sw.Elapsed.TotalSeconds);
            return Math.Round(totalBytes * 8.0 / seconds / 1_000_000.0, 1);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException)
        {
            LoggingService.LogException(ex, "NetworkBenchmarkService.MeasureDownloadMbpsAsync");
            return null;
        }
    }

    private static async Task<double?> MeasureUploadMbpsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            byte[] payload = new byte[UploadBytes];
            using var content = new ByteArrayContent(payload);
            using HttpResponseMessage response = await HttpClient.PostAsync(UploadEndpoint, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            sw.Stop();
            double seconds = Math.Max(0.05, sw.Elapsed.TotalSeconds);
            return Math.Round(UploadBytes * 8.0 / seconds / 1_000_000.0, 1);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException)
        {
            LoggingService.LogException(ex, "NetworkBenchmarkService.MeasureUploadMbpsAsync");
            return null;
        }
    }
}

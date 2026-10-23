using System.Diagnostics;
using System.Security.Cryptography;

namespace Coclico.Modules.Power.Services;

public static class CpuPowerBenchmark
{
    public static double Run(TimeSpan duration, CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 1024];
        new Random(2026).NextBytes(buffer);

        long totalBytes = 0;
        int workers = Math.Max(1, Environment.ProcessorCount);
        var sw = Stopwatch.StartNew();

        try
        {
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct }, __ =>
            {
                long localBytes = 0;
                while (sw.Elapsed < duration)
                {
                    _ = SHA256.HashData(buffer);
                    localBytes += buffer.Length;
                }

                _ = Interlocked.Add(ref totalBytes, localBytes);
            });
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
        }

        double seconds = sw.Elapsed.TotalSeconds;
        return seconds <= 0 ? 0 : totalBytes / (1024.0 * 1024.0) / seconds;
    }
}

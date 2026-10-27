using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Coclico.Installer.Services;

public static class CudaRuntimeDownloader
{
    private const string NugetFlatContainer = "https://api.nuget.org/v3-flatcontainer/";

    private const long ExpectedCublasLtBytes = 674667520;
    private const string ExpectedCublasLtSha256 = "B199D1FF892A81B7FD3D57BA1781549609B41500B36008FEF326038393AD46C7";

    private static readonly (string PackageId, string Label)[] NvidiaRuntimePackages =
    [
        ("ntvlibs.cuda12.cudart64_12.runtime.win-x64", "cudart64_12.dll"),
        ("ntvlibs.cuda12.cublas64_12.runtime.win-x64", "cublas64_12.dll"),
        ("ntvlibs.cuda12.cufft64_11.runtime.win-x64", "cufft64_11.dll"),
        ("ntvlibs.cuda12.nvrtc64_120_0.runtime.win-x64", "nvrtc64_120_0.dll"),
        ("ntvlibs.cuda12.nvrtc64_120_0.alt.runtime.win-x64", "nvrtc64_120_0.alt.dll")
    ];

    private static readonly byte[] DownloadBuffer = new byte[1_048_576];
    private static readonly byte[] CopyBuffer = new byte[81_920];

    private const int MaxSegments = 8;
    private const long SegmentThreshold = 8L * 1024 * 1024;
    private const int SegmentMaxRetries = 3;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Coclico-Installer/2.1");
        return client;
    }

    public static async Task DownloadAsync(string installPath, IProgress<InstallProgressReport> progress, CancellationToken ct = default)
    {
        EnsureSufficientDiskSpace(installPath);

        string cudaDir = Path.Join(installPath, "runtimes", "win-x64", "native", "cuda12");
        _ = Directory.CreateDirectory(cudaDir);

        await DownloadAndExtractFolderAsync(
                "llamasharp.backend.cuda12.windows", "0.26.0",
                "LLamaSharpRuntimes/win-x64/native/cuda12/",
                cudaDir,
                "Moteur CUDA (ggml-cuda.dll)", progress, ct)
            .ConfigureAwait(false);

        foreach ((string packageId, string label) in NvidiaRuntimePackages)
        {
            await DownloadAndExtractFolderAsync(
                    packageId, "12.8.1",
                    "runtimes/win-x64/native/",
                    installPath, label, progress, ct)
                .ConfigureAwait(false);
        }

        await DownloadAndJoinCublasLtAsync(installPath, progress, ct).ConfigureAwait(false);
    }

    private static void EnsureSufficientDiskSpace(string installPath)
    {
        try
        {
            string? driveRoot = Path.GetPathRoot(Path.GetFullPath(installPath));
            if (string.IsNullOrEmpty(driveRoot))
            {
                return;
            }

            var drive = new DriveInfo(driveRoot);
            long requiredBytes = 1536L * 1024 * 1024;
            if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
            {
                throw new IOException(
                    $"Espace disque insuffisant sur {driveRoot} : {(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)):F1} Go disponibles, au moins 1,5 Go requis pour le runtime CUDA.");
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
    }

    private static async Task DownloadAndExtractFolderAsync(
        string packageId, string version, string entryFolder,
        string targetDir, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Join(Path.GetTempPath(), $"coclico-{packageId}.nupkg");
        string extractDir = Path.Join(Path.GetTempPath(), $"coclico-extract-{packageId}");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, progress, ct).ConfigureAwait(false);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            using (var archive = ZipFile.OpenRead(tempNupkg))
            {
                archive.ExtractToDirectory(extractDir, overwriteFiles: true);
            }

            foreach (string file in Directory.EnumerateFiles(extractDir, "*.dll", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(extractDir, file).Replace('\\', '/');
                if (!relative.StartsWith(entryFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, Path.Join(targetDir, Path.GetFileName(file)), overwrite: true);
            }
        }
        finally
        {
            try
            {
                File.Delete(tempNupkg);
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, recursive: true);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task DownloadAndJoinCublasLtAsync(
        string installPath, IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string tempFragment0 = Path.Join(Path.GetTempPath(), "coclico-cublaslt.f00");
        string tempFragment1 = Path.Join(Path.GetTempPath(), "coclico-cublaslt.f01");
        string targetFile = Path.Join(installPath, "cublasLt64_12.dll");

        try
        {
            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f00", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f00",
                    tempFragment0, "cublasLt64_12.dll (fragment 1/2)", progress, ct)
                .ConfigureAwait(false);

            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f01", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f01",
                    tempFragment1, "cublasLt64_12.dll (fragment 2/2)", progress, ct)
                .ConfigureAwait(false);

            progress.Report(new InstallProgressReport(95, "Assemblage de cublasLt64_12.dll...", ""));
            using (var output = File.Create(targetFile))
            {
                await AppendFileAsync(tempFragment0, output, ct).ConfigureAwait(false);
                await AppendFileAsync(tempFragment1, output, ct).ConfigureAwait(false);
            }

            var info = new FileInfo(targetFile);
            if (info.Length != ExpectedCublasLtBytes)
            {
                throw new InvalidDataException(
                    $"cublasLt64_12.dll assemblé invalide : {info.Length} octets au lieu de {ExpectedCublasLtBytes}.");
            }

            string hash = await ComputeSha256Async(targetFile, ct).ConfigureAwait(false);
            if (!string.Equals(hash, ExpectedCublasLtSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("cublasLt64_12.dll assemblé invalide : empreinte SHA-256 incorrecte.");
            }
        }
        finally
        {
            try { File.Delete(tempFragment0); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
            try { File.Delete(tempFragment1); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task DownloadFragmentAsync(
        string packageId, string version, string entryPath,
        string destinationFile, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Join(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, progress, ct).ConfigureAwait(false);

            using var archive = ZipFile.OpenRead(tempNupkg);
            ZipArchiveEntry? entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new InvalidDataException($"Fragment introuvable dans le package {packageId}.");
            }

            using (Stream source = entry.Open())
            using (FileStream target = File.Create(destinationFile))
            {
                source.CopyTo(target);
            }
        }
        finally
        {
            try { File.Delete(tempNupkg); } catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { _ = ex; }
        }
    }

    private static async Task DownloadFileAsync(
        string url, string targetFile, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        progress.Report(new InstallProgressReport(0, $"Téléchargement : {label}", "Connexion au serveur..."));

        long totalBytes;
        bool supportsRanges;

        using (var probe = new HttpRequestMessage(HttpMethod.Get, url))
        {
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            using var probeResponse = await Http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            probeResponse.EnsureSuccessStatusCode();

            supportsRanges = probeResponse.StatusCode == HttpStatusCode.PartialContent;
            ContentRangeHeaderValue? contentRange = probeResponse.Content.Headers.ContentRange;
            totalBytes = contentRange?.Length ?? probeResponse.Content.Headers.ContentLength ?? -1;
        }

        if (supportsRanges && totalBytes >= SegmentThreshold)
        {
            await DownloadSegmentedAsync(url, targetFile, totalBytes, label, progress, ct).ConfigureAwait(false);
            return;
        }
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? expectedBytes = totalBytes > 0 ? totalBytes : response.Content.Headers.ContentLength;

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 1_048_576, useAsync: true);

        byte[] buffer = DownloadBuffer;
        long totalRead = 0;
        int read;
        int lastReport = -1;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;

            double pct = expectedBytes > 0 ? totalRead * 100.0 / expectedBytes.Value : 0;
            int pctNow = (int)pct;
            if (pctNow != lastReport)
            {
                lastReport = pctNow;
                progress.Report(new InstallProgressReport(
                    pct,
                    $"Téléchargement : {label} ({totalRead / 1048576} Mo / {(expectedBytes ?? 0) / 1048576} Mo)",
                    $"{pctNow}%"));
            }
        }
    }

    private static async Task DownloadSegmentedAsync(
        string url, string targetFile, long totalBytes, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        int segmentCount = (int)Math.Clamp(totalBytes / SegmentThreshold, 2, MaxSegments);
        using (var preallocate = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
        {
            preallocate.SetLength(totalBytes);
        }

        using SafeFileHandle handle = File.OpenHandle(
            targetFile, FileMode.Open, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);

        var counter = new SegmentProgress(totalBytes, label, progress);

        long baseSize = totalBytes / segmentCount;
        long remainder = totalBytes % segmentCount;

        var tasks = new Task[segmentCount];
        long from = 0;
        for (int i = 0; i < segmentCount; i++)
        {
            long start = from;
            long length = baseSize + (i < remainder ? 1 : 0);
            long end = start + length - 1;
            from += length;
            tasks[i] = DownloadSegmentAsync(url, handle, start, end, counter, ct);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var info = new FileInfo(targetFile);
        if (info.Length != totalBytes)
        {
            throw new InvalidDataException(
                $"Téléchargement incomplet : {info.Length} octets reçus au lieu de {totalBytes}.");
        }
    }

    private static async Task DownloadSegmentAsync(
        string url, SafeFileHandle handle, long from, long to,
        SegmentProgress counter, CancellationToken ct)
    {
        long position = from;
        byte[] buffer = new byte[1_048_576];

        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (position > to)
            {
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(position, to);

                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new InvalidDataException("Le serveur n'a pas honoré la requête de reprise.");
                }

                using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, ct).ConfigureAwait(false);
                    position += read;
                    counter.Add(read);
                }

                if (position > to)
                {
                    return;
                }

                throw new EndOfStreamException("Connexion interrompue avant la fin du segment.");
            }
            catch (Exception ex) when (attempt < SegmentMaxRetries && !ct.IsCancellationRequested &&
                                       ex is HttpRequestException or EndOfStreamException or InvalidDataException or IOException or SocketException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false);
            }
        }
    }

    private sealed class SegmentProgress
    {
        private readonly long _totalBytes;
        private readonly string _label;
        private readonly IProgress<InstallProgressReport> _progress;
        private long _totalRead;
        private int _lastReport = -1;

        public SegmentProgress(long totalBytes, string label, IProgress<InstallProgressReport> progress)
        {
            _totalBytes = totalBytes;
            _label = label;
            _progress = progress;
        }

        public void Add(long bytes)
        {
            long totalRead = Interlocked.Add(ref _totalRead, bytes);
            int pctNow = (int)(totalRead * 100 / _totalBytes);
            if (pctNow != _lastReport)
            {
                _lastReport = pctNow;
                _progress.Report(new InstallProgressReport(
                    pctNow,
                    $"Téléchargement : {_label} ({totalRead / 1048576} Mo / {_totalBytes / 1048576} Mo)",
                    $"{pctNow}%"));
            }
        }
    }

    private static async Task AppendFileAsync(string fragmentFile, FileStream output, CancellationToken ct)
    {
        using var input = File.OpenRead(fragmentFile);
        byte[] buffer = CopyBuffer;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}

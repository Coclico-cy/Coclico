using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

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

    public static async Task DownloadAsync(string installPath, IProgress<InstallProgressReport> progress, CancellationToken ct = default)
    {
        EnsureSufficientDiskSpace(installPath);

        string cudaDir = Path.Combine(installPath, "runtimes", "win-x64", "native", "cuda12");
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
        catch
        {
        }
    }

    private static async Task DownloadAndExtractFolderAsync(
        string packageId, string version, string entryFolder,
        string targetDir, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Combine(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, progress, ct).ConfigureAwait(false);

            using var archive = ZipFile.OpenRead(tempNupkg);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(entryFolder, StringComparison.OrdinalIgnoreCase) ||
                    !entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = entry.FullName[entryFolder.Length..];
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string destination = Path.Combine(targetDir, fileName.Replace('/', '\\'));
                _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        finally
        {
            try { File.Delete(tempNupkg); } catch { }
        }
    }

    private static async Task DownloadAndJoinCublasLtAsync(
        string installPath, IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string tempFragment0 = Path.Combine(Path.GetTempPath(), "coclico-cublaslt.f00");
        string tempFragment1 = Path.Combine(Path.GetTempPath(), "coclico-cublaslt.f01");
        string targetFile = Path.Combine(installPath, "cublasLt64_12.dll");

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
            try { File.Delete(tempFragment0); } catch { }
            try { File.Delete(tempFragment1); } catch { }
        }
    }

    private static async Task DownloadFragmentAsync(
        string packageId, string version, string entryPath,
        string destinationFile, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Combine(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, progress, ct).ConfigureAwait(false);

            using var archive = ZipFile.OpenRead(tempNupkg);
            ZipArchiveEntry? entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new InvalidDataException($"Fragment introuvable dans le package {packageId}.");
            }

            entry.ExtractToFile(destinationFile, overwrite: true);
        }
        finally
        {
            try { File.Delete(tempNupkg); } catch { }
        }
    }

    private static async Task DownloadFileAsync(
        string url, string targetFile, string label,
        IProgress<InstallProgressReport> progress, CancellationToken ct)
    {
        using var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        client.Timeout = TimeSpan.FromHours(2);

        progress.Report(new InstallProgressReport(0, $"Téléchargement : {label}", "Connexion au serveur..."));

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
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

            double pct = totalBytes > 0 ? totalRead * 100.0 / totalBytes.Value : 0;
            int pctNow = (int)pct;
            if (pctNow != lastReport)
            {
                lastReport = pctNow;
                progress.Report(new InstallProgressReport(
                    pct,
                    $"Téléchargement : {label} ({totalRead / 1048576} Mo / {(totalBytes ?? 0) / 1048576} Mo)",
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

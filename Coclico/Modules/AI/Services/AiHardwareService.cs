using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Coclico.Services.AI;

public enum AiGpuVendor
{
    Unknown,
    None,
    Nvidia,
    Amd,
    Intel
}

public sealed record AiCudaProgress(int Percent, string Message);

public static class AiHardwareService
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string NugetFlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    private const long ExpectedCublasLtBytes = 674667520;
    private const string ExpectedCublasLtSha256 = "B199D1FF892A81B7FD3D57BA1781549609B41500B36008FEF326038393AD46C7";
    private const long RequiredDiskBytes = 1536L * 1024 * 1024;
    private const int TotalRuntimeSteps = 8;

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

    private static AiGpuVendor? _cachedVendor;
    private static string? _cachedGpuName;

    public static AiGpuVendor DetectVendor()
    {
        if (_cachedVendor.HasValue)
        {
            return _cachedVendor.Value;
        }

        (AiGpuVendor Vendor, string Name) found = ScanPciDevices(displayOnly: true);
        if (found.Vendor == AiGpuVendor.None)
        {
            (AiGpuVendor Vendor, string Name) loose = ScanPciDevices(displayOnly: false);
            if (loose.Vendor != AiGpuVendor.None)
            {
                found = loose;
            }
        }

        _cachedVendor = found.Vendor;
        _cachedGpuName = found.Name;
        return found.Vendor;
    }

    public static string GetGpuDisplayName()
    {
        _ = DetectVendor();
        return string.IsNullOrWhiteSpace(_cachedGpuName) ? "carte graphique" : _cachedGpuName;
    }

    public static bool IsCudaRuntimeAvailable()
    {
        string? cudaDir = ResolveCudaDirectory();
        if (cudaDir == null)
        {
            return false;
        }

        foreach (string dll in new[] { "llama.dll", "ggml.dll", "ggml-base.dll", "ggml-cuda.dll" })
        {
            if (!File.Exists(Path.Combine(cudaDir, dll)))
            {
                return false;
            }
        }

        foreach (string dll in new[] { "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "nvrtc64_120_0.dll" })
        {
            if (ResolveDllPath(dll) == null)
            {
                return false;
            }
        }

        return true;
    }

    public static string ResolveNativeDirectory()
    {
        List<string> candidates = BuildCandidateDirectories();
        return candidates.FirstOrDefault(d => Directory.Exists(Path.Combine(d, "cuda12")))
            ?? candidates.FirstOrDefault(Directory.Exists)
            ?? candidates[0];
    }

    public static string? ResolveCudaDirectory()
    {
        string cudaDir = Path.Combine(ResolveNativeDirectory(), "cuda12");
        return Directory.Exists(cudaDir) ? cudaDir : null;
    }

    public static string? ResolveDllPath(string fileName)
    {
        foreach (string candidate in BuildCandidateDirectories())
        {
            string path = Path.Combine(candidate, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static async Task DownloadCudaRuntimeAsync(IProgress<AiCudaProgress> progress, CancellationToken ct = default)
    {
        string installPath = AppContext.BaseDirectory;
        try
        {
            EnsureSufficientDiskSpace(installPath);

            string cudaDir = Path.Combine(installPath, "runtimes", "win-x64", "native", "cuda12");
            _ = Directory.CreateDirectory(cudaDir);

            await DownloadAndExtractFolderAsync(
                    "llamasharp.backend.cuda12.windows", "0.26.0",
                    "LLamaSharpRuntimes/win-x64/native/cuda12/",
                    cudaDir,
                    "Moteur CUDA (ggml-cuda.dll)", 0, progress, ct)
                .ConfigureAwait(false);

            int step = 1;
            foreach ((string packageId, string label) in NvidiaRuntimePackages)
            {
                await DownloadAndExtractFolderAsync(
                        packageId, "12.8.1",
                        "runtimes/win-x64/native/",
                        installPath, label, step, progress, ct)
                    .ConfigureAwait(false);
                step++;
            }

            await DownloadAndJoinCublasLtAsync(installPath, progress, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Droits insuffisants pour installer le runtime CUDA dans le dossier de Coclico. Relancez l'installeur Coclico en administrateur pour installer le runtime CUDA.", ex);
        }
    }

    private static List<string> BuildCandidateDirectories()
    {
        string baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "runtimes", "win-x64", "native"),
            Path.Combine(baseDir, "native"),
            baseDir
        };

        try
        {
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir?.Parent != null; i++)
            {
                dir = dir.Parent;
                candidates.Add(Path.Combine(dir.FullName, "Coclico", "bin", "Debug", "net10.0-windows10.0.22621.0", "runtimes", "win-x64", "native"));
                candidates.Add(Path.Combine(dir.FullName, "Coclico", "bin", "Release", "net10.0-windows10.0.22621.0", "runtimes", "win-x64", "native"));
                candidates.Add(Path.Combine(dir.FullName, "runtimes", "win-x64", "native"));
            }
        }
        catch
        {
        }

        return candidates;
    }

    private static (AiGpuVendor Vendor, string Name) ScanPciDevices(bool displayOnly)
    {
        AiGpuVendor bestVendor = AiGpuVendor.None;
        string bestName = string.Empty;

        try
        {
            using RegistryKey? pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pciKey == null)
            {
                return (bestVendor, bestName);
            }

            foreach (string deviceKey in pciKey.GetSubKeyNames())
            {
                AiGpuVendor vendor = VendorFromId(deviceKey);
                if (vendor == AiGpuVendor.None)
                {
                    continue;
                }

                if (bestVendor != AiGpuVendor.None && VendorPriority(vendor) < VendorPriority(bestVendor))
                {
                    continue;
                }

                using RegistryKey? device = pciKey.OpenSubKey(deviceKey);
                if (device == null)
                {
                    continue;
                }

                foreach (string instanceKey in device.GetSubKeyNames())
                {
                    using RegistryKey? instance = device.OpenSubKey(instanceKey);
                    if (instance == null)
                    {
                        continue;
                    }

                    if (displayOnly && !IsDisplayAdapter(instance))
                    {
                        continue;
                    }

                    if (VendorPriority(vendor) >= VendorPriority(bestVendor))
                    {
                        bestVendor = vendor;
                        bestName = CleanDeviceDesc(instance.GetValue("DeviceDesc")?.ToString(), vendor);
                    }
                }
            }
        }
        catch
        {
        }

        return (bestVendor, bestName);
    }

    private static bool IsDisplayAdapter(RegistryKey instance)
    {
        try
        {
            using RegistryKey? control = instance.OpenSubKey("Control");
            object? classGuid = control?.GetValue("ClassGUID");
            return classGuid != null && string.Equals(classGuid.ToString(), DisplayClassGuid, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static AiGpuVendor VendorFromId(string deviceKeyId)
    {
        if (deviceKeyId.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Nvidia;
        }
        if (deviceKeyId.StartsWith("VEN_1002", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Amd;
        }
        if (deviceKeyId.StartsWith("VEN_8086", StringComparison.OrdinalIgnoreCase))
        {
            return AiGpuVendor.Intel;
        }
        return AiGpuVendor.None;
    }

    private static int VendorPriority(AiGpuVendor vendor)
    {
        return vendor switch
        {
            AiGpuVendor.Nvidia => 3,
            AiGpuVendor.Amd => 2,
            AiGpuVendor.Intel => 1,
            _ => 0
        };
    }

    private static string CleanDeviceDesc(string? raw, AiGpuVendor vendor)
    {
        string fallback = vendor switch
        {
            AiGpuVendor.Nvidia => "GPU NVIDIA",
            AiGpuVendor.Amd => "GPU AMD",
            AiGpuVendor.Intel => "GPU Intel",
            _ => "carte graphique"
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        int lastSemi = raw.LastIndexOf(';');
        string cleaned = lastSemi >= 0 ? raw[(lastSemi + 1)..] : raw;
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned.Trim();
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
            if (drive.IsReady && drive.AvailableFreeSpace < RequiredDiskBytes)
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
        string targetDir, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Combine(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, stepIndex, progress, ct).ConfigureAwait(false);

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
        string installPath, IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string tempFragment0 = Path.Combine(Path.GetTempPath(), "coclico-cublaslt.f00");
        string tempFragment1 = Path.Combine(Path.GetTempPath(), "coclico-cublaslt.f01");
        string targetFile = Path.Combine(installPath, "cublasLt64_12.dll");

        try
        {
            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f00", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f00",
                    tempFragment0, "cublasLt64_12.dll (fragment 1/2)", 6, progress, ct)
                .ConfigureAwait(false);

            await DownloadFragmentAsync(
                    "ntvlibs.cuda12.cublaslt64_12.runtime.win-x64.f01", "12.8.1",
                    "fragments/win-x64/native/cublasLt64_12.dll.f01",
                    tempFragment1, "cublasLt64_12.dll (fragment 2/2)", 7, progress, ct)
                .ConfigureAwait(false);

            progress.Report(new AiCudaProgress(99, "Assemblage de cublasLt64_12.dll..."));
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

            progress.Report(new AiCudaProgress(100, "Runtime CUDA installé avec succès."));
        }
        finally
        {
            try { File.Delete(tempFragment0); } catch { }
            try { File.Delete(tempFragment1); } catch { }
        }
    }

    private static async Task DownloadFragmentAsync(
        string packageId, string version, string entryPath,
        string destinationFile, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        string url = $"{NugetFlatContainer}{packageId}/{version}/{packageId}.{version}.nupkg";
        string tempNupkg = Path.Combine(Path.GetTempPath(), $"coclico-{packageId}.nupkg");

        try
        {
            await DownloadFileAsync(url, tempNupkg, label, stepIndex, progress, ct).ConfigureAwait(false);

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
        string url, string targetFile, string label, int stepIndex,
        IProgress<AiCudaProgress> progress, CancellationToken ct)
    {
        using var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        client.Timeout = TimeSpan.FromHours(2);

        int stepBase = stepIndex * 100 / TotalRuntimeSteps;
        int stepSpan = 100 / TotalRuntimeSteps;
        progress.Report(new AiCudaProgress(stepBase, $"Téléchargement : {label} — connexion au serveur..."));

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

            double filePct = totalBytes > 0 ? totalRead * 100.0 / totalBytes.Value : 0;
            int pctNow = stepBase + (int)(filePct * stepSpan / 100.0);
            if (pctNow != lastReport)
            {
                lastReport = pctNow;
                progress.Report(new AiCudaProgress(
                    pctNow,
                    $"Téléchargement : {label} ({totalRead / 1048576} Mo / {(totalBytes ?? 0) / 1048576} Mo)"));
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
using System;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Coclico.Installer.Services;

public enum InstallerGpuVendor
{
    Unknown,
    None,
    Nvidia,
    Amd,
    Intel
}

public static class GpuDetector
{
    public record GpuInfo(bool HasNvidia, string Name, InstallerGpuVendor Vendor = InstallerGpuVendor.Unknown)
    {
        public bool HasAmd => Vendor == InstallerGpuVendor.Amd;

        public bool HasIntel => Vendor == InstallerGpuVendor.Intel;

        public string RuntimeMessage => Vendor switch
        {
            InstallerGpuVendor.Nvidia => "GPU NVIDIA détecté : l'accélération IA (CUDA 12) sera installée.",
            InstallerGpuVendor.Amd => "GPU AMD détecté : aucun runtime IA GPU n'existe sur Windows (ROCm est réservé à Linux). Coclico utilisera le mode CPU (AVX2).",
            InstallerGpuVendor.Intel => "GPU Intel détecté : Coclico utilisera le mode CPU (AVX2).",
            InstallerGpuVendor.None => "Aucun GPU dédié détecté : Coclico utilisera le mode CPU (AVX2).",
            _ => string.Empty
        };
    }

    private const string DisplayClassGuid = "{4D36E968-E325-11CE-BFC1-08002BE10318}";

    private static readonly string[] NvidiaMarkers = ["NVIDIA", "GeForce", "RTX", "GTX"];
    private static readonly string[] AmdMarkers = ["AMD", "Radeon", "ATI"];
    private static readonly string[] IntelMarkers = ["Intel", "Arc", "Iris", "UHD"];

    public static GpuInfo DetectGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
            InstallerGpuVendor fallbackVendor = InstallerGpuVendor.Unknown;
            string fallbackName = string.Empty;

            foreach (var obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                string compatibility = obj["AdapterCompatibility"]?.ToString() ?? string.Empty;
                InstallerGpuVendor vendor = Classify($"{compatibility} {name}");
                if (vendor == InstallerGpuVendor.Nvidia)
                {
                    return new GpuInfo(true, name, vendor);
                }
                if (vendor != InstallerGpuVendor.Unknown && fallbackVendor == InstallerGpuVendor.Unknown)
                {
                    fallbackVendor = vendor;
                    fallbackName = name;
                }
            }

            if (fallbackVendor != InstallerGpuVendor.Unknown)
            {
                return new GpuInfo(false, fallbackName, fallbackVendor);
            }
        }
        catch
        {
        }

        return DetectGpuFromRegistry();
    }

    private static GpuInfo DetectGpuFromRegistry()
    {
        try
        {
            using RegistryKey? pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pciKey == null)
            {
                return new GpuInfo(false, "Carte graphique standard", InstallerGpuVendor.None);
            }

            InstallerGpuVendor best = InstallerGpuVendor.None;
            foreach (string subKeyName in pciKey.GetSubKeyNames())
            {
                InstallerGpuVendor vendor = subKeyName switch
                {
                    _ when subKeyName.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase) => InstallerGpuVendor.Nvidia,
                    _ when subKeyName.StartsWith("VEN_1002", StringComparison.OrdinalIgnoreCase) => InstallerGpuVendor.Amd,
                    _ when subKeyName.StartsWith("VEN_8086", StringComparison.OrdinalIgnoreCase) => InstallerGpuVendor.Intel,
                    _ => InstallerGpuVendor.None
                };

                if (vendor == InstallerGpuVendor.None ||
                    (best != InstallerGpuVendor.None && best != InstallerGpuVendor.Intel) ||
                    !IsDisplayAdapter(pciKey.OpenSubKey(subKeyName)))
                {
                    continue;
                }

                best = vendor;
            }

            return best switch
            {
                InstallerGpuVendor.Nvidia => new GpuInfo(true, "GPU NVIDIA Compatible", best),
                InstallerGpuVendor.Amd => new GpuInfo(false, "GPU AMD Compatible", best),
                InstallerGpuVendor.Intel => new GpuInfo(false, "GPU Intel Compatible", best),
                _ => new GpuInfo(false, "Carte graphique standard", InstallerGpuVendor.None)
            };
        }
        catch
        {
            return new GpuInfo(false, "Carte graphique standard", InstallerGpuVendor.None);
        }
    }

    private static bool IsDisplayAdapter(RegistryKey? deviceKey)
    {
        if (deviceKey == null)
        {
            return false;
        }

        using (deviceKey)
        {
            foreach (string instanceName in deviceKey.GetSubKeyNames())
            {
                using RegistryKey? instanceKey = deviceKey.OpenSubKey(instanceName);
                if (instanceKey?.GetValue("ClassGUID") is string classGuid &&
                    string.Equals(classGuid, DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static InstallerGpuVendor Classify(string text)
    {
        if (NvidiaMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return InstallerGpuVendor.Nvidia;
        }

        if (AmdMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return InstallerGpuVendor.Amd;
        }

        if (IntelMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return InstallerGpuVendor.Intel;
        }

        return InstallerGpuVendor.Unknown;
    }
}
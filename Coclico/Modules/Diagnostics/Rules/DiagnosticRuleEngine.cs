namespace Coclico.Modules.Diagnostics.Rules;

using Coclico.Modules.Diagnostics.Models;

public static class DiagnosticRuleEngine
{
    public const string RulesVersion = "1.0";

    public const long LowFreeBytesAbsolute = 5 * 1024L * 1024 * 1024;
    public const long CriticalFreeBytesAbsolute = 2 * 1024L * 1024 * 1024;
    public const double LowFreePercent = 5.0;
    public const double CriticalFreePercent = 2.0;

    public const long MinAvailableMemoryBytes = 400L * 1024 * 1024;
    public const double CommitCriticalPercent = 95.0;

    public const double CpuHighLoadPercent = 85.0;
    public const double CpuTemperatureWarningC = 85.0;
    public const double CpuTemperatureErrorC = 95.0;

    public const double PacketLossWarningPercent = 25.0;
    public const double PacketLossErrorPercent = 60.0;
    public const double DnsSlowMs = 1500.0;
    public const double PingHighMedianMs = 150.0;

    public const int UnexpectedShutdownWarning7d = 1;
    public const int CriticalEventGroupWarning7d = 10;

    public const double BatteryWearWarningRatio = 0.5;

    public static IReadOnlyList<DiagnosticFinding> EvaluateStorage(StorageFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        foreach (VolumeFacts volume in facts.Volumes)
        {
            if (volume.TotalBytes <= 0)
            {
                continue;
            }

            double freePercent = volume.FreeBytes * 100.0 / volume.TotalBytes;
            DiagnosticSeverity severity =
                (volume.FreeBytes < CriticalFreeBytesAbsolute || freePercent < CriticalFreePercent)
                    ? DiagnosticSeverity.Error
                : (volume.FreeBytes < LowFreeBytesAbsolute || freePercent < LowFreePercent)
                    ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Success;

            if (severity != DiagnosticSeverity.Success)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "STORAGE_LOW_SPACE_001",
                    Entity = volume.VolumeName,
                    Category = DiagnosticCategory.Storage,
                    Severity = severity,
                    Confidence = DiagnosticConfidence.High,
                    TitleKey = "Diag_Finding_LowSpace_Title",
                    SummaryKey = "Diag_Finding_LowSpace_Summary",
                    ExplanationKey = "Diag_Finding_LowSpace_Why",
                    ObservedAtUtc = DateTime.UtcNow,
                    Evidence =
                    [
                        DiagnosticEvidence.Measured("storage.volume.freeBytes", "Diag_Evidence_FreeSpace", volume.FreeBytes, "bytes", "DriveInfo", volume.VolumeName),
                        DiagnosticEvidence.Measured("storage.volume.freePercent", "Diag_Evidence_FreePercent", Math.Round(freePercent, 1), "%", "computed", volume.VolumeName),
                        DiagnosticEvidence.Measured("storage.volume.totalBytes", "Diag_Evidence_TotalSize", volume.TotalBytes, "bytes", "DriveInfo", volume.VolumeName)
                    ],
                    RecommendedNextStepKeys = ["Diag_Next_FreeUpSpace"],
                    ActionIds = [DiagnosticActionCatalog.OpenDiskAnalyzer],
                    IsActionable = true
                });
            }
        }

        foreach (DiskReliabilityFacts disk in facts.Disks)
        {
            if ((disk.ReadErrorsUncorrected ?? 0) > 0 || (disk.WriteErrorsUncorrected ?? 0) > 0)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "STORAGE_RELIABILITY_ERROR_001",
                    Entity = disk.DiskName,
                    Category = DiagnosticCategory.Storage,
                    Severity = DiagnosticSeverity.Error,
                    Confidence = DiagnosticConfidence.High,
                    TitleKey = "Diag_Finding_DiskErrors_Title",
                    SummaryKey = "Diag_Finding_DiskErrors_Summary",
                    ExplanationKey = "Diag_Finding_DiskErrors_Why",
                    ObservedAtUtc = DateTime.UtcNow,
                    Evidence =
                    [
                        DiagnosticEvidence.Measured("storage.disk.readErrorsUncorrected", "Diag_Evidence_ReadErrors", disk.ReadErrorsUncorrected, "", "MSFT_StorageReliabilityCounter", disk.DiskName),
                        DiagnosticEvidence.Measured("storage.disk.writeErrorsUncorrected", "Diag_Evidence_WriteErrors", disk.WriteErrorsUncorrected, "", "MSFT_StorageReliabilityCounter", disk.DiskName)
                    ],
                    RecommendedNextStepKeys = ["Diag_Next_BackupAndCheckDisk"]
                });
            }

            if (disk.WearPercent is > 80)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "STORAGE_WEAR_001",
                    Entity = disk.DiskName,
                    Category = DiagnosticCategory.Storage,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = DiagnosticConfidence.Medium,
                    TitleKey = "Diag_Finding_DiskWear_Title",
                    SummaryKey = "Diag_Finding_DiskWear_Summary",
                    ObservedAtUtc = DateTime.UtcNow,
                    Evidence = [DiagnosticEvidence.Measured("storage.disk.wearPercent", "Diag_Evidence_Wear", disk.WearPercent, "%", "MSFT_StorageReliabilityCounter", disk.DiskName)],
                    RecommendedNextStepKeys = ["Diag_Next_BackupAndCheckDisk"]
                });
            }
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluateMemory(MemoryFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (facts.AvailablePhysicalBytes >= 0
            && facts.AvailablePhysicalBytes < MinAvailableMemoryBytes)
        {
            double commitPercent = facts.CommitLimitBytes > 0
                ? facts.CommitTotalBytes * 100.0 / facts.CommitLimitBytes
                : 0;

            findings.Add(new DiagnosticFinding
            {
                Id = "MEMORY_PRESSURE_001",
                Category = DiagnosticCategory.Memory,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_MemoryPressure_Title",
                SummaryKey = "Diag_Finding_MemoryPressure_Summary",
                ExplanationKey = "Diag_Finding_MemoryPressure_Why",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                [
                    DiagnosticEvidence.Measured("memory.availablePhysical", "Diag_Evidence_AvailableRam", facts.AvailablePhysicalBytes, "bytes", "GlobalMemoryStatusEx"),
                    DiagnosticEvidence.Measured("memory.commitPercent", "Diag_Evidence_Commit", Math.Round(commitPercent, 1), "%", "GlobalMemoryStatusEx")
                ],
                PossibleCauseKeys = ["Diag_Cause_MemoryHeavyApp", "Diag_Cause_ManyApps"],
                RecommendedNextStepKeys = ["Diag_Next_InspectTopProcesses"],
                ActionIds = [DiagnosticActionCatalog.OpenRamCleaner],
                IsActionable = true,
                IsFactBacked = false
            });
        }

        if (facts.TopProcessName is not null && facts.TopProcessWorkingSetBytes is > 0 && facts.TotalPhysicalBytes > 0)
        {
            double percent = facts.TopProcessWorkingSetBytes.Value * 100.0 / facts.TotalPhysicalBytes;
            if (percent >= 25)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "MEMORY_TOP_CONSUMER_001",
                    Entity = facts.TopProcessName,
                    Category = DiagnosticCategory.Memory,
                    Severity = DiagnosticSeverity.Info,
                    Confidence = DiagnosticConfidence.Medium,
                    TitleKey = "Diag_Finding_TopMemoryProcess_Title",
                    SummaryKey = "Diag_Finding_TopMemoryProcess_Summary",
                    ObservedAtUtc = DateTime.UtcNow,
                    Evidence = [DiagnosticEvidence.Measured("memory.topProcess.workingSet", "Diag_Evidence_TopProcess", facts.TopProcessName, "bytes", "Process", facts.TopProcessName)],
                    RecommendedNextStepKeys = ["Diag_Next_WatchTrend"],
                    IsActionable = false,
                    IsFactBacked = false
                });
            }
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluateCpu(CpuFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (facts.LoadPercentMax >= CpuHighLoadPercent)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "CPU_HIGH_LOAD_001",
                Category = DiagnosticCategory.Cpu,
                Severity = DiagnosticSeverity.Info,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_CpuLoad_Title",
                SummaryKey = "Diag_Finding_CpuLoad_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                [
                    DiagnosticEvidence.Measured("cpu.loadMin", "Diag_Evidence_CpuLoadMin", Math.Round(facts.LoadPercentMin, 1), "%", "GetSystemTimes"),
                    DiagnosticEvidence.Measured("cpu.loadMax", "Diag_Evidence_CpuLoadMax", Math.Round(facts.LoadPercentMax, 1), "%", "GetSystemTimes")
                ],
                PossibleCauseKeys = ["Diag_Cause_HeavyTask", "Diag_Cause_BackgroundTask"],
                RecommendedNextStepKeys = ["Diag_Next_InspectTopProcesses"],
                IsFactBacked = false
            });
        }

        if (facts.TemperatureAvailable && facts.TemperatureC is double temp)
        {
            if (temp >= CpuTemperatureErrorC)
            {
                findings.Add(CreateThermalFinding(temp, DiagnosticSeverity.Error, DiagnosticConfidence.Medium));
            }
            else if (temp >= CpuTemperatureWarningC)
            {
                findings.Add(CreateThermalFinding(temp, DiagnosticSeverity.Warning, DiagnosticConfidence.Low));
            }
        }

        return findings;
    }

    private static DiagnosticFinding CreateThermalFinding(double temperatureC, DiagnosticSeverity severity, DiagnosticConfidence confidence)
        => new()
        {
            Id = "CPU_THERMAL_001",
            Category = DiagnosticCategory.Cpu,
            Severity = severity,
            Confidence = confidence,
            TitleKey = "Diag_Finding_CpuTemp_Title",
            SummaryKey = "Diag_Finding_CpuTemp_Summary",
            ExplanationKey = "Diag_Finding_CpuTemp_Why",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence = [DiagnosticEvidence.Measured("cpu.temperature", "Diag_Evidence_CpuTemp", Math.Round(temperatureC, 1), "°C", "MSAcpi_ThermalZoneTemperature")],
            PossibleCauseKeys = ["Diag_Cause_Dust", "Diag_Cause_FanCurve", "Diag_Cause_HeavyTask"],
            RecommendedNextStepKeys = ["Diag_Next_CheckCooling"],
            IsFactBacked = false
        };

    public static IReadOnlyList<DiagnosticFinding> EvaluateNetwork(NetworkFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (facts.AnyAdapterUp
            && facts.Gateway.GatewayAddress is not null
            && facts.Gateway.ProbesSent >= 3
            && facts.Gateway.ProbesReceived == 0)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "NETWORK_GATEWAY_UNREACHABLE_001",
                Category = DiagnosticCategory.Network,
                Severity = DiagnosticSeverity.Error,
                Confidence = DiagnosticConfidence.Medium,
                TitleKey = "Diag_Finding_GatewayUnreachable_Title",
                SummaryKey = "Diag_Finding_GatewayUnreachable_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                [
                    DiagnosticEvidence.Measured("network.gateway.probesSent", "Diag_Evidence_ProbesSent", facts.Gateway.ProbesSent, "", "Ping", facts.Gateway.GatewayAddress),
                    DiagnosticEvidence.Measured("network.gateway.probesReceived", "Diag_Evidence_ProbesReceived", facts.Gateway.ProbesReceived, "", "Ping", facts.Gateway.GatewayAddress)
                ],
                PossibleCauseKeys = ["Diag_Cause_BoxReboot", "Diag_Cause_CableWifi"],
                RecommendedNextStepKeys = ["Diag_Network_RestartBox", "Diag_Next_OpenNetwork"],
                ActionIds = [DiagnosticActionCatalog.OpenNetwork],
                IsActionable = true
            });
        }

        if (!facts.Dns.Resolved && facts.AnyAdapterUp)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "NETWORK_DNS_FAIL_001",
                Category = DiagnosticCategory.Network,
                Severity = DiagnosticSeverity.Error,
                Confidence = DiagnosticConfidence.Medium,
                TitleKey = "Diag_Finding_DnsFail_Title",
                SummaryKey = "Diag_Finding_DnsFail_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("network.dns.resolved", "Diag_Evidence_DnsResult", "false", "", "DnsQuery", facts.Dns.HostName)],
                RecommendedNextStepKeys = ["Diag_Next_OpenNetwork"],
                ActionIds = [DiagnosticActionCatalog.OpenNetwork],
                IsActionable = true
            });
        }
        else if (facts.Dns.ResolveMs is > DnsSlowMs)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "NETWORK_DNS_SLOW_001",
                Category = DiagnosticCategory.Network,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_DnsSlow_Title",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("network.dns.resolveMs", "Diag_Evidence_DnsDuration", Math.Round(facts.Dns.ResolveMs.Value, 0), "ms", "DnsQuery", facts.Dns.HostName)],
                RecommendedNextStepKeys = ["Diag_Next_OpenNetwork"],
                IsActionable = false
            });
        }

        foreach (ExternalProbeFacts probe in facts.ExternalProbes)
        {
            if (probe.ProbesSent == 0)
            {
                continue;
            }

            double lossPercent = (probe.ProbesSent - probe.ProbesReceived) * 100.0 / probe.ProbesSent;
            if (lossPercent >= PacketLossErrorPercent)
            {
                findings.Add(CreateLossFinding(probe, DiagnosticSeverity.Error, DiagnosticConfidence.Medium));
            }
            else if (lossPercent >= PacketLossWarningPercent)
            {
                findings.Add(CreateLossFinding(probe, DiagnosticSeverity.Warning, DiagnosticConfidence.Medium));
            }
            else if (probe.MedianMs is > PingHighMedianMs)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "NETWORK_PING_HIGH_001",
                    Entity = probe.Destination,
                    Category = DiagnosticCategory.Network,
                    Severity = DiagnosticSeverity.Info,
                    Confidence = DiagnosticConfidence.Low,
                    TitleKey = "Diag_Finding_HighLatency_Title",
                    ObservedAtUtc = DateTime.UtcNow,
                    Evidence =
                    [
                        DiagnosticEvidence.Measured("network.ping.medianMs", "Diag_Evidence_MedianPing", Math.Round(probe.MedianMs.Value, 1), "ms", "Ping", probe.Destination),
                        DiagnosticEvidence.Measured("network.ping.p95Ms", "Diag_Evidence_P95Ping", Math.Round(probe.P95Ms ?? 0, 1), "ms", "Ping", probe.Destination)
                    ],
                    RecommendedNextStepKeys = ["Diag_Next_OpenNetwork"],
                    IsFactBacked = false
                });
            }
        }

        return findings;
    }

    private static DiagnosticFinding CreateLossFinding(ExternalProbeFacts probe, DiagnosticSeverity severity, DiagnosticConfidence confidence)
        => new()
        {
            Id = "NETWORK_PACKET_LOSS_001",
            Entity = probe.Destination,
            Category = DiagnosticCategory.Network,
            Severity = severity,
            Confidence = confidence,
            TitleKey = "Diag_Finding_PacketLoss_Title",
            SummaryKey = "Diag_Finding_PacketLoss_Summary",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence =
            [
                DiagnosticEvidence.Measured("network.ping.probesSent", "Diag_Evidence_ProbesSent", probe.ProbesSent, "", "Ping", probe.Destination),
                DiagnosticEvidence.Measured("network.ping.probesReceived", "Diag_Evidence_ProbesReceived", probe.ProbesReceived, "", "Ping", probe.Destination)
            ],
            PossibleCauseKeys = ["Diag_Cause_WifiSignal", "Diag_Cause_BoxSaturated"],
            RecommendedNextStepKeys = ["Diag_Next_OpenNetwork"],
            ActionIds = [DiagnosticActionCatalog.OpenNetwork],
            IsActionable = true
        };

    public static IReadOnlyList<DiagnosticFinding> EvaluateWindows(WindowsFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (facts.UnexpectedShutdowns7d >= UnexpectedShutdownWarning7d)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "WINDOWS_UNEXPECTED_SHUTDOWN_001",
                Category = DiagnosticCategory.Windows,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Medium,
                TitleKey = "Diag_Finding_UnexpectedShutdown_Title",
                SummaryKey = "Diag_Finding_UnexpectedShutdown_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("windows.unexpectedShutdowns7d", "Diag_Evidence_UnexpectedShutdowns", facts.UnexpectedShutdowns7d, "", "EventLog:System")],
                PossibleCauseKeys = ["Diag_Cause_PowerLoss", "Diag_Cause_Crash", "Diag_Cause_Thermal"],
                RecommendedNextStepKeys = ["Diag_Next_CheckReliabilityMonitor"]
            });
        }

        foreach (EventGroupFacts group in facts.CriticalGroups7d.Where(group => group.Count >= CriticalEventGroupWarning7d))
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "WINDOWS_CRITICAL_EVENTS_001",
                Entity = $"{group.Provider}/{group.EventId}",
                Category = DiagnosticCategory.Windows,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Medium,
                TitleKey = "Diag_Finding_RecurringEvents_Title",
                SummaryKey = "Diag_Finding_RecurringEvents_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                    [
                        DiagnosticEvidence.Measured("windows.events.count7d", "Diag_Evidence_EventCount", group.Count, "", "EventLog:System"),
                        DiagnosticEvidence.Measured("windows.events.provider", "Diag_Evidence_EventProvider", group.Provider, "", "EventLog:System")
                    ],
                RecommendedNextStepKeys = ["Diag_Next_CheckEventDetails"]
            });
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluateDevices(IReadOnlyList<DeviceProblemFacts> problemDevices)
    {
        List<DiagnosticFinding> findings = [];

        foreach (DeviceProblemFacts device in problemDevices)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "DEVICE_PNP_ERROR_001",
                Entity = device.DeviceName,
                Category = DiagnosticCategory.Devices,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.High,
                TitleKey = "Diag_Finding_DeviceError_Title",
                SummaryKey = "Diag_Finding_DeviceError_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                [
                    DiagnosticEvidence.Measured("device.pnp.code", "Diag_Evidence_DeviceCode", device.ConfigManagerErrorCode, "", "Win32_PnPEntity", device.DeviceName),
                    DiagnosticEvidence.Measured("device.pnp.class", "Diag_Evidence_DeviceClass", device.DeviceClass ?? "-", "", "Win32_PnPEntity", device.DeviceName)
                ],
                RecommendedNextStepKeys = ["Diag_Next_OpenDeviceManager"],
                ActionIds = [DiagnosticActionCatalog.OpenDeviceManager],
                IsActionable = true
            });
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluateSecurity(DefenderFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (!facts.StatusReadable)
        {
            return findings;
        }

        if (facts.AntivirusEnabled && !facts.RealTimeProtectionEnabled)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "SECURITY_RTP_DISABLED_001",
                Category = DiagnosticCategory.Security,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.High,
                TitleKey = "Diag_Finding_RtpDisabled_Title",
                SummaryKey = "Diag_Finding_RtpDisabled_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("defender.realTimeProtection", "Diag_Evidence_Rtp", "false", "", "Get-MpComputerStatus")],
                RecommendedNextStepKeys = ["Diag_Next_OpenWindowsSecurity"],
                ActionIds = [DiagnosticActionCatalog.OpenHealthDefense],
                IsActionable = true
            });
        }
        else if (!facts.AntivirusEnabled && !facts.AntispywareEnabled)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "SECURITY_AV_OFF_001",
                Category = DiagnosticCategory.Security,
                Severity = DiagnosticSeverity.Error,
                Confidence = DiagnosticConfidence.High,
                TitleKey = "Diag_Finding_AvOff_Title",
                SummaryKey = "Diag_Finding_AvOff_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence =
                [
                    DiagnosticEvidence.Measured("defender.antivirusEnabled", "Diag_Evidence_Av", "false", "", "Get-MpComputerStatus"),
                    DiagnosticEvidence.Measured("defender.antispywareEnabled", "Diag_Evidence_Antispyware", "false", "", "Get-MpComputerStatus")
                ],
                RecommendedNextStepKeys = ["Diag_Next_OpenWindowsSecurity"],
                ActionIds = [DiagnosticActionCatalog.OpenHealthDefense],
                IsActionable = true
            });
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluatePowerAndThermal(BatteryFacts battery, ThermalFacts thermal)
    {
        List<DiagnosticFinding> findings = [];

        if (battery.BatteryPresent && battery.FullLifeRatio is double ratio && ratio < BatteryWearWarningRatio)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "POWER_BATTERY_WEAR_001",
                Category = DiagnosticCategory.Power,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_BatteryWear_Title",
                SummaryKey = "Diag_Finding_BatteryWear_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("battery.fullLifeRatio", "Diag_Evidence_BatteryRatio", Math.Round(ratio * 100, 0), "%", "GetSystemPowerStatus")],
                RecommendedNextStepKeys = ["Diag_Next_KeepPluggedHeavyLoad"],
                IsFactBacked = false
            });
        }

        if (thermal.ThermalZoneAvailable && thermal.ThermalZoneC is double zoneC && zoneC >= CpuTemperatureWarningC)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "POWER_THERMAL_ZONE_001",
                Category = DiagnosticCategory.Power,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Low,
                TitleKey = "Diag_Finding_ThermalZone_Title",
                SummaryKey = "Diag_Finding_ThermalZone_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("thermal.zoneC", "Diag_Evidence_ThermalZone", Math.Round(zoneC, 1), "°C", "MSAcpi_ThermalZoneTemperature")],
                PossibleCauseKeys = ["Diag_Cause_Dust", "Diag_Cause_FanCurve"],
                RecommendedNextStepKeys = ["Diag_Next_CheckCooling"],
                IsFactBacked = false
            });
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> EvaluateProcesses(ProcessFacts facts)
    {
        List<DiagnosticFinding> findings = [];

        if (facts.StartupHealthReadable && !facts.StartupHealthy)
        {
            findings.Add(new DiagnosticFinding
            {
                Id = "STARTUP_HEALTH_001",
                Category = DiagnosticCategory.Processes,
                Severity = DiagnosticSeverity.Warning,
                Confidence = DiagnosticConfidence.Medium,
                TitleKey = "Diag_Finding_StartupHealth_Title",
                SummaryKey = "Diag_Finding_StartupHealth_Summary",
                ObservedAtUtc = DateTime.UtcNow,
                Evidence = [DiagnosticEvidence.Measured("startup.healthy", "Diag_Evidence_StartupHealth", "false", "", "StartupHealthService")],
                RecommendedNextStepKeys = ["Diag_Next_InspectStartup"],
                ActionIds = [DiagnosticActionCatalog.OpenPrograms],
                IsActionable = true
            });
        }

        return findings;
    }

    public static IReadOnlyList<DiagnosticFinding> SortFindings(IEnumerable<DiagnosticFinding> findings)
        => findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenByDescending(f => f.ObservedAtUtc)
            .ToList();

    public static IReadOnlyList<DiagnosticFinding> Deduplicate(IEnumerable<DiagnosticFinding> findings)
        => findings
            .GroupBy(f => f.DedupeKey, StringComparer.Ordinal)
            .Select(g => g
                .OrderByDescending(f => f.Severity)
                .ThenByDescending(f => f.Confidence)
                .First())
            .ToList();
}

using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Rules;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticRuleEngineTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void Storage_FreeSpaceAboveThresholds_ProducesNoFinding()
    {
        StorageFacts facts = new(
            Volumes: [new VolumeFacts("C:", 100 * GiB, 50 * GiB, "NTFS", "Fixed")],
            Disks: [],
            DisksEnumerated: false);

        Assert.Empty(DiagnosticRuleEngine.EvaluateStorage(facts));
    }

    [Theory]
    [InlineData(4.9, 50, DiagnosticSeverity.Warning)]
    [InlineData(50, 4.9, DiagnosticSeverity.Warning)]
    [InlineData(1.9, 20, DiagnosticSeverity.Error)]
    [InlineData(50, 1.9, DiagnosticSeverity.Error)]
    public void Storage_LowSpace_BelowThresholds_ProducesFinding(
        double freeGiB, double freePercent, DiagnosticSeverity expectedSeverity)
    {
        long total = 100 * GiB;
        long free = (long)(total * freePercent / 100.0);
        if (freeGiB < 5)
        {
            free = Math.Min(free, (long)(freeGiB * GiB));
        }

        StorageFacts facts = new(
            Volumes: [new VolumeFacts("C:", total, free, "NTFS", "Fixed")],
            Disks: [],
            DisksEnumerated: false);

        IReadOnlyList<DiagnosticFinding> findings = DiagnosticRuleEngine.EvaluateStorage(facts);

        DiagnosticFinding finding = Assert.Single(findings);
        Assert.Equal("STORAGE_LOW_SPACE_001", finding.Id);
        Assert.Equal(expectedSeverity, finding.Severity);
        Assert.Equal(DiagnosticConfidence.High, finding.Confidence);
        Assert.Equal("C:", finding.Entity);
    }

    [Fact]
    public void Storage_ZeroTotalVolume_IsIgnored()
    {
        StorageFacts facts = new(
            Volumes: [new VolumeFacts("Z:", 0, 0, "NTFS", "Unknown")],
            Disks: [],
            DisksEnumerated: false);

        Assert.Empty(DiagnosticRuleEngine.EvaluateStorage(facts));
    }

    [Fact]
    public void Storage_DiskWithoutReliabilityCounters_ProducesNoFinding()
    {
        StorageFacts facts = new(
            Volumes: [],
            Disks: [new DiskReliabilityFacts("Samsung 990 Pro", null, null, null, null, null, null)],
            DisksEnumerated: true);

        Assert.Empty(DiagnosticRuleEngine.EvaluateStorage(facts));
    }

    [Fact]
    public void Storage_UncorrectedReadErrors_ProduceErrorFinding()
    {
        StorageFacts facts = new(
            Volumes: [],
            Disks: [new DiskReliabilityFacts("Old HDD", null, null, 3, null, null, null)],
            DisksEnumerated: true);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateStorage(facts));
        Assert.Equal("STORAGE_RELIABILITY_ERROR_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Storage_WearAbove80_ProducesWarningFinding()
    {
        StorageFacts facts = new(
            Volumes: [],
            Disks: [new DiskReliabilityFacts("SSD A", null, 81, null, null, null, null)],
            DisksEnumerated: true);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateStorage(facts));
        Assert.Equal("STORAGE_WEAR_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Storage_WearAtExactThreshold80_ProducesNoFinding()
    {
        StorageFacts facts = new(
            Volumes: [],
            Disks: [new DiskReliabilityFacts("SSD A", null, 80, null, null, null, null)],
            DisksEnumerated: true);

        Assert.Empty(DiagnosticRuleEngine.EvaluateStorage(facts));
    }

    [Fact]
    public void Memory_HighAvailableRam_ProducesNoFinding()
    {
        MemoryFacts facts = new(
            TotalPhysicalBytes: 16 * GiB,
            AvailablePhysicalBytes: 8 * GiB,
            CommitTotalBytes: 10 * GiB,
            CommitLimitBytes: 20 * GiB,
            ProcessCount: 120,
            TopProcessName: null,
            TopProcessWorkingSetBytes: null);

        Assert.Empty(DiagnosticRuleEngine.EvaluateMemory(facts));
    }

    [Fact]
    public void Memory_LowAvailableRam_ProducesLowConfidenceWarning()
    {
        MemoryFacts facts = new(
            TotalPhysicalBytes: 8 * GiB,
            AvailablePhysicalBytes: 100 * 1024 * 1024,
            CommitTotalBytes: 10 * GiB,
            CommitLimitBytes: 10 * GiB,
            ProcessCount: 200,
            TopProcessName: null,
            TopProcessWorkingSetBytes: null);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateMemory(facts));
        Assert.Equal("MEMORY_PRESSURE_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal(DiagnosticConfidence.Low, finding.Confidence);
        Assert.False(finding.IsFactBacked);
    }

    [Fact]
    public void Memory_TopConsumerAt25Percent_IsOnlyInformational()
    {
        MemoryFacts facts = new(
            TotalPhysicalBytes: 8 * GiB,
            AvailablePhysicalBytes: 4 * GiB,
            CommitTotalBytes: 5 * GiB,
            CommitLimitBytes: 12 * GiB,
            ProcessCount: 100,
            TopProcessName: "BigApp",
            TopProcessWorkingSetBytes: 2 * GiB);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateMemory(facts));
        Assert.Equal("MEMORY_TOP_CONSUMER_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Info, finding.Severity);
        Assert.False(finding.IsFactBacked);
    }

    [Fact]
    public void Cpu_LoadBelowThreshold_ProducesNoFinding()
    {
        CpuFacts facts = new("Intel i7", 16, 10, 40, null, false, null, null);
        Assert.Empty(DiagnosticRuleEngine.EvaluateCpu(facts));
    }

    [Fact]
    public void Cpu_HighLoad_IsOnlyInformational()
    {
        CpuFacts facts = new("Intel i7", 16, 90, 100, null, false, null, null);
        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateCpu(facts));
        Assert.Equal("CPU_HIGH_LOAD_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Cpu_MissingTemperature_ProducesNoThermalFinding()
    {
        CpuFacts facts = new("AMD Ryzen", 16, 5, 20, null, false, null, null);
        Assert.Empty(DiagnosticRuleEngine.EvaluateCpu(facts));
    }

    [Theory]
    [InlineData(84.9, 0)]
    [InlineData(85.0, 1)]
    [InlineData(95.0, 1)]
    [InlineData(99.0, 1)]
    public void Cpu_TemperatureThresholds(double temperatureC, int expectedFindings)
    {
        CpuFacts facts = new("Intel i7", 16, 5, 20, temperatureC, true, null, null);
        IReadOnlyList<DiagnosticFinding> findings = DiagnosticRuleEngine.EvaluateCpu(facts);
        Assert.Equal(expectedFindings, findings.Count);

        if (expectedFindings > 0)
        {
            Assert.All(findings, f => Assert.Equal("CPU_THERMAL_001", f.Id));
            if (temperatureC >= DiagnosticRuleEngine.CpuTemperatureErrorC)
            {
                Assert.Equal(DiagnosticSeverity.Error, findings[0].Severity);
            }
            else
            {
                Assert.Equal(DiagnosticSeverity.Warning, findings[0].Severity);
            }
        }
    }

    [Fact]
    public void Network_NoAdapterUp_DnsFailureIsNotReportedAsError()
    {
        NetworkFacts facts = new(
            UpAdapters: [],
            AnyAdapterUp: false,
            ActiveAdapterName: null,
            LinkSpeedMbps: null,
            Gateway: new GatewayProbeFacts(null, 0, 0, null, null),
            Dns: new DnsProbeFacts("www.microsoft.com", false, null),
            ExternalProbes: []);

        Assert.Empty(DiagnosticRuleEngine.EvaluateNetwork(facts));
    }

    [Fact]
    public void Network_GatewayLostWithSeveralProbes_ProducesError()
    {
        NetworkFacts facts = new(
            UpAdapters: ["Ethernet"],
            AnyAdapterUp: true,
            ActiveAdapterName: "Ethernet",
            LinkSpeedMbps: 1000,
            Gateway: new GatewayProbeFacts("192.168.1.1", 4, 0, null, null),
            Dns: new DnsProbeFacts("www.microsoft.com", true, 20),
            ExternalProbes: []);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateNetwork(facts));
        Assert.Equal("NETWORK_GATEWAY_UNREACHABLE_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Network_GatewayWithTooFewProbes_ProducesNoFinding()
    {
        NetworkFacts facts = new(
            UpAdapters: ["Ethernet"],
            AnyAdapterUp: true,
            ActiveAdapterName: "Ethernet",
            LinkSpeedMbps: 1000,
            Gateway: new GatewayProbeFacts("192.168.1.1", 2, 0, null, null),
            Dns: new DnsProbeFacts("www.microsoft.com", true, 20),
            ExternalProbes: []);

        Assert.Empty(DiagnosticRuleEngine.EvaluateNetwork(facts));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(10, 0)]
    [InlineData(4, 4)]
    public void Network_PacketLossThresholds(int sent, int received)
    {
        NetworkFacts facts = new(
            UpAdapters: ["Ethernet"],
            AnyAdapterUp: true,
            ActiveAdapterName: "Ethernet",
            LinkSpeedMbps: 1000,
            Gateway: new GatewayProbeFacts("192.168.1.1", 4, 4, 1, 2),
            Dns: new DnsProbeFacts("www.microsoft.com", true, 20),
            ExternalProbes: [new ExternalProbeFacts("1.1.1.1", sent, received, null, null)]);

        double loss = (sent - received) * 100.0 / sent;
        IReadOnlyList<DiagnosticFinding> findings = DiagnosticRuleEngine.EvaluateNetwork(facts);

        if (loss >= DiagnosticRuleEngine.PacketLossWarningPercent)
        {
            Assert.Contains(findings, f => f.Id == "NETWORK_PACKET_LOSS_001");
        }
        else
        {
            Assert.DoesNotContain(findings, f => f.Id == "NETWORK_PACKET_LOSS_001");
        }
    }

    [Fact]
    public void Network_IcmpBlockedHost_IsHighLatencyInfoAtMost()
    {
        NetworkFacts facts = new(
            UpAdapters: ["Ethernet"],
            AnyAdapterUp: true,
            ActiveAdapterName: "Ethernet",
            LinkSpeedMbps: 1000,
            Gateway: new GatewayProbeFacts("192.168.1.1", 4, 4, 1, 2),
            Dns: new DnsProbeFacts("www.microsoft.com", true, 20),
            ExternalProbes: [new ExternalProbeFacts("example.net", 4, 4, 200, 300)]);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateNetwork(facts));
        Assert.Equal("NETWORK_PING_HIGH_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Info, finding.Severity);
        Assert.Equal(DiagnosticConfidence.Low, finding.Confidence);
    }

    [Fact]
    public void Windows_NoUnexpectedShutdowns_ProducesNoFinding()
    {
        WindowsFacts facts = new("Windows 11", "26100", TimeSpan.FromDays(3), 0, [], true);
        Assert.Empty(DiagnosticRuleEngine.EvaluateWindows(facts));
    }

    [Fact]
    public void Windows_UnexpectedShutdown_ProducesWarning()
    {
        WindowsFacts facts = new("Windows 11", "26100", TimeSpan.FromDays(3), 1, [], true);
        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateWindows(facts));
        Assert.Equal("WINDOWS_UNEXPECTED_SHUTDOWN_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Windows_IsolatedCriticalEvent_IsNotAProblem()
    {
        WindowsFacts facts = new(
            "Windows 11", "26100", TimeSpan.FromDays(3), 0,
            [new EventGroupFacts("Application", 1000, 1, DateTime.UtcNow, "Error")],
            EventLogAccessible: true);

        Assert.Empty(DiagnosticRuleEngine.EvaluateWindows(facts));
    }

    [Fact]
    public void Windows_RecurringCriticalEvents_ProduceOneFindingPerGroup()
    {
        WindowsFacts facts = new(
            "Windows 11", "26100", TimeSpan.FromDays(3), 0,
            [
                new EventGroupFacts("Disk", 154, 25, DateTime.UtcNow, "Error"),
                new EventGroupFacts("WHEA", 18, 14, DateTime.UtcNow, "Error")
            ],
            EventLogAccessible: true);

        Assert.Equal(2, DiagnosticRuleEngine.EvaluateWindows(facts).Count);
    }

    [Fact]
    public void Devices_ProblemDevice_ProducesFindingWithWindowsCode()
    {
        IReadOnlyList<DiagnosticFinding> findings = DiagnosticRuleEngine.EvaluateDevices(
            [new DeviceProblemFacts("Unknown USB Device", "USB", 28)]);

        DiagnosticFinding finding = Assert.Single(findings);
        Assert.Equal("DEVICE_PNP_ERROR_001", finding.Id);
        Assert.Equal("Unknown USB Device", finding.Entity);
        Assert.Equal(DiagnosticConfidence.High, finding.Confidence);
    }

    [Fact]
    public void Security_UnreadableStatus_ProducesNoFinding()
    {
        DefenderFacts facts = new(false, false, false, false, null);
        Assert.Empty(DiagnosticRuleEngine.EvaluateSecurity(facts));
    }

    [Fact]
    public void Security_RealTimeProtectionDisabled_ProducesWarning()
    {
        DefenderFacts facts = new(true, false, true, true, "1");
        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateSecurity(facts));
        Assert.Equal("SECURITY_RTP_DISABLED_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Security_AllProtectionsOff_ProducesError()
    {
        DefenderFacts facts = new(true, false, false, false, "30");
        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateSecurity(facts));
        Assert.Equal("SECURITY_AV_OFF_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Power_NoBatteryOnDesktop_ProducesNoFinding()
    {
        BatteryFacts battery = new(false, true, null, null, null, DataAvailable: false);
        ThermalFacts thermal = new(null, false);
        Assert.Empty(DiagnosticRuleEngine.EvaluatePowerAndThermal(battery, thermal));
    }

    [Fact]
    public void Power_WornBattery_ProducesLowConfidenceWarning()
    {
        BatteryFacts battery = new(true, false, 40, 60, 0.4, DataAvailable: true);
        ThermalFacts thermal = new(null, false);

        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluatePowerAndThermal(battery, thermal));
        Assert.Equal("POWER_BATTERY_WEAR_001", finding.Id);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal(DiagnosticConfidence.Low, finding.Confidence);
    }

    [Fact]
    public void Power_HealthyBattery_ProducesNoFinding()
    {
        BatteryFacts battery = new(true, false, 80, 240, 0.9, DataAvailable: true);
        ThermalFacts thermal = new(null, false);
        Assert.Empty(DiagnosticRuleEngine.EvaluatePowerAndThermal(battery, thermal));
    }

    [Fact]
    public void Processes_UnhealthyStartup_ProducesWarning()
    {
        ProcessFacts facts = new(150, "app", 5, "app", 3, true, false, "démarrage lent");
        DiagnosticFinding finding = Assert.Single(DiagnosticRuleEngine.EvaluateProcesses(facts));
        Assert.Equal("STARTUP_HEALTH_001", finding.Id);
    }

    [Fact]
    public void Processes_HealthyStartup_ProducesNoFinding()
    {
        ProcessFacts facts = new(150, "app", 5, "app", 3, true, true, null);
        Assert.Empty(DiagnosticRuleEngine.EvaluateProcesses(facts));
    }

    [Fact]
    public void SortFindings_OrdersBySeverityThenConfidenceThenDate()
    {
        DateTime baseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DiagnosticFinding info = MakeFinding("A", DiagnosticSeverity.Info, DiagnosticConfidence.High, baseTime);
        DiagnosticFinding warningLow = MakeFinding("B", DiagnosticSeverity.Warning, DiagnosticConfidence.Low, baseTime);
        DiagnosticFinding warningHigh = MakeFinding("C", DiagnosticSeverity.Warning, DiagnosticConfidence.High, baseTime);
        DiagnosticFinding critical = MakeFinding("D", DiagnosticSeverity.Critical, DiagnosticConfidence.Low, baseTime);

        IReadOnlyList<DiagnosticFinding> sorted = DiagnosticRuleEngine.SortFindings([info, warningLow, warningHigh, critical]);

        Assert.Equal(["D", "C", "B", "A"], sorted.Select(f => f.Id).ToArray());
    }

    [Fact]
    public void Deduplicate_KeepsMostSeverePerRuleAndEntity()
    {
        DiagnosticFinding low = MakeFinding("R1", DiagnosticSeverity.Warning, DiagnosticConfidence.High, DateTime.UtcNow, entity: "C:");
        DiagnosticFinding severe = MakeFinding("R1", DiagnosticSeverity.Error, DiagnosticConfidence.Low, DateTime.UtcNow, entity: "C:");
        DiagnosticFinding other = MakeFinding("R1", DiagnosticSeverity.Warning, DiagnosticConfidence.Low, DateTime.UtcNow, entity: "D:");

        IReadOnlyList<DiagnosticFinding> deduped = DiagnosticRuleEngine.Deduplicate([low, severe, other]);

        Assert.Equal(2, deduped.Count);
        Assert.Contains(deduped, f => f.Severity == DiagnosticSeverity.Error && f.Entity == "C:");
        Assert.Contains(deduped, f => f.Entity == "D:");
    }

    [Fact]
    public void AllRuleFindings_NeverCarryFreeCommandStrings()
    {
        StorageFacts storage = new(
            [new VolumeFacts("C:", 100 * GiB, 1 * GiB, "NTFS", "Fixed")],
            [],
            DisksEnumerated: true);
        CpuFacts cpu = new("Intel i7", 16, 90, 100, 95, true, null, null);

        IEnumerable<DiagnosticFinding> all =
            DiagnosticRuleEngine.EvaluateStorage(storage)
            .Concat(DiagnosticRuleEngine.EvaluateCpu(cpu));

        Assert.All(all, f =>
        {
            Assert.DoesNotContain("cmd", string.Join(' ', f.ActionIds), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("powershell", string.Join(' ', f.ActionIds), StringComparison.OrdinalIgnoreCase);
            Assert.All(f.ActionIds, static actionId => Assert.Matches("^[a-z0-9_]+$", actionId));
        });
    }

    private static DiagnosticFinding MakeFinding(
        string id,
        DiagnosticSeverity severity,
        DiagnosticConfidence confidence,
        DateTime observedAt,
        string? entity = null)
        => new()
        {
            Id = id,
            Entity = entity,
            Category = DiagnosticCategory.Storage,
            Severity = severity,
            Confidence = confidence,
            TitleKey = "Diag_Finding_LowSpace_Title",
            ObservedAtUtc = observedAt
        };
}

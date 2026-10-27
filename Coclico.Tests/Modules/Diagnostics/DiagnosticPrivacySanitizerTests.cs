using Coclico.Modules.Diagnostics.Models;
using Coclico.Modules.Diagnostics.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DiagnosticPrivacySanitizerTests
{
    private readonly DiagnosticPrivacySanitizer _sanitizer = new();

    [Theory]
    [InlineData("C:\\Users\\martin\\Documents\\report.docx", "C:\\Users\\***\\Documents\\report.docx")]
    [InlineData("c:\\users\\john.doe\\appdata\\local\\temp", "C:\\Users\\***\\appdata\\local\\temp")]
    [InlineData("D:\\Games\\setup.exe", "D:\\Games\\setup.exe")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeText_MasksUserProfilePaths(string? input, string expected)
    {
        string result = _sanitizer.SanitizeText(input);
        if (input is null || input.Length == 0)
        {
            Assert.Equal(expected, result);
        }
        else if (input.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain("martin", result, StringComparison.Ordinal);
            Assert.DoesNotContain("john.doe", result, StringComparison.Ordinal);
            Assert.StartsWith(DiagnosticPrivacySanitizer.UserPlaceholder, result, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void SanitizeText_MasksSerialNumbers()
    {
        string result = _sanitizer.SanitizeText("Serial Number: ABC12345-XYZ");

        Assert.DoesNotContain("ABC12345", result, StringComparison.Ordinal);
        Assert.Contains(DiagnosticPrivacySanitizer.MaskedValue, result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeText_MasksLocalMachineName()
    {
        string machineName = Environment.MachineName;
        if (string.IsNullOrEmpty(machineName))
        {
            return;
        }

        string result = _sanitizer.SanitizeText($"PC de {machineName}");

        Assert.DoesNotContain(machineName, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeFindings_MasksEntitiesAndEvidence()
    {
        DiagnosticFinding finding = new()
        {
            Id = "DEVICE_PNP_ERROR_001",
            Entity = "C:\\Users\\martin\\USB Device",
            Category = DiagnosticCategory.Devices,
            Severity = DiagnosticSeverity.Warning,
            TitleKey = "Diag_Finding_DeviceError_Title",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence =
            [
                DiagnosticEvidence.Measured("device.path", "Diag_Evidence_DeviceClass", "C:\\Users\\martin\\dev\\driver.sys", "", "Win32_PnPEntity"),
                new DiagnosticEvidence
                {
                    MetricId = "device.serial",
                    DisplayNameKey = "Diag_Evidence_DeviceCode",
                    Value = "S/N 987654321",
                    Source = "Win32_PnPEntity",
                    ObservedAtUtc = DateTime.UtcNow,
                    RawDetails = "raw C:\\Users\\martin"
                }
            ]
        };

        IReadOnlyList<DiagnosticFinding> sanitized = _sanitizer.SanitizeFindings([finding]);

        DiagnosticFinding clean = Assert.Single(sanitized);
        Assert.DoesNotContain("martin", clean.Entity ?? string.Empty, StringComparison.Ordinal);
        Assert.All(clean.Evidence, e =>
        {
            Assert.DoesNotContain("martin", e.Value ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("martin", e.RawDetails ?? string.Empty, StringComparison.Ordinal);
        });

        Assert.Equal(DiagnosticPrivacySanitizer.MaskedValue, clean.Evidence[1].Value);
    }

    [Fact]
    public void SanitizeFindings_DoesNotMutateInput()
    {
        DiagnosticFinding finding = new()
        {
            Id = "R1",
            Entity = "C:\\Users\\martin\\disk",
            Category = DiagnosticCategory.Storage,
            TitleKey = "Diag_Finding_LowSpace_Title",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence = [DiagnosticEvidence.Measured("storage.volume.freeBytes", "Diag_Evidence_FreeSpace", 123L, "bytes", "DriveInfo")]
        };

        _ = _sanitizer.SanitizeFindings([finding]);

        Assert.Equal("C:\\Users\\martin\\disk", finding.Entity);
    }

    [Fact]
    public void SensitiveMetrics_MacAndSerial_AreFullyMasked()
    {
        DiagnosticFinding finding = new()
        {
            Id = "R2",
            Category = DiagnosticCategory.Network,
            TitleKey = "Diag_Finding_LowSpace_Title",
            ObservedAtUtc = DateTime.UtcNow,
            Evidence =
            [
                DiagnosticEvidence.Measured("adapter.macAddress", "Diag_Evidence_DeviceClass", "AA:BB:CC:DD:EE:FF", "", "Win32_NetworkAdapter"),
                DiagnosticEvidence.Measured("adapter.serialNumber", "Diag_Evidence_DeviceClass", "SN-42", "", "Win32_NetworkAdapter"),
                DiagnosticEvidence.Measured("adapter.linkSpeedMbps", "Diag_Evidence_DeviceClass", 1000, "Mbps", "Win32_NetworkAdapter")
            ]
        };

        IReadOnlyList<DiagnosticFinding> sanitized = _sanitizer.SanitizeFindings([finding]);
        DiagnosticFinding clean = Assert.Single(sanitized);

        Assert.Equal(DiagnosticPrivacySanitizer.MaskedValue, clean.Evidence[0].Value);
        Assert.Equal(DiagnosticPrivacySanitizer.MaskedValue, clean.Evidence[1].Value);
        Assert.Equal("1000", clean.Evidence[2].Value);
    }
}

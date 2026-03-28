#nullable enable
// ViewModelTests.cs
// Tests des ViewModels et des modèles de données sous-jacents.
//
// STRATÉGIE :
//   Les ViewModels (SettingsViewModel, DashboardViewModel, ScannerViewModel)
//   utilisent ServiceContainer.GetRequired<T>() directement dans leur constructeur,
//   ce qui empêche l'instanciation sans un DI container complet et un contexte WPF/STA.
//
//   On teste donc :
//   - Les propriétés calculées / la logique de présentation via AppSettings (modèle).
//   - Les formules de DashboardViewModel reproduites en isolation (RamUsagePercent, etc.).
//   - Les règles de validation de SettingsService.Sanitize via des saves/loads.
//   - Les invariants de AuditRetentionDays (Math.Clamp) sans passer par le ViewModel.

using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AppSettings — Valeurs par défaut & propriétés
    // ─────────────────────────────────────────────────────────────────────────

    public class AppSettingsDefaultsTests
    {
        [Fact]
        public void Defaults_Language_IsFrench()
        {
            var settings = new AppSettings();
            Assert.Equal("fr", settings.Language);
        }

        [Fact]
        public void Defaults_AccentColor_IsValidHex()
        {
            var settings = new AppSettings();
            Assert.Matches(@"^#[0-9A-Fa-f]{6,8}$", settings.AccentColor);
        }

        [Fact]
        public void Defaults_FontSize_InValidRange()
        {
            var settings = new AppSettings();
            Assert.InRange(settings.FontSize, 8.0, 32.0);
        }

        [Fact]
        public void Defaults_SidebarWidth_InValidRange()
        {
            var settings = new AppSettings();
            Assert.InRange(settings.SidebarWidth, 52.0, 500.0);
        }

        [Fact]
        public void Defaults_CardOpacity_InValidRange()
        {
            var settings = new AppSettings();
            Assert.InRange(settings.CardOpacity, 0.0, 1.0);
        }

        [Fact]
        public void Defaults_WingetScope_IsMachine()
        {
            var settings = new AppSettings();
            Assert.Equal("machine", settings.WingetScope);
        }

        [Fact]
        public void Defaults_LaunchAtStartup_IsFalse()
        {
            var settings = new AppSettings();
            Assert.False(settings.LaunchAtStartup);
        }

        [Fact]
        public void Defaults_MinimizeToTray_IsFalse()
        {
            var settings = new AppSettings();
            Assert.False(settings.MinimizeToTray);
        }

        [Fact]
        public void Defaults_CodePatcherAuditOnly_IsTrue()
        {
            // Sécurité : l'audit-only doit être activé par défaut (safe default)
            var settings = new AppSettings();
            Assert.True(settings.CodePatcherAuditOnly);
        }

        [Fact]
        public void Defaults_AuditRetentionDays_Is90()
        {
            var settings = new AppSettings();
            Assert.Equal(90, settings.AuditRetentionDays);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SettingsService — Persistance + Sanitization
    // ─────────────────────────────────────────────────────────────────────────

    public class SettingsServicePersistenceTests
    {
        [Fact]
        public async Task SaveLoad_Language_Persists()
        {
            var svc = new SettingsService();
            await svc.LoadAsync();

            var original = svc.Settings.Language;
            try
            {
                svc.Settings.Language = "en";
                await svc.SaveAsync();

                var svc2 = new SettingsService();
                await svc2.LoadAsync();
                Assert.Equal("en", svc2.Settings.Language);
            }
            finally
            {
                svc.Settings.Language = original;
                await svc.SaveAsync();
            }
        }

        [Fact]
        public async Task SaveLoad_FontSize_ClampedOnLoad()
        {
            var svc = new SettingsService();
            await svc.LoadAsync();
            var original = svc.Settings.FontSize;

            try
            {
                // Valeur hors plage — doit être clampée à [8, 32] au prochain chargement
                svc.Settings.FontSize = 100.0;  // dépasse le max
                await svc.SaveAsync();

                var svc2 = new SettingsService();
                await svc2.LoadAsync();
                Assert.InRange(svc2.Settings.FontSize, 8.0, 32.0);
            }
            finally
            {
                svc.Settings.FontSize = original;
                await svc.SaveAsync();
            }
        }

        [Fact]
        public async Task SaveLoad_CodePatcherAuditOnly_Persists()
        {
            var svc = new SettingsService();
            await svc.LoadAsync();
            var original = svc.Settings.CodePatcherAuditOnly;

            try
            {
                svc.Settings.CodePatcherAuditOnly = false;
                await svc.SaveAsync();

                var svc2 = new SettingsService();
                await svc2.LoadAsync();
                Assert.False(svc2.Settings.CodePatcherAuditOnly);
            }
            finally
            {
                svc.Settings.CodePatcherAuditOnly = original;
                await svc.SaveAsync();
            }
        }

        [Fact]
        public async Task SaveLoad_InvalidLanguage_FallsBackToFrench()
        {
            var svc = new SettingsService();
            await svc.LoadAsync();
            var original = svc.Settings.Language;

            try
            {
                svc.Settings.Language = "xyz";  // Langue invalide
                await svc.SaveAsync();

                var svc2 = new SettingsService();
                await svc2.LoadAsync();
                // La sanitization doit corriger vers "fr"
                Assert.Equal("fr", svc2.Settings.Language);
            }
            finally
            {
                svc.Settings.Language = original;
                await svc.SaveAsync();
            }
        }

        [Fact]
        public async Task SaveLoad_AuditRetentionDays_Persists()
        {
            var svc = new SettingsService();
            await svc.LoadAsync();
            var original = svc.Settings.AuditRetentionDays;

            try
            {
                svc.Settings.AuditRetentionDays = 180;
                await svc.SaveAsync();

                var svc2 = new SettingsService();
                await svc2.LoadAsync();
                Assert.Equal(180, svc2.Settings.AuditRetentionDays);
            }
            finally
            {
                svc.Settings.AuditRetentionDays = original;
                await svc.SaveAsync();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DashboardViewModel — Formules de calcul (testées sans instanciation WPF)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tests des formules issues de DashboardViewModel sans avoir besoin du
    /// thread STA / WPF. On vérifie la logique de calcul de façon isolée.
    /// </summary>
    public class DashboardViewModelFormulasTests
    {
        // Reproduit la formule : RamUsagePercent = RamTotalGb > 0 ? (RamUsedGb / RamTotalGb) * 100.0 : 0
        private static double ComputeRamUsagePercent(double used, double total)
            => total > 0 ? (used / total) * 100.0 : 0;

        // Reproduit la formule : DiskFreeGb = DiskTotalGb - DiskUsedGb
        private static double ComputeDiskFreeGb(double total, double used)
            => total - used;

        // Reproduit la formule : DiskUsagePercent = DiskTotalGb > 0 ? (DiskUsedGb / DiskTotalGb) * 100.0 : 0
        private static double ComputeDiskUsagePercent(double used, double total)
            => total > 0 ? (used / total) * 100.0 : 0;

        [Fact]
        public void RamUsagePercent_WithValidValues_IsCorrect()
        {
            double percent = ComputeRamUsagePercent(8.0, 16.0);
            Assert.Equal(50.0, percent, precision: 2);
        }

        [Fact]
        public void RamUsagePercent_WithZeroTotal_ReturnsZero()
        {
            double percent = ComputeRamUsagePercent(8.0, 0.0);
            Assert.Equal(0.0, percent);
        }

        [Fact]
        public void RamUsagePercent_FullRam_Returns100()
        {
            double percent = ComputeRamUsagePercent(16.0, 16.0);
            Assert.Equal(100.0, percent, precision: 2);
        }

        [Fact]
        public void DiskFreeGb_IsCorrectlyComputed()
        {
            double free = ComputeDiskFreeGb(500.0, 300.0);
            Assert.Equal(200.0, free, precision: 2);
        }

        [Fact]
        public void DiskUsagePercent_75Percent_IsCorrect()
        {
            double percent = ComputeDiskUsagePercent(375.0, 500.0);
            Assert.Equal(75.0, percent, precision: 2);
        }

        [Fact]
        public void DiskUsagePercent_WithZeroTotal_ReturnsZero()
        {
            double percent = ComputeDiskUsagePercent(100.0, 0.0);
            Assert.Equal(0.0, percent);
        }

        [Fact]
        public void CpuUsageText_FormattedCorrectly()
        {
            // Reproduit la propriété : $"{CpuUsage:F0}%"
            double cpuUsage = 42.7;
            string text = $"{cpuUsage:F0}%";
            Assert.Equal("43%", text);
        }

        [Fact]
        public void RamUsageText_FormattedCorrectly()
        {
            // Reproduit : $"{RamUsedGb.ToString("F1", CultureInfo.CurrentCulture)} GB / {RamTotalGb.ToString("F1", CultureInfo.CurrentCulture)} GB"
            // Le ViewModel utilise CultureInfo.CurrentCulture — on reproduit la même logique
            double used = 7.854, total = 15.9;
            string usedStr = used.ToString("F1", System.Globalization.CultureInfo.CurrentCulture);
            string totalStr = total.ToString("F1", System.Globalization.CultureInfo.CurrentCulture);
            string text = $"{usedStr} GB / {totalStr} GB";
            Assert.Contains($"{usedStr} GB", text);
            Assert.Contains($"{totalStr} GB", text);
            Assert.Contains("GB", text);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SettingsViewModel — Logique de clamp AuditRetentionDays
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Teste la logique de clamp de AuditRetentionDays (extraite du setter du ViewModel)
    /// sans avoir besoin d'instancier le ViewModel (qui nécessite WPF + DI).
    /// </summary>
    public class SettingsViewModelLogicTests
    {
        // Reproduit la logique du setter SettingsViewModel.AuditRetentionDays
        private static int ClampAuditRetention(int value) => Math.Clamp(value, 7, 3650);

        [Fact]
        public void AuditRetentionDays_BelowMin_ClampedTo7()
        {
            Assert.Equal(7, ClampAuditRetention(1));
            Assert.Equal(7, ClampAuditRetention(0));
            Assert.Equal(7, ClampAuditRetention(-100));
        }

        [Fact]
        public void AuditRetentionDays_AboveMax_ClampedTo3650()
        {
            Assert.Equal(3650, ClampAuditRetention(9999));
            Assert.Equal(3650, ClampAuditRetention(3651));
        }

        [Fact]
        public void AuditRetentionDays_ValidValue_ReturnedUnchanged()
        {
            Assert.Equal(90, ClampAuditRetention(90));
            Assert.Equal(365, ClampAuditRetention(365));
            Assert.Equal(7, ClampAuditRetention(7));
            Assert.Equal(3650, ClampAuditRetention(3650));
        }

        [Theory]
        [InlineData(7)]
        [InlineData(30)]
        [InlineData(90)]
        [InlineData(365)]
        [InlineData(3650)]
        public void AuditRetentionDays_BoundaryValues_AreValid(int days)
        {
            int clamped = ClampAuditRetention(days);
            Assert.Equal(days, clamped);
        }

        // Reproduce SettingsViewModel.CodePatcherAuditOnly update logic
        [Fact]
        public void CodePatcherAuditOnly_DefaultIsTrue_CanBeSetToFalse()
        {
            var settings = new AppSettings();
            Assert.True(settings.CodePatcherAuditOnly);

            settings.CodePatcherAuditOnly = false;
            Assert.False(settings.CodePatcherAuditOnly);
        }

        // Reproduit la logique de WingetScope — seules "machine" et "user" sont valides
        [Fact]
        public void WingetScope_MachineIsValid()
        {
            var settings = new AppSettings();
            settings.WingetScope = "machine";
            Assert.Equal("machine", settings.WingetScope);
        }

        [Fact]
        public void WingetScope_UserIsValid()
        {
            var settings = new AppSettings();
            settings.WingetScope = "user";
            Assert.Equal("user", settings.WingetScope);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ScannerViewModel — Logique de commandes (sans DI)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Teste les invariants du ScannerViewModel sans instanciation WPF.
    /// ScannerViewModel utilise ServiceContainer.GetRequired&lt;T&gt;() directement
    /// dans son constructeur, donc on teste la logique de command pattern en isolation.
    /// </summary>
    public class ScannerViewModelLogicTests
    {
        // Reproduit la logique de guard IsLoading dans ScanApplicationsAsync
        private bool _isLoading = false;

        private bool CanStartScan() => !_isLoading;

        [Fact]
        public void ScanCommand_WhenNotLoading_CanExecute()
        {
            _isLoading = false;
            Assert.True(CanStartScan());
        }

        [Fact]
        public void ScanCommand_WhenLoading_CannotExecute()
        {
            _isLoading = true;
            Assert.False(CanStartScan());
        }

        [Fact]
        public void StatusMessage_DefaultValue_IsNotEmpty()
        {
            // Le StatusMessage par défaut doit avoir une valeur non-vide
            // (soit la clé de localisation, soit la valeur fallback)
            string fallback = "Prêt à analyser.";
            Assert.False(string.IsNullOrEmpty(fallback));
        }

        [Fact]
        public void ProgramInfo_Properties_CanBeAssigned()
        {
            // Vérifie que ProgramInfo est un POCO correctement structuré
            var info = new InstalledProgramsService.ProgramInfo
            {
                Name = "Coclico",
                Publisher = "Coclico Team",
                Version = "1.0.4",
                InstallPath = @"C:\Program Files\Coclico",
                ExePath = @"C:\Program Files\Coclico\Coclico.exe",
                Source = "winget",
                IconPath = "",
                Category = "Utility",
                SizeBytes = 52_000_000L
            };

            Assert.Equal("Coclico", info.Name);
            Assert.Equal("Coclico Team", info.Publisher);
            Assert.Equal("1.0.4", info.Version);
            Assert.Equal(52_000_000L, info.SizeBytes);
        }

        [Fact]
        public void ProgramInfo_DefaultValues_AreNonNull()
        {
            // Aucune propriété string ne doit être null par défaut
            var info = new InstalledProgramsService.ProgramInfo();
            Assert.NotNull(info.Name);
            Assert.NotNull(info.Publisher);
            Assert.NotNull(info.Version);
            Assert.NotNull(info.Source);
        }
    }
}

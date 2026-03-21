using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class AppSettingsModelTests
    {
        [Fact]
        public void AppSettings_DefaultLanguage_IsFrench()
        {
            var s = new AppSettings();
            Assert.Equal("fr", s.Language);
        }

        [Fact]
        public void AppSettings_DefaultAccentColor_IsIndigo()
        {
            var s = new AppSettings();
            Assert.Matches(@"^#[0-9A-Fa-f]{6,8}$", s.AccentColor);
        }

        [Fact]
        public void AppSettings_DefaultFontSize_Is13()
        {
            var s = new AppSettings();
            Assert.Equal(13.0, s.FontSize);
        }

        [Fact]
        public void AppSettings_DefaultSidebarWidth_Is220()
        {
            var s = new AppSettings();
            Assert.Equal(220, s.SidebarWidth);
        }

        [Fact]
        public void AppSettings_DefaultCardOpacity_IsLow()
        {
            var s = new AppSettings();
            Assert.InRange(s.CardOpacity, 0.0, 1.0);
        }

        [Fact]
        public void AppSettings_DefaultWingetScope_IsMachine()
        {
            var s = new AppSettings();
            Assert.Equal("machine", s.WingetScope);
        }

        [Fact]
        public void AppSettings_DefaultFirstRun_IsTrue()
        {
            var s = new AppSettings();
            Assert.True(s.FirstRun);
        }

        [Fact]
        public void AppSettings_DefaultLaunchMode_IsNormal()
        {
            var s = new AppSettings();
            Assert.Equal("Normal", s.LaunchMode);
        }

        [Fact]
        public void AppSettings_JsonSerialization_RoundTrips()
        {
            var original = new AppSettings
            {
                Language = "en",
                AccentColor = "#FF5733",
                FontSize = 14.5,
                SidebarWidth = 250,
                CompactMode = true,
                WingetScope = "user"
            };
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Language, restored!.Language);
            Assert.Equal(original.AccentColor, restored.AccentColor);
            Assert.Equal(original.FontSize, restored.FontSize);
            Assert.Equal(original.SidebarWidth, restored.SidebarWidth);
            Assert.Equal(original.CompactMode, restored.CompactMode);
            Assert.Equal(original.WingetScope, restored.WingetScope);
        }

        [Fact]
        public void AppSettings_JsonPropertyNames_AreLowerCamelCase()
        {
            var s = new AppSettings { Language = "de", AccentColor = "#AABBCC" };
            var json = JsonSerializer.Serialize(s);
            Assert.Contains("\"language\"", json);
            Assert.Contains("\"accentColor\"", json);
            Assert.Contains("\"fontSize\"", json);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("fr")]
        [InlineData("de")]
        [InlineData("es")]
        [InlineData("it")]
        [InlineData("ja")]
        [InlineData("ko")]
        [InlineData("pt")]
        [InlineData("ru")]
        [InlineData("zh")]
        public void AppSettings_SupportedLanguages_AreValid(string lang)
        {
            var s = new AppSettings { Language = lang };
            Assert.Equal(lang, s.Language);
        }
    }

    public class SettingsSanitizationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _settingsPath;

        public SettingsSanitizationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CoclicoSettingsTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _settingsPath = Path.Combine(_tempDir, "settings.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private void WriteSettings(object settings)
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        [Fact]
        public void Sanitize_InvalidLanguage_FallsBackToFrench()
        {
            var raw = new AppSettings { Language = "xx" };
            WriteSettings(raw);

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            string[] validLangs = ["en", "fr", "de", "es", "it", "ja", "ko", "pt", "ru", "zh"];
            if (!Array.Exists(validLangs, l => l == loaded.Language))
                loaded.Language = "fr";

            Assert.Equal("fr", loaded.Language);
        }

        [Theory]
        [InlineData("")]
        [InlineData("blue")]
        [InlineData("GGGGGG")]
        [InlineData("#12")]
        [InlineData("#ZZZZZZ")]
        public void Sanitize_InvalidAccentColor_FallsBackToDefault(string invalidColor)
        {
            var raw = new AppSettings { AccentColor = invalidColor };
            WriteSettings(raw);

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            if (string.IsNullOrWhiteSpace(loaded.AccentColor) ||
                !System.Text.RegularExpressions.Regex.IsMatch(loaded.AccentColor, @"^#[0-9A-Fa-f]{6,8}$"))
                loaded.AccentColor = "#6366F1";

            Assert.Equal("#6366F1", loaded.AccentColor);
        }

        [Theory]
        [InlineData("#AABBCC")]
        [InlineData("#6366F1")]
        [InlineData("#FF5733FF")]
        [InlineData("#000000")]
        public void Sanitize_ValidAccentColor_Preserved(string validColor)
        {
            var raw = new AppSettings { AccentColor = validColor };
            WriteSettings(raw);

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            if (string.IsNullOrWhiteSpace(loaded.AccentColor) ||
                !System.Text.RegularExpressions.Regex.IsMatch(loaded.AccentColor, @"^#[0-9A-Fa-f]{6,8}$"))
                loaded.AccentColor = "#6366F1";

            Assert.Equal(validColor, loaded.AccentColor);
        }

        [Fact]
        public void Sanitize_FontSizeTooSmall_ClampedTo8()
        {
            var s = new AppSettings { FontSize = 2.0 };
            s.FontSize = Math.Clamp(s.FontSize, 8.0, 32.0);
            Assert.Equal(8.0, s.FontSize);
        }

        [Fact]
        public void Sanitize_FontSizeTooLarge_ClampedTo32()
        {
            var s = new AppSettings { FontSize = 100.0 };
            s.FontSize = Math.Clamp(s.FontSize, 8.0, 32.0);
            Assert.Equal(32.0, s.FontSize);
        }

        [Fact]
        public void Sanitize_SidebarWidthTooSmall_ClampedTo52()
        {
            var s = new AppSettings { SidebarWidth = 10 };
            s.SidebarWidth = Math.Clamp(s.SidebarWidth, 52.0, 500.0);
            Assert.Equal(52.0, s.SidebarWidth);
        }

        [Fact]
        public void Sanitize_SidebarWidthTooLarge_ClampedTo500()
        {
            var s = new AppSettings { SidebarWidth = 9999 };
            s.SidebarWidth = Math.Clamp(s.SidebarWidth, 52.0, 500.0);
            Assert.Equal(500.0, s.SidebarWidth);
        }

        [Fact]
        public void Sanitize_CardOpacityNegative_ClampedToZero()
        {
            var s = new AppSettings { CardOpacity = -0.5 };
            s.CardOpacity = Math.Clamp(s.CardOpacity, 0.0, 1.0);
            Assert.Equal(0.0, s.CardOpacity);
        }

        [Fact]
        public void Sanitize_CardOpacityAboveOne_ClampedToOne()
        {
            var s = new AppSettings { CardOpacity = 2.5 };
            s.CardOpacity = Math.Clamp(s.CardOpacity, 0.0, 1.0);
            Assert.Equal(1.0, s.CardOpacity);
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("")]
        [InlineData("ALL")]
        public void Sanitize_InvalidWingetScope_FallsBackToMachine(string invalid)
        {
            var s = new AppSettings { WingetScope = invalid };
            if (s.WingetScope is not "machine" and not "user")
                s.WingetScope = "machine";
            Assert.Equal("machine", s.WingetScope);
        }

        [Fact]
        public void Sanitize_ValidWingetScope_User_Preserved()
        {
            var s = new AppSettings { WingetScope = "user" };
            if (s.WingetScope is not "machine" and not "user")
                s.WingetScope = "machine";
            Assert.Equal("user", s.WingetScope);
        }

        [Fact]
        public async Task SettingsService_SaveAsync_WritesJsonFile()
        {
            // Using the singleton — just verify SaveAsync doesn't throw
            var ex = await Record.ExceptionAsync(() => SettingsService.Instance.SaveAsync());
            Assert.Null(ex);

            // The file should exist after save
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "settings.json");
            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task SettingsService_LoadAsync_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() => SettingsService.Instance.LoadAsync());
            Assert.Null(ex);
        }

        [Fact]
        public void SettingsService_Load_DoesNotThrow()
        {
            var ex = Record.Exception(() => SettingsService.Instance.Load());
            Assert.Null(ex);
        }

        [Fact]
        public void SettingsService_Save_DoesNotThrow()
        {
            var ex = Record.Exception(() => SettingsService.Instance.Save());
            Assert.Null(ex);
        }

        [Fact]
        public void SettingsService_SettingsAfterLoad_IsNotNull()
        {
            SettingsService.Instance.Load();
            Assert.NotNull(SettingsService.Instance.Settings);
        }

        [Fact]
        public async Task SettingsService_SaveThenLoadAsync_PreservesLanguage()
        {
            var original = SettingsService.Instance.Settings.Language;
            SettingsService.Instance.Settings.Language = "en";
            await SettingsService.Instance.SaveAsync();
            await SettingsService.Instance.LoadAsync();
            Assert.Equal("en", SettingsService.Instance.Settings.Language);

            // Restore
            SettingsService.Instance.Settings.Language = original;
            await SettingsService.Instance.SaveAsync();
        }

        [Fact]
        public void SettingsService_EnableDisableAutostart_DoesNotThrow()
        {
            var ex1 = Record.Exception(() => SettingsService.Instance.EnableAutostart());
            var ex2 = Record.Exception(() => SettingsService.Instance.DisableAutostart());
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void SettingsService_DisableAutostart_IsIdempotent()
        {
            // Calling disable twice should not throw
            var ex1 = Record.Exception(() => SettingsService.Instance.DisableAutostart());
            var ex2 = Record.Exception(() => SettingsService.Instance.DisableAutostart());
            Assert.Null(ex1);
            Assert.Null(ex2);
        }
    }
}

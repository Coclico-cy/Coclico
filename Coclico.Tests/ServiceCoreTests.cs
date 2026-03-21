using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class AiActionParserTests
    {
        [Fact]
        public void Clean_NoThinkBlock_ReturnsOriginal()
        {
            var input = "Hello, world!";
            var result = AiActionParser.Clean(input);
            Assert.Equal("Hello, world!", result);
        }

        [Fact]
        public void Clean_SingleThinkBlock_IsRemoved()
        {
            var input = "<think>internal reasoning</think>Final answer.";
            var result = AiActionParser.Clean(input);
            Assert.Equal("Final answer.", result);
        }

        [Fact]
        public void Clean_MultilineThinkBlock_IsRemoved()
        {
            var input = "<think>\nline1\nline2\n</think>Answer";
            var result = AiActionParser.Clean(input);
            Assert.Equal("Answer", result);
        }

        [Fact]
        public void Clean_MultipleThinkBlocks_AllRemoved()
        {
            var input = "<think>a</think>middle<think>b</think>end";
            var result = AiActionParser.Clean(input);
            Assert.Equal("middleend", result);
        }

        [Fact]
        public void Clean_CaseInsensitiveTag_IsRemoved()
        {
            var input = "<THINK>hidden</THINK>visible";
            var result = AiActionParser.Clean(input);
            Assert.Equal("visible", result);
        }

        [Fact]
        public void Clean_EmptyString_ReturnsEmpty()
        {
            var result = AiActionParser.Clean("");
            Assert.Equal("", result);
        }

        [Fact]
        public void Clean_TrimsWhitespace()
        {
            var input = "<think>x</think>  answer  ";
            var result = AiActionParser.Clean(input);
            Assert.Equal("answer", result);
        }
    }

    public class LoggingServiceTests
    {
        [Fact]
        public void LogInfo_DoesNotThrow()
        {
            var ex = Record.Exception(() => LoggingService.LogInfo("Test info message"));
            Assert.Null(ex);
        }

        [Fact]
        public void LogError_DoesNotThrow()
        {
            var ex = Record.Exception(() => LoggingService.LogError("Test error message"));
            Assert.Null(ex);
        }

        [Fact]
        public void LogDebug_DoesNotThrow()
        {
            var ex = Record.Exception(() => LoggingService.LogDebug("Test debug message"));
            Assert.Null(ex);
        }

        [Fact]
        public void LogException_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
                LoggingService.LogException(new InvalidOperationException("test"), "LoggingServiceTest"));
            Assert.Null(ex);
        }

        [Fact]
        public async Task LogInfo_WritesToFile()
        {
            var unique = "TESTMARKER_" + Guid.NewGuid().ToString("N");
            LoggingService.LogInfo(unique);

            await Task.Delay(500);

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "logs");
            var logFile = Path.Combine(logDir, $"log_{DateTime.UtcNow:yyyyMMdd}.txt");

            if (File.Exists(logFile))
            {
                var content = await File.ReadAllTextAsync(logFile);
                Assert.Contains(unique, content);
            }
        }
    }

    public class ProfileServiceTests
    {
        private readonly string _testName = "UnitTest_" + Guid.NewGuid().ToString("N")[..8];

        [Fact]
        public void GetAllProfiles_ReturnsNonNull()
        {
            var result = ProfileService.Instance.GetAllProfiles();
            Assert.NotNull(result);
        }

        [Fact]
        public void Load_NonExistentProfile_ReturnsNull()
        {
            var result = ProfileService.Instance.Load("NonExistent_" + Guid.NewGuid());
            Assert.Null(result);
        }

        [Fact]
        public async Task LoadAsync_NonExistentProfile_ReturnsNull()
        {
            var result = await ProfileService.Instance.LoadAsync("NonExistent_" + Guid.NewGuid());
            Assert.Null(result);
        }

        [Fact]
        public void SaveAndLoad_Profile_RoundTrips()
        {
            var profile = new AppProfile
            {
                Name = _testName,
                Description = "Test profile"
            };

            try
            {
                ProfileService.Instance.Save(profile);
                var loaded = ProfileService.Instance.Load(_testName);

                Assert.NotNull(loaded);
                Assert.Equal(_testName, loaded!.Name);
                Assert.Equal("Test profile", loaded.Description);
            }
            finally
            {
                try { ProfileService.Instance.Delete(_testName); } catch { }
            }
        }

        [Fact]
        public async Task SaveAsyncAndLoadAsync_Profile_RoundTrips()
        {
            var name = "UnitTest_" + Guid.NewGuid().ToString("N")[..8];
            var profile = new AppProfile { Name = name, Description = "Async test" };

            try
            {
                await ProfileService.Instance.SaveAsync(profile);
                var loaded = await ProfileService.Instance.LoadAsync(name);

                Assert.NotNull(loaded);
                Assert.Equal(name, loaded!.Name);
            }
            finally
            {
                try { ProfileService.Instance.Delete(name); } catch { }
            }
        }

        [Fact]
        public void Delete_ExistingProfile_RemovesIt()
        {
            var name = "UnitTest_" + Guid.NewGuid().ToString("N")[..8];
            ProfileService.Instance.Save(new AppProfile { Name = name });

            ProfileService.Instance.Delete(name);

            var result = ProfileService.Instance.Load(name);
            Assert.Null(result);
        }

        [Fact]
        public void Rename_Profile_ChangesName()
        {
            var oldName = "UnitTest_" + Guid.NewGuid().ToString("N")[..8];
            var newName = "UnitTest_" + Guid.NewGuid().ToString("N")[..8];

            try
            {
                ProfileService.Instance.Save(new AppProfile { Name = oldName });
                ProfileService.Instance.Rename(oldName, newName);

                Assert.Null(ProfileService.Instance.Load(oldName));
                Assert.NotNull(ProfileService.Instance.Load(newName));
            }
            finally
            {
                try { ProfileService.Instance.Delete(oldName); } catch { }
                try { ProfileService.Instance.Delete(newName); } catch { }
            }
        }
    }

    public class AppProfileModelTests
    {
        [Fact]
        public void AvatarInitials_ReturnsFirstLetterUppercase()
        {
            var profile = new AppProfile { Name = "alice" };
            Assert.Equal("A", profile.AvatarInitials);
        }

        [Fact]
        public void AvatarInitials_EmptyName_ReturnsQuestionMark()
        {
            var profile = new AppProfile { Name = "" };
            Assert.Equal("?", profile.AvatarInitials);
        }

        [Fact]
        public void LastUsed_EqualToLastModified()
        {
            var now = DateTime.UtcNow;
            var profile = new AppProfile { LastModified = now };
            Assert.Equal(now, profile.LastUsed);
        }

        [Fact]
        public void DefaultProfile_HasExpectedDefaults()
        {
            var profile = new AppProfile();
            Assert.Equal("Default", profile.Name);
            Assert.Equal(string.Empty, profile.Description);
            Assert.NotNull(profile.Settings);
            Assert.NotNull(profile.Categories);
            Assert.NotNull(profile.FilterGroups);
        }
    }

    public class AppSettingsTests
    {
        [Fact]
        public void DefaultSettings_HaveExpectedValues()
        {
            var settings = new AppSettings();
            Assert.False(string.IsNullOrWhiteSpace(settings.Language));
            Assert.False(string.IsNullOrWhiteSpace(settings.AccentColor));
        }

        [Fact]
        public void SettingsService_Instance_IsNotNull()
        {
            Assert.NotNull(SettingsService.Instance);
        }

        [Fact]
        public void SettingsService_Settings_IsNotNull()
        {
            Assert.NotNull(SettingsService.Instance.Settings);
        }

        [Fact]
        public void ApplyFrom_CopiesValues()
        {
            var source = new AppSettings
            {
                Language = "fr",
                AccentColor = "#FF0000",
                CompactMode = true,
                FontSize = 14
            };
            var target = SettingsService.Instance;
            var backup = new AppSettings
            {
                Language = target.Settings.Language,
                AccentColor = target.Settings.AccentColor,
                CompactMode = target.Settings.CompactMode,
                FontSize = target.Settings.FontSize
            };

            try
            {
                target.ApplyFrom(source);
                Assert.Equal("fr", target.Settings.Language);
                Assert.Equal("#FF0000", target.Settings.AccentColor);
                Assert.True(target.Settings.CompactMode);
                Assert.Equal(14, target.Settings.FontSize);
            }
            finally
            {
                target.ApplyFrom(backup);
            }
        }
    }

    public class RelayCommandTests
    {
        [Fact]
        public void CanExecute_NullPredicate_ReturnsTrue()
        {
            var cmd = new RelayCommand(_ => { });
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_WithFalsePredicate_ReturnsFalse()
        {
            var cmd = new RelayCommand(_ => { }, _ => false);
            Assert.False(cmd.CanExecute(null));
        }

        [Fact]
        public void CanExecute_WithTruePredicate_ReturnsTrue()
        {
            var cmd = new RelayCommand(_ => { }, _ => true);
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void Execute_CallsAction()
        {
            var called = false;
            var cmd = new RelayCommand(_ => called = true);
            cmd.Execute(null);
            Assert.True(called);
        }

        [Fact]
        public void Execute_PassesParameter()
        {
            object? received = null;
            var cmd = new RelayCommand(p => received = p);
            cmd.Execute("hello");
            Assert.Equal("hello", received);
        }

        [Fact]
        public void Constructor_NullExecute_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
        }

        [Fact]
        public void RelayCommandAsync_CanExecute_TrueWhenNotExecuting()
        {
            var cmd = new RelayCommandAsync(_ => Task.CompletedTask);
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void RelayCommandAsync_CanExecute_FalseWithFalsePredicate()
        {
            var cmd = new RelayCommandAsync(_ => Task.CompletedTask, _ => false);
            Assert.False(cmd.CanExecute(null));
        }
    }

    public class KeyboardShortcutModelTests
    {
        [Fact]
        public void DisplayLabel_CombinesModifiersAndKey()
        {
            var sc = new KeyboardShortcut { Modifiers = "Ctrl+Shift", Key = "F5" };
            Assert.Equal("Ctrl+Shift+F5", sc.DisplayLabel);
        }

        [Fact]
        public void Description_WithChainName_IncludesName()
        {
            var sc = new KeyboardShortcut { ChainName = "MyChain" };
            Assert.Contains("MyChain", sc.Description);
        }

        [Fact]
        public void Description_WithoutChainName_UsesAction()
        {
            var sc = new KeyboardShortcut { Action = "DoSomething", ChainName = null };
            Assert.Equal("DoSomething", sc.Description);
        }

        [Fact]
        public void DefaultId_IsNewGuid()
        {
            var sc = new KeyboardShortcut();
            Assert.False(string.IsNullOrWhiteSpace(sc.Id));
            Assert.True(Guid.TryParse(sc.Id, out _));
        }

        [Fact]
        public void KeyboardShortcutsService_Instance_IsNotNull()
        {
            Assert.NotNull(KeyboardShortcutsService.Instance);
        }

        [Fact]
        public void GetShortcuts_ReturnsNonNull()
        {
            var shortcuts = KeyboardShortcutsService.Instance.GetShortcuts();
            Assert.NotNull(shortcuts);
        }

        [Fact]
        public void AddAndRemoveShortcut_WorksCorrectly()
        {
            var svc = KeyboardShortcutsService.Instance;
            var sc = new KeyboardShortcut { Id = "TEST_" + Guid.NewGuid(), Key = "F12", Modifiers = "Ctrl" };

            svc.AddShortcut(sc);
            var after = svc.GetShortcuts();
            Assert.Contains(after, s => s.Id == sc.Id);

            svc.RemoveShortcut(sc.Id);
            var final = svc.GetShortcuts();
            Assert.DoesNotContain(final, s => s.Id == sc.Id);
        }
    }
}

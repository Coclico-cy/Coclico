using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Coclico.Services;
using Coclico.ViewModels;
using Xunit;

namespace Coclico.Tests
{

    public class UserAccountServiceTests
    {
        [Fact]
        public void Constructor_DoesNotThrow()
        {
            var ex = Record.Exception(() => new UserAccountService());
            Assert.Null(ex);
        }

        [Fact]
        public void UserName_IsNotNullOrEmpty()
        {
            var svc = new UserAccountService();
            Assert.False(string.IsNullOrEmpty(svc.UserName));
        }

        [Fact]
        public void DisplayName_IsNotNullOrEmpty()
        {
            var svc = new UserAccountService();
            Assert.False(string.IsNullOrEmpty(svc.DisplayName));
        }

        [Fact]
        public void Email_IsNotNullOrEmpty()
        {
            var svc = new UserAccountService();
            Assert.False(string.IsNullOrEmpty(svc.Email));
        }

        [Fact]
        public void Email_ContainsAtSign()
        {
            var svc = new UserAccountService();
            Assert.Contains("@", svc.Email);
        }

        [Fact]
        public void UserName_DoesNotContainBackslash()
        {
            var svc = new UserAccountService();
            Assert.DoesNotContain("\\", svc.UserName);
        }

        [Fact]
        public void LoadUserData_CalledTwice_DoesNotThrow()
        {
            var svc = new UserAccountService();
            var ex = Record.Exception(() => svc.LoadUserData());
            Assert.Null(ex);
        }

        [Fact]
        public void IsMicrosoftAccount_AccessDoesNotThrow()
        {
            var svc = new UserAccountService();
            var ex = Record.Exception(() => { _ = svc.IsMicrosoftAccount; });
            Assert.Null(ex);
        }

        [Fact]
        public void SetCustomAvatar_NonExistentFile_ThrowsFileNotFoundException()
        {
            var svc = new UserAccountService();
            Assert.Throws<FileNotFoundException>(() =>
                svc.SetCustomAvatar(@"C:\nonexistent_avatar_coclico_test_99999.png"));
        }

        [Fact]
        public void LocalAccount_Email_ContainsWindowsLocal()
        {
            var svc = new UserAccountService();
            if (!svc.IsMicrosoftAccount)
                Assert.EndsWith("@windows.local", svc.Email);
        }

        [Fact]
        public void Avatar_Property_AccessDoesNotThrow()
        {
            var svc = new UserAccountService();
            var ex = Record.Exception(() => { _ = svc.Avatar; });
            Assert.Null(ex);
        }
    }

    public class AppResourceGuardServiceTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(AppResourceGuardService.Instance);
        }

        [Fact]
        public void Instance_IsSingleton()
        {
            var a = AppResourceGuardService.Instance;
            var b = AppResourceGuardService.Instance;
            Assert.Same(a, b);
        }

        [Fact]
        public void Stop_WithoutStart_DoesNotThrow()
        {
            var ex = Record.Exception(() => AppResourceGuardService.Instance.Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void CurrentPressure_WithoutStart_IsValidEnum()
        {
            var pressure = AppResourceGuardService.Instance.CurrentPressure;
            Assert.True(Enum.IsDefined(typeof(AppResourceGuardService.PressureLevel), pressure));
        }

        [Fact]
        public void AppCpuPercent_WithoutStart_IsNonNegative()
        {
            Assert.True(AppResourceGuardService.Instance.AppCpuPercent >= 0.0);
        }

        [Fact]
        public void AppWorkingSetMb_WithoutStart_IsNonNegative()
        {
            Assert.True(AppResourceGuardService.Instance.AppWorkingSetMb >= 0);
        }

        [Fact]
        public void AppPrivateMb_WithoutStart_IsNonNegative()
        {
            Assert.True(AppResourceGuardService.Instance.AppPrivateMb >= 0);
        }

        [Fact]
        public void PressureChanged_SubscriptionAndUnsubscription_DoesNotThrow()
        {
            var guard = AppResourceGuardService.Instance;
            void Handler(AppResourceGuardService.PressureLevel _) { }
            var ex1 = Record.Exception(() => guard.PressureChanged += Handler);
            var ex2 = Record.Exception(() => guard.PressureChanged -= Handler);
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void GuardMessage_SubscriptionAndUnsubscription_DoesNotThrow()
        {
            var guard = AppResourceGuardService.Instance;
            void Handler(string _) { }
            var ex1 = Record.Exception(() => guard.GuardMessage += Handler);
            var ex2 = Record.Exception(() => guard.GuardMessage -= Handler);
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void PressureLevel_Enum_HasExpectedValues()
        {
            Assert.True(Enum.IsDefined(typeof(AppResourceGuardService.PressureLevel), AppResourceGuardService.PressureLevel.Normal));
            Assert.True(Enum.IsDefined(typeof(AppResourceGuardService.PressureLevel), AppResourceGuardService.PressureLevel.Elevated));
            Assert.True(Enum.IsDefined(typeof(AppResourceGuardService.PressureLevel), AppResourceGuardService.PressureLevel.High));
            Assert.True(Enum.IsDefined(typeof(AppResourceGuardService.PressureLevel), AppResourceGuardService.PressureLevel.Critical));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FlowExecutionService
    // ─────────────────────────────────────────────────────────────────────────

    public class FlowExecutionServiceTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(FlowExecutionService.Instance);
        }

        [Fact]
        public void Instance_IsSingleton()
        {
            var a = FlowExecutionService.Instance;
            var b = FlowExecutionService.Instance;
            Assert.Same(a, b);
        }

        [Fact]
        public void SetConnections_Null_DoesNotThrow()
        {
            var ex = Record.Exception(() => FlowExecutionService.Instance.SetConnections(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void SetConnections_EmptyCollection_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
                FlowExecutionService.Instance.SetConnections(
                    new ObservableCollection<FlowViewModel.FlowConnection>()));
            Assert.Null(ex);
        }

        [Fact]
        public void SetConnections_WithConnections_DoesNotThrow()
        {
            var connections = new ObservableCollection<FlowViewModel.FlowConnection>();
            var ex = Record.Exception(() => FlowExecutionService.Instance.SetConnections(connections));
            Assert.Null(ex);
        }
    }
}

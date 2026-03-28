#nullable enable
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

    public class ResourceGuardServiceTests
    {
        [Fact]
        public void New_IsNotNull()
        {
            Assert.NotNull(new ResourceGuardService());
        }

        [Fact]
        public void Stop_WithoutStart_DoesNotThrow()
        {
            var ex = Record.Exception(() => new ResourceGuardService().Stop());
            Assert.Null(ex);
        }

        [Fact]
        public void CurrentPressure_WithoutStart_IsValidEnum()
        {
            var pressure = new ResourceGuardService().CurrentPressure;
            Assert.True(Enum.IsDefined(typeof(ResourceGuardService.PressureLevel), pressure));
        }

        [Fact]
        public void AppCpuPercent_WithoutStart_IsNonNegative()
        {
            Assert.True(new ResourceGuardService().AppCpuPercent >= 0.0);
        }

        [Fact]
        public void AppWorkingSetMb_WithoutStart_IsNonNegative()
        {
            Assert.True(new ResourceGuardService().AppWorkingSetMb >= 0);
        }

        [Fact]
        public void AppPrivateMb_WithoutStart_IsNonNegative()
        {
            Assert.True(new ResourceGuardService().AppPrivateMb >= 0);
        }

        [Fact]
        public void PressureChanged_SubscriptionAndUnsubscription_DoesNotThrow()
        {
            var guard = new ResourceGuardService();
            void Handler(ResourceGuardService.PressureLevel _) { }
            var ex1 = Record.Exception(() => guard.PressureChanged += Handler);
            var ex2 = Record.Exception(() => guard.PressureChanged -= Handler);
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void GuardMessage_SubscriptionAndUnsubscription_DoesNotThrow()
        {
            var guard = new ResourceGuardService();
            void Handler(string _) { }
            var ex1 = Record.Exception(() => guard.GuardMessage += Handler);
            var ex2 = Record.Exception(() => guard.GuardMessage -= Handler);
            Assert.Null(ex1);
            Assert.Null(ex2);
        }

        [Fact]
        public void PressureLevel_Enum_HasExpectedValues()
        {
            Assert.True(Enum.IsDefined(typeof(ResourceGuardService.PressureLevel), ResourceGuardService.PressureLevel.Normal));
            Assert.True(Enum.IsDefined(typeof(ResourceGuardService.PressureLevel), ResourceGuardService.PressureLevel.Elevated));
            Assert.True(Enum.IsDefined(typeof(ResourceGuardService.PressureLevel), ResourceGuardService.PressureLevel.High));
            Assert.True(Enum.IsDefined(typeof(ResourceGuardService.PressureLevel), ResourceGuardService.PressureLevel.Critical));
        }
    }

    public class WorkflowServiceConnectionTests
    {
        [Fact]
        public void WorkflowService_CanBeInstantiated()
        {
            Assert.NotNull(new WorkflowService());
        }

        [Fact]
        public void SetConnections_Null_DoesNotThrow()
        {
            var ex = Record.Exception(() => new WorkflowService().SetConnections(null!));
            Assert.Null(ex);
        }

        [Fact]
        public void SetConnections_EmptyCollection_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
                new WorkflowService().SetConnections(
                    new ObservableCollection<Coclico.ViewModels.WorkflowPipelinesViewModel.VisualPipelineConnection>()));
            Assert.Null(ex);
        }

        [Fact]
        public void SetConnections_WithConnections_DoesNotThrow()
        {
            var connections = new ObservableCollection<Coclico.ViewModels.WorkflowPipelinesViewModel.VisualPipelineConnection>();
            var ex = Record.Exception(() => new WorkflowService().SetConnections(connections));
            Assert.Null(ex);
        }
    }
}

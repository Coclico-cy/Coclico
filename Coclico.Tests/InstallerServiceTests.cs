#nullable enable
using System;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class InstallerServiceTests
    {
        [Fact]
        public void GetAvailableSoftware_ReturnsNonEmpty()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.NotNull(items);
            Assert.NotEmpty(items);
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveName()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveWingetId()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.WingetId)));
        }

        [Fact]
        public void GetAvailableSoftware_AllItemsHaveCategory()
        {
            var svc = new InstallerService();
            var items = svc.GetAvailableSoftware();

            Assert.All(items, item =>
                Assert.False(string.IsNullOrWhiteSpace(item.Category)));
        }

        [Fact]
        public void IsRunAsAdmin_ReturnsBoolWithoutThrowing()
        {
            var svc = new InstallerService();
            var ex = Record.Exception(() => svc.IsRunAsAdmin());
            Assert.Null(ex);
        }

        [Fact]
        public void SoftwareItem_DefaultStatusIsNonEmpty()
        {
            var item = new InstallerService.SoftwareItem { Name = "Test", WingetId = "test.id" };
            Assert.False(string.IsNullOrWhiteSpace(item.Status));
            Assert.Equal(0, item.ProgressValue);
        }
    }
}

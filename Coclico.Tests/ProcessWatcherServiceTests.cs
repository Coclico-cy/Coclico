#nullable enable
using System;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests
{
    public class ProcessWatcherServiceTests
    {
        [Fact]
        public void CanBeInstantiated()
        {
            Assert.NotNull(new ProcessWatcherService());
        }

        [Fact]
        public void KillProcess_NonExistentProcess_DoesNotThrow()
        {
            var ex = Record.Exception(() =>
                new ProcessWatcherService().KillProcess("this_process_does_not_exist_xyz"));
            Assert.Null(ex);
        }
    }
}

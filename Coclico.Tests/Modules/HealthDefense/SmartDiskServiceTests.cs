using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.HealthDefense;

public sealed class SmartDiskServiceTests
{
    [Fact]
    public void GetDiskStatus_NeverThrows()
    {
        Exception? exception = Record.Exception(() => _ = SmartDiskService.GetDiskStatus());

        Assert.Null(exception);
    }

    [Fact]
    public void GetDiskStatus_ReturnsNonNullList()
    {
        List<SmartDiskStatus> status = SmartDiskService.GetDiskStatus();

        Assert.NotNull(status);
    }
}

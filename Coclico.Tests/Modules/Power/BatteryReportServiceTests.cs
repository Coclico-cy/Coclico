using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.Power;

public sealed class BatteryReportServiceTests
{
    [Fact]
    public async Task GenerateReportAsync_NeverThrows()
    {
        Exception? exception = await Record.ExceptionAsync(GenerateAsync);

        Assert.Null(exception);

        static async Task GenerateAsync()
        {
            string? reportPath = await BatteryReportService.GenerateReportAsync();
            if (reportPath != null)
            {

                Assert.True(File.Exists(reportPath));
            }
        }
    }

    [Fact]
    public void OpenReport_NonexistentPath_DoesNotThrow()
    {
        string nonexistent = Path.Join(Path.GetTempPath(), "coclico-inexistant-" + Guid.NewGuid().ToString("N") + ".html");

        Exception? exception = Record.Exception(() => BatteryReportService.OpenReport(nonexistent));

        Assert.Null(exception);
    }
}

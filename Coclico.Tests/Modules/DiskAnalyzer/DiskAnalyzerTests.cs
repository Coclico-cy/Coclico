using Coclico.Modules.DiskAnalyzer.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class DiskAnalyzerTests
{
    [Fact]
    public async Task ScanAsync_ComputesSizesAndHierarchyCorrectly()
    {
        string dir = Path.Combine(Path.GetTempPath(), "coclico-disk-test-" + Guid.NewGuid().ToString("N")[..8]);
        string sub1 = Path.Combine(dir, "Sub1");
        string sub2 = Path.Combine(sub1, "Sub2");
        _ = Directory.CreateDirectory(sub2);
        await File.WriteAllBytesAsync(Path.Combine(dir, "a.bin"), new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(sub1, "b.bin"), new byte[2000]);
        await File.WriteAllBytesAsync(Path.Combine(sub2, "c.bin"), new byte[4000]);

        try
        {
            DiskItemNode node = await DiskAnalyzerService.ScanAsync(dir, null);

            Assert.Equal(7000, node.SizeBytes);
            Assert.Equal(3, node.FileCount);
            Assert.Single(node.Children);

            DiskItemNode sub1Node = node.Children[0];
            Assert.Equal("Sub1", sub1Node.Name);
            Assert.Equal(6000, sub1Node.SizeBytes);
            Assert.Equal(2, sub1Node.FileCount);
            Assert.Single(sub1Node.Children);

            DiskItemNode sub2Node = sub1Node.Children[0];
            Assert.Equal(4000, sub2Node.SizeBytes);
            Assert.Empty(sub2Node.Children);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch { }
        }
    }

    [Fact]
    public void FormatSize_UsesHumanReadableUnits()
    {
        Assert.Equal("512 o", DiskItemNode.FormatSize(512));
        Assert.Equal("1 Ko", DiskItemNode.FormatSize(1024));
        Assert.Equal("1,5 Mo", DiskItemNode.FormatSize(1536 * 1024));
    }

    [Fact]
    public void DiskAnalyzerView_InstantiatesWithoutXamlErrors()
    {
        Coclico.Modules.DiskAnalyzer.Views.DiskAnalyzerView? view = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                view = new Coclico.Modules.DiskAnalyzer.Views.DiskAnalyzerView();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.NotNull(view);
    }
}

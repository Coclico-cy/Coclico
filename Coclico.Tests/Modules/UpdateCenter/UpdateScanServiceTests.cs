using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

public sealed class UpdateScanServiceTests
{
    private static UpdateItem Item(UpdateKind kind) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = kind.ToString(),
        Kind = kind,
        Source = UpdateSource.Winget,
        Status = UpdateItemStatus.Available
    };

    private sealed class StubWingetService : WingetUpdateService
    {
        public bool Fail { get; set; }

        public StubWingetService() : base(new SecureProcessRunner()) { }

        public override Task<IReadOnlyList<UpdateItem>> ScanAsync(CancellationToken ct)
            => Fail
                ? throw new InvalidOperationException("winget unavailable")
                : Task.FromResult<IReadOnlyList<UpdateItem>>(new[] { Item(UpdateKind.Application) });
    }

    private sealed class StubDriverService : DriverUpdateService
    {
        public override Task<IReadOnlyList<UpdateItem>> SearchWindowsUpdateDriverUpdatesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UpdateItem>>(new[] { Item(UpdateKind.Driver) });
    }

    private sealed class StubSystemService : SystemUpdateService
    {
        public StubSystemService() : base(new SecureProcessRunner()) { }

        public override Task<UpdateItem> GetWindowsUpdateStatusAsync(CancellationToken ct)
            => Task.FromResult(UpdateItem.UpToDate("windows", "Windows Update", UpdateKind.Windows, UpdateSource.WindowsUpdate));
    }

    [Fact]
    public async Task ScanAllAsync_PartialFailure_StillReturnsOtherSources()
    {
        var service = new UpdateScanService(
            new StubWingetService { Fail = true },
            new StubDriverService(),
            new StubSystemService());

        var result = await service.ScanAllAsync(CancellationToken.None);

        Assert.Contains(result.Items, i => i.Kind == UpdateKind.Driver);
        Assert.Contains(result.Items, i => i.Kind == UpdateKind.Windows);
        Assert.True(result.HasFailures);
        Assert.Contains("winget unavailable", result.SourceErrors["Application"]);
    }

    [Fact]
    public async Task ScanAllAsync_AllSources_ReturnsSortedItems()
    {
        var service = new UpdateScanService(
            new StubWingetService(),
            new StubDriverService(),
            new StubSystemService());

        var result = await service.ScanAllAsync(CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.AvailableCount);
        Assert.False(result.HasFailures);
        Assert.True(result.ScannedAt <= DateTimeOffset.UtcNow);
    }
}

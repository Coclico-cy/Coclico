using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;
using Coclico.Modules.UpdateCenter.ViewModels;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

public sealed class UpdateCenterViewModelTests
{
    private sealed class StubScanService : UpdateScanService
    {
        public IReadOnlyList<UpdateItem> Result { get; set; } = Array.Empty<UpdateItem>();

        public StubScanService() : base(null!, null!, null!) { }

        public override Task<UpdateScanResult> ScanAllAsync(CancellationToken ct)
            => Task.FromResult(new UpdateScanResult
            {
                Items = Result,
                ScannedAt = DateTimeOffset.UtcNow,
                SourceErrors = new Dictionary<string, string>()
            });
    }

    private sealed class StubWingetService : WingetUpdateService
    {
        public int UpgradeCalls { get; private set; }

        public StubWingetService() : base(new SecureProcessRunner()) { }

        public override Task<bool> UpgradeAsync(string packageId, CancellationToken ct)
        {
            UpgradeCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class StubDriverService : DriverUpdateService
    {
        public int InstallCalls { get; private set; }

        public override Task<bool> InstallAsync(UpdateItem item, CancellationToken ct)
        {
            InstallCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class StubRestorePointService : RestorePointService
    {
        public int RestorePointCalls { get; private set; }

        public override bool Create(string description)
        {
            RestorePointCalls++;
            return true;
        }
    }

    private static UpdateItem AppItem(string id, UpdateItemStatus status = UpdateItemStatus.Available) => new()
    {
        Id = id,
        Name = id,
        Kind = UpdateKind.Application,
        Source = UpdateSource.Winget,
        Status = status,
        CurrentVersion = "1.0",
        AvailableVersion = "2.0"
    };

    private static UpdateItem DriverItem(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Kind = UpdateKind.Driver,
        Source = UpdateSource.WindowsUpdate,
        Status = UpdateItemStatus.Available
    };

    private static UpdateCenterViewModel CreateViewModel(
        StubScanService scanService,
        StubWingetService? winget = null,
        StubDriverService? drivers = null,
        StubRestorePointService? restore = null)
        => new(scanService, winget ?? new StubWingetService(), drivers ?? new StubDriverService(), restore ?? new StubRestorePointService());

    [Fact]
    public async Task CheckAll_FillsSections_AndBecomesReady()
    {
        var scanService = new StubScanService
        {
            Result = new[] { AppItem("App1"), AppItem("App2", UpdateItemStatus.UnknownVersion) }
        };

        var viewModel = CreateViewModel(scanService);
        viewModel.CheckAll();

        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        Assert.Equal(UpdateCenterState.Ready, viewModel.State);
        Assert.Equal(2, viewModel.Applications.Items.Count);
        Assert.NotNull(viewModel.LastScan);
    }

    [Fact]
    public async Task Selection_OnlySelectableItemsCanBeChecked()
    {
        var scanService = new StubScanService
        {
            Result = new[]
            {
                AppItem("App1"),
                UpdateItem.UpToDate("App2", "App2", UpdateKind.Application, UpdateSource.Winget)
            }
        };

        var viewModel = CreateViewModel(scanService);
        viewModel.CheckAll();
        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        var upToDate = viewModel.Applications.Items.Single(i => i.Id == "App2");
        upToDate.IsSelected = true;

        Assert.False(upToDate.IsSelected);
        Assert.Empty(viewModel.SelectedItems);
    }

    [Fact]
    public async Task UpdateSelected_InstallsDrivers_AndCreatesRestorePointFirst()
    {
        var scanService = new StubScanService
        {
            Result = new[]
            {
                DriverItem("d1", "NVIDIA GeForce Driver"),
                DriverItem("d2", "Intel Audio Driver")
            }
        };

        var drivers = new StubDriverService();
        var restore = new StubRestorePointService();
        var viewModel = CreateViewModel(scanService, drivers: drivers, restore: restore);

        viewModel.CheckAll();
        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        foreach (var item in viewModel.Drivers.Items)
            item.IsSelected = true;

        await viewModel.UpdateSelectedAsync();

        Assert.Equal(2, drivers.InstallCalls);
        Assert.Equal(1, restore.RestorePointCalls);
        Assert.Contains("2 élément(s) mis à jour", viewModel.LastScanSummary);
    }

    [Fact]
    public async Task UpdateSelected_UpdatesAppsAndDriversTogether()
    {
        var scanService = new StubScanService
        {
            Result = new[]
            {
                AppItem("Mozilla.Firefox"),
                DriverItem("d1", "AMD Graphics Driver")
            }
        };

        var winget = new StubWingetService();
        var drivers = new StubDriverService();
        var viewModel = CreateViewModel(scanService, winget: winget, drivers: drivers);

        viewModel.CheckAll();
        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        foreach (var section in viewModel.Sections)
            foreach (var item in section.Items)
                item.IsSelected = true;

        await viewModel.UpdateSelectedAsync();

        Assert.Equal(1, winget.UpgradeCalls);
        Assert.Equal(1, drivers.InstallCalls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}

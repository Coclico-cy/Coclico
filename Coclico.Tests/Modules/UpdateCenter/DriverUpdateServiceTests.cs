using System;
using System.Collections.Generic;
using System.Linq;
using Coclico.Modules.UpdateCenter.Models;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

public sealed class DriverUpdateServiceTests
{
    [Fact]
    public void DriverInfo_AgeMonths_NeverNegative()
    {
        var driver = new DriverInfo
        {
            DeviceId = "PCI\\VEN_10DE",
            DeviceName = "GPU",
            DriverDate = DateTime.UtcNow.AddDays(30)
        };

        Assert.Equal(0, driver.AgeMonths);
    }

    [Fact]
    public void DriverInfo_AgeMonths_ComputedFromDriverDate()
    {
        var driver = new DriverInfo
        {
            DeviceId = "PCI\\VEN_8086",
            DeviceName = "Ethernet",
            DriverDate = DateTime.UtcNow.AddMonths(-24)
        };

        Assert.InRange(driver.AgeMonths, 23, 25);
    }

    [Fact]
    public void Inventory_Filtering_RemovesInvalidEntries()
    {
        var inventory = new List<DriverInfo>
        {
            new() { DeviceId = "A", DeviceName = "Valid" },
            new() { DeviceId = "", DeviceName = "NoDeviceId" },
            new() { DeviceId = "B", DeviceName = "" }
        };

        var valid = inventory.Where(d => !string.IsNullOrWhiteSpace(d.DeviceId) && !string.IsNullOrWhiteSpace(d.DeviceName)).ToList();

        Assert.Single(valid);
        Assert.Equal("A", valid[0].DeviceId);
    }
}

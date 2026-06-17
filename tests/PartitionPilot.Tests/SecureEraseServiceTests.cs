namespace PartitionPilot.Tests;

public class SecureEraseServiceTests
{
    [Fact]
    public void CanSanitizeDisk_RejectsUnknownPhysicalDisk()
    {
        var disk = new DiskInfo { Number = 3, FriendlyName = "Unknown", Size = 1000 };

        var allowed = SecureEraseService.CanSanitizeDisk(disk, [], osSupportsNvmeSanitize: true, out var reason);

        Assert.False(allowed);
        Assert.Contains("Could not verify", reason);
    }

    [Fact]
    public void CanSanitizeDisk_RejectsNonNvmeDisk()
    {
        var disk = new DiskInfo { Number = 1, FriendlyName = "SATA SSD", Size = 1000 };
        var physical = new[]
        {
            new PhysicalDiskInfo { DeviceId = "1", FriendlyName = "SATA SSD", Size = 1000, BusType = "SATA" }
        };

        var allowed = SecureEraseService.CanSanitizeDisk(disk, physical, osSupportsNvmeSanitize: true, out var reason);

        Assert.False(allowed);
        Assert.Contains("not NVMe", reason);
    }

    [Fact]
    public void CanSanitizeDisk_AllowsMatchingNvmeDisk()
    {
        var disk = new DiskInfo { Number = 2, FriendlyName = "NVMe SSD", Size = 2000 };
        var physical = new[]
        {
            new PhysicalDiskInfo { DeviceId = "2", FriendlyName = "NVMe SSD", Size = 2000, BusType = "NVMe" }
        };

        var allowed = SecureEraseService.CanSanitizeDisk(disk, physical, osSupportsNvmeSanitize: true, out var reason);

        Assert.True(allowed);
        Assert.Contains("NVMe", reason);
    }
}

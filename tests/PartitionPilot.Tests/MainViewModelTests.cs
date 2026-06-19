namespace PartitionPilot.Tests;

public class MainViewModelTests
{
    [Fact]
    public void VersionText_ComesFromAssemblyMetadata()
    {
        Assert.Equal($"PartitionPilot v{UpdateService.GetCurrentVersion()}", MainViewModel.GetVersionText());
    }

    [Fact]
    public void RedactSupportBundleText_RemovesUserPathsAndSerials()
    {
        var text = """
            Mounting image C:\Users\Alice Doe\Desktop\customer image.vhdx
            Disk scan completed. Model: Samsung, Serial: S5H7NX0R123456B
            {"SerialNumber":"ABC123","Path":"D:\\Customer Data\\disk.wim"}
            """;

        var redacted = MainViewModel.RedactSupportBundleText(text);

        Assert.DoesNotContain("Alice", redacted);
        Assert.DoesNotContain("customer image", redacted);
        Assert.DoesNotContain("S5H7NX0R123456B", redacted);
        Assert.DoesNotContain("ABC123", redacted);
        Assert.Contains("Serial: [redacted]", redacted);
        Assert.Contains("\"SerialNumber\":\"[redacted]\"", redacted);
    }
}

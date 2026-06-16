namespace PartitionPilot;

public class FolderSizeInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public double Proportion { get; set; }

    public string SizeText => SizeUtil.Format(Size);
}

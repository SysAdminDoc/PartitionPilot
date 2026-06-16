namespace PartitionPilot;

public class MountedImageInfo
{
    public string ImagePath { get; set; } = "";
    public string StorageType { get; set; } = ""; // "ISO", "VHD", "VHDX"
    public long Size { get; set; }
    public char? DriveLetter { get; set; }

    public string SizeText => SizeUtil.Format(Size);
    public string LetterDisplay => DriveLetter.HasValue ? $"{DriveLetter}:" : "—";
}

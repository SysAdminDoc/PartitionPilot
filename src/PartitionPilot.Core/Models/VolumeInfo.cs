namespace PartitionPilot;

public class VolumeInfo
{
    public char? DriveLetter { get; set; }
    public string FileSystemLabel { get; set; } = "";
    public string FileSystemType { get; set; } = "";
    public long Size { get; set; }
    public long SizeRemaining { get; set; }
    public string DriveType { get; set; } = "";
    public string EncryptionStatus { get; set; } = "";

    public string LetterDisplay => DriveLetter.HasValue ? $"{DriveLetter}:" : "No letter";

    public string DisplayText
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(FileSystemLabel) ? "Local volume" : FileSystemLabel;
            var size = Size > 0 ? SizeUtil.Format(Size) : "unknown size";
            var free = SizeRemaining > 0 ? $"{SizeUtil.Format(SizeRemaining)} free" : "free space unknown";
            var encryption = string.IsNullOrWhiteSpace(EncryptionStatus) ? "" : $", {EncryptionStatus}";
            return $"{LetterDisplay} {label} ({free} of {size}, {FileSystemType}{encryption})";
        }
    }
}

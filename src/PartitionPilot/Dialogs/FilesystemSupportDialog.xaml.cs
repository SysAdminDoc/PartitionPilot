using System.Windows;

namespace PartitionPilot.Dialogs;

public partial class FilesystemSupportDialog : Window
{
    public FilesystemSupportDialog()
    {
        InitializeComponent();
        matrixGrid.ItemsSource = GetSupportMatrix();
    }

    private static List<FsSupport> GetSupportMatrix() =>
    [
        new("NTFS",   "Yes", "Yes", "Yes", "Yes", "Yes", "Yes", "Default Windows filesystem"),
        new("FAT32",  "Yes", "Yes", "No",  "No",  "Yes", "Yes", "Windows limits format to 32 GB via diskpart"),
        new("exFAT",  "Yes", "Yes", "No",  "No",  "No",  "Yes", "No resize or check support"),
        new("ReFS",   "Yes", "Yes", "No",  "Yes", "Yes", "Yes", "Dev Drive capable (Win 11 22621+)"),
        new("FAT16",  "No",  "Yes", "No",  "No",  "Yes", "Yes", "Legacy; max 2 GB"),
        new("ext2/3/4","No", "No",  "No",  "No",  "No",  "No",  "Detected; no Windows write support"),
        new("HFS+",   "No",  "No",  "No",  "No",  "No",  "No",  "Detected; Apple filesystem"),
        new("APFS",   "No",  "No",  "No",  "No",  "No",  "No",  "Detected; Apple filesystem"),
        new("Linux Swap","No","No", "No",  "No",  "No",  "No",  "Detected; Linux only"),
        new("LUKS",   "No",  "No",  "No",  "No",  "No",  "No",  "Detected; Linux encryption"),
    ];

    private record FsSupport(string FileSystem, string Create, string Format,
        string Resize, string Extend, string Check, string Label, string Notes);
}

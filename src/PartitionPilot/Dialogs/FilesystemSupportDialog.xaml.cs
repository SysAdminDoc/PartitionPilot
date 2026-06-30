using System.Windows;
using PartitionPilot;

namespace PartitionPilot.Dialogs;

public partial class FilesystemSupportDialog : Window
{
    public FilesystemSupportDialog()
    {
        InitializeComponent();
        matrixGrid.ItemsSource = GetSupportMatrix();
    }

    private static List<FsSupport> GetSupportMatrix() =>
        FilesystemCapabilityService.GetMatrix()
            .Select(c => new FsSupport(
                c.FileSystem,
                YesNo(c.CanCreate),
                YesNo(c.CanFormat),
                YesNo(c.CanResize),
                YesNo(c.CanExtend),
                YesNo(c.CanCheck),
                YesNo(c.CanLabel),
                c.Notes))
            .ToList();

    private static string YesNo(bool value) => value ? LocExtension.Get("Yes") : LocExtension.Get("No");

    private record FsSupport(string FileSystem, string Create, string Format,
        string Resize, string Extend, string Check, string Label, string Notes);
}

using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PartitionPilot;

namespace PartitionPilot.MarketingCapture;

internal static class Program
{
    private const uint DesktopCreateWindow = 0x0002;
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopWriteObjects = 0x0080;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint WaitTimeout = 0x00000102;
    private const int UoiName = 2;

    private static readonly (int Tab, string FileName)[] Views =
    [
        (0, "01-partition-workspace.png"),
        (2, "02-disk-health.png"),
        (3, "03-maintenance-tools.png"),
        (6, "04-imaging-and-cloning.png")
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        SetBestDpiAwareness();
        var output = Path.GetFullPath(GetOption(args, "--output") ??
            Path.Combine(Environment.CurrentDirectory, "assets", "screenshots"));
        return args.Contains("--worker", StringComparer.OrdinalIgnoreCase)
            ? RunWorker(output)
            : RunOnPrivateDesktop(output);
    }

    private static int RunOnPrivateDesktop(string output)
    {
        Directory.CreateDirectory(output);
        var desktopName = $"PartitionPilotCapture_{Environment.ProcessId}_{DateTime.UtcNow.Ticks}";
        var desktop = CreateDesktop(
            desktopName,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            DesktopCreateWindow | DesktopReadObjects | DesktopWriteObjects,
            IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a private capture desktop.");

        try
        {
            var worker = Environment.ProcessPath ??
                throw new InvalidOperationException("Could not resolve the capture executable.");
            var commandLine = new StringBuilder($"\"{worker}\" --worker --output \"{output}\"");
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = $"winsta0\\{desktopName}"
            };
            if (!CreateProcess(
                    worker,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    AppContext.BaseDirectory,
                    ref startup,
                    out var process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the capture worker.");
            }

            try
            {
                var wait = WaitForSingleObject(process.Process, 180_000);
                if (wait == WaitTimeout)
                {
                    TerminateProcess(process.Process, 124);
                    Console.Error.WriteLine("Capture timed out after 180 seconds.");
                    return 124;
                }
                if (!GetExitCodeProcess(process.Process, out var exitCode))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read capture status.");
                return unchecked((int)exitCode);
            }
            finally
            {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    private static int RunWorker(string output)
    {
        if (string.Equals(GetCurrentDesktopName(), "Default", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Capture worker refused to run on the interactive desktop.");
            return 3;
        }

        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "portable.txt"), "capture\n");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "settings.txt"), "dark\n");
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "shell.json"),
            """
            {
              "WindowLeft": 0,
              "WindowTop": 0,
              "WindowWidth": 1454,
              "WindowHeight": 900,
              "WindowMaximized": false,
              "SelectedTabIndex": 0,
              "ActivityLogHeight": 184,
              "ActivityLogCollapsed": true,
              "Language": ""
            }
            """);

        typeof(App)
            .GetProperty(nameof(App.IsSimulationMode), BindingFlags.Public | BindingFlags.Static)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(null, [true]);

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PartitionPilot;component/Themes/DarkTheme.xaml")
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PartitionPilot;component/Themes/AppStyles.xaml")
        });
        LanguageService.LoadAndApply("");

        var exitCode = 0;
        var window = new MainWindow
        {
            Width = 1454,
            Height = 900,
            Left = 0,
            Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false
        };
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await CaptureViewsAsync(window, output);
            }
            catch (Exception ex)
            {
                exitCode = 1;
                await File.WriteAllTextAsync(Path.Combine(output, "capture-error.txt"), ex.ToString());
            }
            finally
            {
                window.Close();
                app.Shutdown(exitCode);
            }
        };
        app.Run(window);
        return exitCode;
    }

    private static async Task CaptureViewsAsync(MainWindow window, string output)
    {
        if (window.DataContext is not MainViewModel viewModel)
            throw new InvalidOperationException("The production window did not expose its view model.");

        await WaitUntilAsync(
            () => !viewModel.Partitions.IsBusy && viewModel.Partitions.Disks.Count > 0,
            TimeSpan.FromSeconds(30));

        var results = new List<object>();
        var captureTarget = window.Content as FrameworkElement ?? window;
        foreach (var (tab, fileName) in Views)
        {
            viewModel.SelectedTabIndex = tab;
            await Task.Delay(tab == 2 ? 2200 : 1400);
            var destination = Path.Combine(output, fileName);
            var (width, height, bytes) = Capture(captureTarget, destination);
            results.Add(new { tab, file = fileName, width, height, bytes });
        }

        var report = JsonSerializer.Serialize(
            new
            {
                version = MainViewModel.GetVersionText(),
                isolatedDesktop = true,
                simulatedData = true,
                screenshots = results
            },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(
            Path.Combine(output, "capture-report.json"),
            report.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        File.Delete(Path.Combine(output, "capture-error.txt"));
    }

    private static (int Width, int Height, long Bytes) Capture(FrameworkElement visual, string destination)
    {
        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        if (width < 1200 || height < 760)
            throw new InvalidOperationException($"Unexpected capture size {width}x{height}.");

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(destination))
            encoder.Save(stream);
        var bytes = new FileInfo(destination).Length;
        if (bytes < 80_000)
            throw new InvalidOperationException($"{Path.GetFileName(destination)} appears blank or incomplete.");
        return (width, height, bytes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > timeout)
                throw new TimeoutException("The simulated product state did not finish loading.");
            await Task.Delay(100);
        }
    }

    private static string GetCurrentDesktopName()
    {
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var required = 0;
        GetUserObjectInformation(desktop, UoiName, IntPtr.Zero, 0, ref required);
        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            if (!GetUserObjectInformation(desktop, UoiName, buffer, required, ref required))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the desktop name.");
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return null;
    }

    private static void SetBestDpiAwareness()
    {
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
            SetProcessDpiAware();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string desktop, IntPtr device, IntPtr deviceMode, int flags, uint desiredAccess, IntPtr securityAttributes);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, IntPtr information, int length, ref int needed);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAware();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}

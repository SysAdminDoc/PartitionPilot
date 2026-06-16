# PartitionPilot Roadmap

## Research-Driven Additions

### P0 — Critical Fixes (ship-blocking)

- [ ] P0 — Fix broken FindImageDriveLetter
  Why: Method iterates volumes but never returns a value; all mounted images show no drive letter
  Evidence: `WmiDiskService.cs:451-478` — loop body is empty, function always returns null
  Touches: `Services/WmiDiskService.cs`
  Acceptance: Mounted ISO/VHD images display their assigned drive letter in the Disk Images tab
  Complexity: S

- [ ] P0 — Replace silent catch blocks with error logging
  Why: 8 catch blocks in WmiDiskService swallow all exceptions including connectivity/permission errors, making failures invisible
  Evidence: `WmiDiskService.cs` lines 47, 89, 148, 210, 322, 348, 379, 448 — all `catch { /* return empty */ }`
  Touches: `Services/WmiDiskService.cs`, `Services/ActivityLog.cs`
  Acceptance: WMI query failures appear in the activity log with the exception message
  Complexity: S

- [x] P0 — Fix wipe free-space mode targeting
  Why: Free-space wipe binds to DiskInfo (whole disks) but cipher /w: needs a volume drive letter. Users selecting "wipe free space" get a disk-level wipe instead.
  Evidence: `ToolsViewModel.cs:131-172` — WipeTargets is `ObservableCollection<DiskInfo>`, but `cipher /w:X:\` needs a letter
  Touches: `ViewModels/ToolsViewModel.cs`, `Views/ToolsView.xaml`
  Acceptance: Free-space wipe shows a volume/letter selector; full-disk wipe shows a disk selector. Correct tool is invoked for each mode.
  Complexity: S

- [ ] P0 — Fix boot repair EFI partition detection
  Why: Hardcodes `/s S:` as EFI System Partition path. Most systems mount EFI at different letters or don't assign one.
  Evidence: `ToolsViewModel.cs:581` — `bcdbootArgs = $"{SelectedBootDrive}:\\Windows /s S: /f UEFI"`
  Touches: `ViewModels/ToolsViewModel.cs`
  Acceptance: Boot repair auto-detects the EFI partition by querying MSFT_Partition for the System partition type GUID `{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}`
  Complexity: S

- [x] P0 — Add loading indicators to UI
  Why: IsBusy property exists on all 4 ViewModels but no XAML renders a spinner/overlay. During async operations, the UI looks frozen.
  Evidence: All ViewModels have `IsBusy` property; no View XAML references it
  Touches: `Views/PartitionsView.xaml`, `Views/DiskHealthView.xaml`, `Views/ToolsView.xaml`, `Views/DiskImagesView.xaml`, `Themes/AppStyles.xaml`
  Acceptance: Each tab shows a semi-transparent overlay with "Working..." text while IsBusy is true, and action buttons are disabled
  Complexity: S

- [x] P0 — Add README.md
  Why: No documentation exists. Users and contributors cannot understand what the project is, how to build it, or how to use it.
  Evidence: `Glob **/*.md` returns zero results (no markdown files in repo)
  Touches: root `README.md`
  Acceptance: README has: project description, screenshot, features list, build instructions, usage instructions, license badge
  Complexity: S

- [x] P0 — Add LICENSE file
  Why: No license file means the project is technically "all rights reserved." Required for open-source distribution.
  Evidence: No LICENSE or COPYING file exists
  Touches: root `LICENSE`
  Acceptance: MIT LICENSE file exists at repo root
  Complexity: S

### P1 — Architecture & Quality

- [ ] P1 — Extract IDialogService to remove MessageBox from ViewModels
  Why: ViewModels directly call MessageBox.Show() (12+ instances), violating MVVM and preventing unit testing
  Evidence: `PartitionsViewModel.cs:278,306,369,414,458,567,602,639`, `ToolsViewModel.cs:319,324,389,395` etc.
  Touches: New `Services/IDialogService.cs`, all ViewModels, `MainWindow.xaml.cs` (register implementation)
  Acceptance: No ViewModel imports System.Windows.MessageBox; all dialogs routed through IDialogService; ViewModels are testable without a UI
  Complexity: M

- [ ] P1 — Add CancellationToken support for async operations
  Why: Long operations (disk wipe, full format, benchmark) cannot be cancelled. User must wait or kill the process.
  Evidence: No CancellationToken parameter on any async method across all ViewModels and services
  Touches: `Services/ProcessRunner.cs`, all ViewModels, add Cancel button to UI
  Acceptance: Each long-running operation shows a Cancel button; clicking it aborts the process and logs "Operation cancelled"
  Complexity: M

- [ ] P1 — Add progress reporting for long operations
  Why: Only benchmark has IProgress<string>. Disk wipe, full format, and filesystem check run for minutes with no feedback.
  Evidence: `ToolsViewModel.cs` — RunWipeAsync, RunFsCheckAsync, RunOptimizeAsync have no progress reporting; only RunBenchmarkCore uses IProgress
  Touches: `Services/ProcessRunner.cs`, `ViewModels/ToolsViewModel.cs`, `Views/ToolsView.xaml`
  Acceptance: Wipe, check, and optimize operations show progress status in the activity log at minimum. Benchmark already works.
  Complexity: M

- [ ] P1 — Add input sanitization for shell commands
  Why: Values from dialog inputs (volume labels, file paths) are interpolated directly into diskpart scripts and PowerShell commands
  Evidence: `PartitionsViewModel.cs:264-268` — `label="{label}"` with no escaping; `ProcessRunner.cs:27` — simple Replace for quotes
  Touches: `Services/ProcessRunner.cs`, `ViewModels/PartitionsViewModel.cs`
  Acceptance: Volume labels with special characters (quotes, backticks, semicolons) don't cause command injection. ProcessRunner properly escapes all arguments.
  Complexity: M

- [ ] P1 — Extract service interfaces for testability
  Why: WmiDiskService and ProcessRunner are concrete classes with no interfaces. Cannot mock for unit testing.
  Evidence: All ViewModels take concrete types: `WmiDiskService wmiService`, `ProcessRunner processRunner`
  Touches: New `Services/IWmiDiskService.cs`, `Services/IProcessRunner.cs`, all ViewModels (change parameter types)
  Acceptance: All service dependencies are interface-typed; test project can substitute fakes
  Complexity: M

- [ ] P1 — Add unit test project
  Why: Zero tests exist. Core logic (SizeUtil, segment computation, volume enrichment) is pure and testable.
  Evidence: No test project or test files anywhere in the repo
  Touches: New `tests/PartitionPilot.Tests/` project with xUnit
  Acceptance: Tests cover SizeUtil.Format, EnrichPartitionsWithVolumes, ComputeDiskBarSegments, ProcessRunner argument escaping. CI runs them.
  Complexity: L

- [ ] P1 — Add CI/CD pipeline (GitHub Actions)
  Why: No automated build verification. Contributors can break the build without knowing.
  Evidence: No `.github/workflows/` directory
  Touches: `.github/workflows/build.yml`
  Acceptance: Push to main triggers build + test. PR checks pass before merge.
  Complexity: M

- [ ] P1 — Add activity log export
  Why: Activity log is memory-only, lost on app close. Users need logs for troubleshooting failed operations.
  Evidence: `Services/ActivityLog.cs` — FullText property is ephemeral
  Touches: `Services/ActivityLog.cs`, `MainWindow.xaml` (add export button)
  Acceptance: "Export Log" button saves activity log to a timestamped .txt file. Log also auto-saves to %TEMP%/PartitionPilot/ on app close.
  Complexity: S

- [ ] P1 — Fix ProcessRunner error detection
  Why: Only throws when exit code != 0 AND stderr is non-empty. Some tools write to stderr on success (PowerShell progress). Some report errors in stdout with exit code 0.
  Evidence: `ProcessRunner.cs:56` — `if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))`
  Touches: `Services/ProcessRunner.cs`
  Acceptance: Non-zero exit code always throws (regardless of stderr content). Diskpart output is scanned for "error" patterns. PowerShell stderr warnings don't cause false failures.
  Complexity: S

### P2 — Features & UX

- [ ] P2 — Add dark mode / theme switching
  Why: Professional desktop apps support both themes. Many IT professionals prefer dark mode for extended use.
  Evidence: AppStyles.xaml has only light-theme colors; no theme switching mechanism
  Touches: `Themes/AppStyles.xaml`, new `Themes/DarkTheme.xaml`, `MainWindow.xaml` (add toggle), `App.xaml.cs`
  Acceptance: Settings toggle switches between light and dark themes. Theme preference persists across sessions (via user settings file).
  Complexity: M

- [x] P2 — Add right-click context menu on partition list
  Why: Every commercial competitor (EaseUS, MiniTool, AOMEI, GParted) has right-click menus on partitions. Users expect this.
  Evidence: `Views/PartitionsView.xaml` — ListView has no ContextMenu
  Touches: `Views/PartitionsView.xaml`, `Views/PartitionsView.xaml.cs`
  Acceptance: Right-clicking a partition shows: Create, Delete, Format, Resize, Extend, Split, Change Letter, Set Active, Hide/Unhide — same as the toolbar buttons
  Complexity: S

- [ ] P2 — Add keyboard shortcuts
  Why: Power users and accessibility. No keyboard shortcuts defined for any operation.
  Evidence: No InputBindings or KeyBinding definitions in any XAML file
  Touches: `MainWindow.xaml`, `Views/PartitionsView.xaml`
  Acceptance: F5=Refresh, Delete=Delete partition, F2=Rename/Change Letter. Shortcuts shown in toolbar button tooltips.
  Complexity: S

- [ ] P2 — Add disk usage analysis view
  Why: AOMEI charges $50 for this; no free Windows tool does it well. Shows treemap/bar of what consumes space on a volume.
  Evidence: AOMEI Partition Assistant Pro feature; CrystalDiskInfo doesn't have it; Windows Storage Sense is limited
  Touches: New `Views/DiskUsageView.xaml`, new `ViewModels/DiskUsageViewModel.cs`, add as Tab 5 or sub-view of Partitions
  Acceptance: Selecting a volume shows top 20 largest folders/files with size bars. Scan completes in under 30 seconds for typical drives.
  Complexity: L

- [ ] P2 — Add BitLocker status display
  Why: BitLocker is table-stakes for enterprise. Showing encryption status per volume is low-effort and high-value.
  Evidence: Windows BitLocker PowerShell module (`Get-BitLockerVolume`) provides status; no competitor shows it inline with partitions
  Touches: `Services/WmiDiskService.cs` (add BitLocker query), `Models/PartitionInfo.cs` (add EncryptionStatus), `Views/PartitionsView.xaml`
  Acceptance: Partition table shows a lock icon and "BitLocker: On/Off/Suspended" in the Details column for encrypted volumes
  Complexity: M

- [ ] P2 — Add installer (Inno Setup or WiX)
  Why: Professional distribution requires an installer for Start Menu shortcuts, uninstall support, and file associations.
  Evidence: No installer exists; current distribution is raw .exe
  Touches: New `installer/` directory with Inno Setup script
  Acceptance: Installer creates Start Menu shortcut, desktop icon (optional), and Add/Remove Programs entry. Uninstall is clean.
  Complexity: M

- [ ] P2 — Add WinGet manifest
  Why: WinGet is the standard Windows package manager. Submission makes the tool discoverable via `winget install PartitionPilot`.
  Evidence: No WinGet manifest; tool isn't in winget-pkgs repository
  Touches: New `winget/` manifest files, GitHub Releases
  Acceptance: `winget search PartitionPilot` finds the package after submission to winget-pkgs
  Complexity: S

### P3 — Future Considerations

- [ ] P3 — Add disk cloning / imaging
  Why: Top-requested paid feature across EaseUS/MiniTool/AOMEI. Can be partially implemented using wbadmin or DISM.
  Evidence: EaseUS charges $50/yr for this; wbadmin and DISM exist natively
  Touches: New ViewModel + View, ProcessRunner for wbadmin/DISM commands
  Acceptance: User can create a full disk image (.wim or .vhdx) and restore it to same-size or larger disk
  Complexity: XL

- [ ] P3 — Add disk surface test (bad sector scan)
  Why: MiniTool and AOMEI include this. Uses chkdsk /R or custom sector-read scan.
  Evidence: MiniTool Partition Wizard includes surface test; `chkdsk /R` scans for bad sectors natively
  Touches: New section in ToolsView, ToolsViewModel
  Acceptance: User selects a volume, runs scan, sees progress and count of bad sectors found
  Complexity: M

- [ ] P3 — Add auto-update mechanism
  Why: Desktop apps without auto-update become stale. Users won't manually check for updates.
  Evidence: No update mechanism exists; Velopack and Squirrel.Windows are popular .NET solutions
  Touches: New update service, App.xaml.cs startup check
  Acceptance: On launch, app checks GitHub Releases for newer version. Shows notification if update available. One-click update.
  Complexity: L

- [ ] P3 — Add accessibility support (screen readers, keyboard nav)
  Why: Admin tools should be usable by all IT professionals including those using assistive technology.
  Evidence: No AutomationProperties set on any control; no TabIndex defined; no high-contrast theme
  Touches: All XAML files (add AutomationProperties.Name, TabIndex), `Themes/HighContrastTheme.xaml`
  Acceptance: Windows Narrator can navigate all tabs, read partition info, and trigger operations. All controls reachable via Tab key.
  Complexity: L

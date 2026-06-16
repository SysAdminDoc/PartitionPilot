# Research — PartitionPilot

## Executive Summary

PartitionPilot is a .NET 10 WPF disk partition management tool for Windows 10/11 that aims to replace commercial products (EaseUS $50/yr, MiniTool $59/yr, AOMEI $50/yr) with a free, open-source, single-file executable. The C# WPF implementation has a proper MVVM architecture with 29 C# files and 13 XAML files across 4 feature tabs: Partitions, Disk Health, Tools, and Disk Images.

The strongest current shape is the architecture itself — clean MVVM separation, async operations via WMI + Process shelling, professional tab-based UI with a custom DiskBar control, and comprehensive feature coverage (9 partition operations, SMART monitoring, 7 tools, VHD management).

**Top opportunities in priority order:**
1. Fix 4 bugs shipping in current code (broken FindImageDriveLetter, wipe targeting, boot repair EFI detection, silent exception swallowing)
2. Add loading indicators — IsBusy exists on all ViewModels but no XAML binds to it
3. Add README and LICENSE for distribution readiness
4. Extract service interfaces and add dialog service pattern for testability
5. Add CancellationToken and progress reporting for long operations
6. Add dark mode theme switching
7. Add right-click context menus and keyboard shortcuts
8. Add CI/CD pipeline with automated builds
9. Add installer and WinGet manifest for distribution
10. Add disk usage analysis feature (underserved by competitors)

## Product Map

**Core workflows:**
- Partition management: create, delete, format, resize, extend, split, change letter, set active, hide/unhide
- Disk health monitoring: SMART data, 4K alignment audit, physical disk info
- System tools: MBR→GPT, FAT32→NTFS, filesystem check, optimize/TRIM, secure wipe, bootloader repair, disk benchmark
- Disk images: mount/dismount ISO/VHD/VHDX, create VHD

**User personas:**
- IT professionals managing multiple machines (primary)
- Power users extending VM disks or managing multi-boot systems
- Sysadmins needing free alternatives to commercial partition tools

**Platforms and distribution:**
- Windows 10/11 only (x64), requires administrator privileges
- Self-contained single-file .exe (~60-80MB), no .NET runtime required
- No installer, no auto-update, no WinGet package currently

**Key integrations and data flows:**
- WMI/CIM: MSFT_Disk, MSFT_Partition, MSFT_Volume, MSFT_PhysicalDisk, MSFT_StorageReliabilityCounter (via System.Management NuGet)
- Native tools: diskpart.exe, mbr2gpt.exe, bcdboot.exe, cipher.exe, convert.exe, reagentc.exe
- PowerShell cmdlets: Resize-Partition, Format-Volume, Repair-Volume, Optimize-Volume, Mount/Dismount-DiskImage, Set-Partition

## Competitive Landscape

**EaseUS Partition Master ($50/yr Pro)**
- Does well: OS migration to SSD, partition recovery, merge partitions, WinPE bootable media
- Learn from: "Pending operations" UX — users queue changes, review, then apply. This prevents accidents.
- Avoid: bloatware bundling, aggressive upsell dialogs, limited free tier

**MiniTool Partition Wizard ($59/yr Pro)**
- Does well: split partition in-place, cluster size change, data recovery, surface test
- Learn from: clean partition visualization with right-click context menus for every operation
- Avoid: paywalling basic features, complex licensing tiers

**AOMEI Partition Assistant ($50 Pro)**
- Does well: most generous free tier (includes resize, move, merge, clone), SSD secure erase, 4K alignment, disk speed test
- Learn from: disk space analysis view (treemap of file usage), quick partition → wizard-driven flow
- Avoid: ads in free version, Windows Server separate pricing

**GParted (free, Linux)**
- Does well: partition move (change offset without data loss) — the #1 feature Windows tools can't match natively. Handles 30+ filesystems.
- Learn from: simple, focused interface; clear color-coded partition bar; keyboard-driven
- Avoid: Linux-only limitation, no SMART integration, no disk images

**CrystalDiskInfo / CrystalDiskMark (free)**
- Does well: deep SMART attribute display (all 30+ attributes), disk benchmark with queue depth options, trend tracking over time
- Learn from: benchmark methodology (multiple queue depths, configurable file sizes), SMART attribute detail level
- Avoid: single-purpose scope, no partition management

## Security, Privacy, and Reliability

**Bugs found:**
- `WmiDiskService.cs:451-478`: `FindImageDriveLetter` has an empty loop body — iterates volumes but never returns a drive letter. Every mounted image shows no drive letter.
- `ToolsViewModel.cs:581`: Boot repair hardcodes `/s S:` as the EFI System Partition path. Not all systems mount EFI at S:.
- `ToolsViewModel.cs:131-172`: Wipe free-space mode (`WipeIsFreeSpace`) binds to `WipeTargets` which is `ObservableCollection<DiskInfo>`. Free-space wipe via `cipher /w:` needs a volume letter, not a disk number.
- `WmiDiskService.cs` (lines 47, 89, 148, 210, 322, 348, 379, 448): Eight `catch { /* return empty */ }` blocks swallow all exceptions including WMI connectivity failures, permission errors, and malformed data — no logging.

**Missing guardrails:**
- No input sanitization on values passed to diskpart/PowerShell via string interpolation (`ProcessRunner.cs:27`, `PartitionsViewModel.cs:264-268`)
- `ProcessRunner.cs:56`: Error detection requires BOTH non-zero exit code AND non-empty stderr — some tools write warnings to stderr on success, and some tools succeed with exit code 0 but report errors in stdout
- No backup reminder before destructive operations (delete, format, wipe)
- No validation of diskpart output patterns to confirm operations succeeded
- Benchmark (`ToolsViewModel.cs:651`) doesn't verify 256MB free space before creating temp file

**Recovery and rollback needs:**
- No operation preview (queue-then-apply pattern). All operations execute immediately.
- No undo capability. Partition operations are irreversible by nature, but the tool should at minimum log enough detail to diagnose what happened.
- Activity log is memory-only — lost on app close. Should persist to file.

## Architecture Assessment

**Module improvements needed:**
- ViewModels directly call `MessageBox.Show()` (e.g., `PartitionsViewModel.cs:278`, `ToolsViewModel.cs:319`). This violates MVVM and prevents unit testing. Extract an `IDialogService` interface.
- No `CancellationToken` on any async operation. Long operations (wipe, benchmark, full format) cannot be cancelled.
- Services have no interface abstractions — `WmiDiskService` and `ProcessRunner` are concrete classes, making them unmockable for testing.
- `MainViewModel.cs` creates all services and child ViewModels directly. A simple manual DI pattern would improve testability.

**Technology adoption opportunities:**
- **CommunityToolkit.Mvvm**: The official Microsoft MVVM library uses Roslyn source generators to eliminate boilerplate. `[ObservableProperty]` replaces manual SetProperty calls; `[RelayCommand]` replaces manual ICommand wiring. Would cut ViewModel boilerplate by ~40%. Trade-off: requires all ViewModels to become `partial` classes. Source: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/
- **.NET 9/10 Fluent Theme**: Built-in `ThemeMode="System"` provides native light/dark/high-contrast switching without custom XAML. Enables dark mode with one line change. Source: https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md
- **Velopack**: Successor to Squirrel.Windows for auto-updates. Delta packages, cross-platform, GitHub Releases hosting. Best current option for desktop app auto-update. Source: https://docs.velopack.io/

**Refactor candidates:**
- `PartitionsViewModel.cs` (647 lines): The Extend operation (lines 467-574) handles recovery environment, pagefile, and standard paths in one method. Extract to a dedicated `ExtendPartitionService`.
- `ToolsViewModel.cs` (740 lines): Each tool (7 total) is a self-contained async method. Could extract individual tool services, but current structure is acceptable.
- `WmiDiskService.cs` (507 lines): Repeated pattern of scope.Connect() → searcher → foreach → dispose. A generic helper method would reduce boilerplate by ~40%.

**Test gaps:**
- Zero tests. No test project exists. Priority test targets:
  1. `SizeUtil.Format()` — pure function, trivial to test
  2. `WmiDiskService.EnrichPartitionsWithVolumes()` — static method, easy to test
  3. `PartitionsViewModel.ComputeDiskBarSegments()` — core visualization logic, worth testing
  4. `ProcessRunner` — mockable with interface extraction
  5. Integration tests using VHD files for non-destructive partition operations

**Documentation gaps:**
- No README.md
- No LICENSE file
- No CONTRIBUTING.md
- No architecture documentation
- No code comments on non-obvious patterns (e.g., the PowerShell fallback for SMART data)

## Rejected Ideas

| Idea | Reason | Source |
|------|--------|--------|
| Dynamic disk management | Niche, declining Windows feature. diskpart covers it for power users. | AOMEI Pro feature |
| Storage Spaces management | Built into Windows Settings app. Not worth replicating. | Windows native |
| OS migration to SSD | Too many failure modes (BCD, drivers, UUID changes, boot repair). Commercial tools use kernel drivers. | EaseUS/MiniTool flagship |
| Partition recovery/undelete | Requires raw sector scanning and partition table signature detection — architectural scope exceeds a partition manager. | TestDisk |
| NTFS permission viewer | Different problem domain. Windows has icacls and Security tab. | Adjacent tool research |
| System tray mode | Overkill for an occasional-use admin tool. | MiniTool has it |
| Drag-and-drop for images | Confusing UX for admin tool where precision matters. | UI brainstorm |
| i18n/l10n | English-only is appropriate for v1. Low demand signal for admin tools. | General consideration |
| Telemetry/analytics | Privacy concern for an admin tool that manages disks. Opt-in would be ignored. | General consideration |
| Move partition (change offset) | No Windows API exists. Would require custom sector-level I/O — kernel driver territory. | GParted capability |
| Merge partitions (preserving both) | No atomic API. Would need copy + delete + extend workflow with significant data loss risk. | EaseUS/AOMEI |

## Sources

**Commercial competitors:**
- https://www.easeus.com/partition-manager/
- https://www.minitool.com/partition-manager/
- https://www.diskpart.com/partition-assistant/
- https://www.paragon-software.com/home/hdm-windows/
- https://crystalmark.info/en/software/crystaldiskinfo/
- https://crystalmark.info/en/software/crystaldiskmark/

**Open-source tools:**
- https://gparted.org/
- https://www.cgsecurity.org/wiki/TestDisk
- https://github.com/pbatard/rufus
- https://github.com/TalAloni/DiskAccessLibrary (C# library for low-level disk/VHD/NTFS access — potential future dependency for advanced operations)
- https://github.com/becerda/DiskpartGUI-WPF (abandoned C# WPF diskpart wrapper — confirms no active OSS competitor in this space)

**Windows APIs and documentation:**
- https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-partition
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-volume
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/diskpart
- https://learn.microsoft.com/en-us/windows/deployment/mbr-to-gpt

**WPF and .NET:**
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
- https://github.com/CommunityToolkit/dotnet

**Packaging, distribution, and security:**
- https://docs.velopack.io/ (auto-update framework, successor to Squirrel.Windows)
- https://github.com/microsoft/winget-pkgs
- https://azure.microsoft.com/en-us/products/artifact-signing (code signing ~$10/mo)
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/

**WPF theming and toolkits:**
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/ (CommunityToolkit.Mvvm — source generators)
- https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md (.NET 9+ Fluent theme with dark mode)
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90

**Additional Windows APIs:**
- https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume (BitLocker WMI)
- https://learn.microsoft.com/en-us/windows/dev-drive/ (Dev Drive / ReFS)

## Open Questions

1. **License choice**: MIT is standard for OSS tools, but admin tools that shell out to Windows binaries may want to clarify that the tool itself is MIT while the Windows APIs/tools it calls are Microsoft-licensed. Needs explicit LICENSE file.
2. **Target .NET version for distribution**: Currently targets .NET 10 (preview-era). Should this pin to .NET 8 LTS for wider compatibility and smaller self-contained size, or stay on .NET 10?
3. **Benchmark methodology validation**: The current 256MB sequential + 500-op random 4K test differs from CrystalDiskMark's approach (which uses multiple queue depths and threads). Should the benchmark be expanded for credibility, or is simple adequate for a partition tool?

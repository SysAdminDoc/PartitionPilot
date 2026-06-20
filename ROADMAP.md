# PartitionPilot Roadmap

## Research-Driven Additions

### P1

- [ ] P1 — Add post-clone verification checksum
  Why: Sector clone writes all sectors but performs no verification — an incomplete clone from write caching or hardware error is undetectable.
  Evidence: Clonezilla uses XXH128, DiskGenius 6.2 added auto-TRIM before clone. Rescuezilla issue #237 documents bad-sector clone failures.
  Touches: `src/PartitionPilot.Core/Services/SectorCloneService.cs` (add verify pass after CopyLoop), `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` (UI progress for verify phase)
  Acceptance: After sector clone completes, a verification pass reads both source and destination in 1MB blocks, compares hashes, and reports match/mismatch with sector offset detail. CLI `pp plan` shows verify status.
  Complexity: S

- [ ] P1 — Add bad-sector rescue mode for sector clone
  Why: SectorCloneService.CopyLoop throws on any read failure. Dying source drives need a mode that zeroes unreadable sectors and logs their offsets.
  Evidence: Clonezilla partclone `-rescue` flag, DiskGenius resumable sector copy with bad-sector threshold. Most-requested clone feature in Rescuezilla/Clonezilla trackers.
  Touches: `src/PartitionPilot.Core/Services/SectorCloneService.cs` (add rescue parameter to CloneAsync, zero-fill on read error, accumulate bad sector list), `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` (rescue mode toggle, bad sector report display)
  Acceptance: When rescue mode is enabled, read failures zero the destination sector, log the offset, and continue. Final report lists all bad sectors with count and percentage. Non-rescue mode retains current fail-hard behavior.
  Complexity: M

- [ ] P1 — Trigger SMART self-tests (short and extended)
  Why: Table-stakes for disk health monitoring. HD Sentinel, GSmartControl, and smartmontools all support self-test initiation. PartitionPilot is read-only.
  Evidence: smartmontools `smartctl -t short/long`, HD Sentinel Pro self-test feature, GSmartControl GUI self-test trigger.
  Touches: `src/PartitionPilot.Core/Services/SmartQueryService.cs` or new `SmartTestService.cs` (invoke smartctl or use IOCTL_STORAGE_PROTOCOL_COMMAND for NVMe DEVICE_SELF_TEST), `src/PartitionPilot/ViewModels/DiskHealthViewModel.cs` (trigger button, status polling)
  Acceptance: Disk Health tab has "Run Short Test" and "Run Extended Test" buttons. Test status polls and displays progress/result. Works on both SATA (via smartctl) and NVMe (via IOCTL or smartctl).
  Complexity: M

- [ ] P1 — Surface full NVMe health log attributes
  Why: PartitionPilot shows a subset of NVMe SMART data via LibreHardwareMonitor. CrystalDiskInfo displays 20+ NVMe attributes including Unsafe Shutdowns, Controller Busy Time, Error Log Entries, Critical Warning bits, and per-sensor temperatures.
  Evidence: NVME_HEALTH_INFO_LOG structure (Microsoft Learn), CrystalDiskInfo English.lang SmartNVMe section.
  Touches: `src/PartitionPilot.Core/Services/SmartQueryService.cs` (add IOCTL_STORAGE_QUERY_PROPERTY with NVMeDataTypeLogPage fallback), `src/PartitionPilot.Core/Models/SmartData.cs` (add UnsafeShutdowns, ControllerBusyTime, ErrorLogEntries, CriticalWarningFlags fields), `src/PartitionPilot/Views/DiskHealthView.xaml` (display new attributes)
  Acceptance: NVMe drives show Unsafe Shutdowns, Controller Busy Time (minutes), Error Information Log Entry Count, Critical Warning flags (spare low, temp exceeded, reliability degraded, read-only, backup failed), and Temperature Sensors 1-8 where reported by hardware.
  Complexity: M

- [ ] P1 — Show BitLocker encryption progress and method
  Why: Win32_EncryptableVolume exposes ConversionStatus (with EncryptionPercentage), EncryptionMethod (XTS-AES-128, AES-256, etc.), and LockStatus. PartitionPilot only shows on/off/unknown.
  Evidence: Win32_EncryptableVolume.GetConversionStatus() and GetEncryptionMethod() methods (Microsoft Learn). BitLocker enabled by default on Windows 11 24H2+.
  Touches: `src/PartitionPilot.Core/Services/BitLockerPreflight.cs` (query ConversionStatus, EncryptionMethod), `src/PartitionPilot.Core/Services/WmiDiskService.cs` (GetBitLockerStatusAsync returns richer data), partition detail display in `PartitionsView.xaml`
  Acceptance: Partition details show "BitLocker: On (XTS-AES-128, 100%)" or "BitLocker: Encrypting (45%)" instead of just "BitLocker: On".
  Complexity: S

- [ ] P1 — Log journal-save failures in OperationQueue
  Why: Four `catch { }` blocks in OperationQueue.ApplyAllAsync silently swallow journal write failures. If ProgramData is unwritable, crash-recovery protection fails without user indication.
  Evidence: Code scan found bare catch at `OperationQueue.cs` lines 85, 95, 105, 119.
  Touches: `src/PartitionPilot.Core/Services/OperationQueue.cs` (add `_log.Log()` calls in catch blocks)
  Acceptance: Journal save failures log a warning to ActivityLog. Behavior is otherwise unchanged — journal failures do not block operation execution.
  Complexity: S

- [ ] P1 — Add CLI benchmark command
  Why: DiskSpd benchmarks are GUI-only. Sysadmins need scripted benchmark runs for fleet disk performance baselines.
  Evidence: CrystalDiskMark has no CLI; DiskSpd itself is CLI but raw. Commercial tools like AOMEI expose CLI benchmarks.
  Touches: `src/PartitionPilot.Cli/Program.cs` (add `benchmark` command with `--drive` and `--json` flags), references `DiskSpdService` from Core
  Acceptance: `pp benchmark --drive C --json` runs DiskSpd profiles on the target drive and outputs structured results. Human-readable table by default, JSON with `--json`.
  Complexity: S

- [ ] P1 — Add CLI SMART history and trend commands
  Why: SMART history and trend analysis are GUI-only. CLI users managing fleets need scripted access to health trends.
  Evidence: smartctl `--json` provides structured health output. PartitionPilot's SmartHistoryService already has the data.
  Touches: `src/PartitionPilot.Cli/Program.cs` (add `smart-history` and `smart-trends` subcommands), references `SmartHistoryService` from Core
  Acceptance: `pp smart-history --disk 0 --json` shows recorded readings. `pp smart-trends --disk 0` shows trend analysis with severity levels. Both support `--json`.
  Complexity: S

### P2

- [ ] P2 — Add filesystem support matrix view
  Why: GParted and KDE Partition Manager show a matrix of which operations each filesystem type supports. Helps users understand what's possible before queueing operations.
  Evidence: GParted Tools > File System Support dialog, KDE PM Tools > File System Support.
  Touches: New dialog or view in `src/PartitionPilot/Dialogs/` showing FS (NTFS, FAT32, exFAT, ReFS, FAT) vs. operations (Create, Format, Resize, Shrink, Extend, Check, Label) with supported/unsupported indicators
  Acceptance: A "Filesystem Support" button or menu item opens a matrix showing which operations PartitionPilot supports per filesystem. Unsupported combinations show the reason (e.g., "Windows limitation").
  Complexity: S

- [ ] P2 — Enable FAT32 formatting above 32 GB
  Why: Windows caps FAT32 format at 32 GB via diskpart/Format-Volume. Users need large FAT32 for game consoles, cameras, and cross-platform USB drives. Macrorit formats FAT32 up to 2 TB free.
  Evidence: Macrorit free edition, AOMEI free edition both support >32GB FAT32. Top community complaint about Windows Disk Management.
  Touches: `src/PartitionPilot/ViewModels/PartitionsViewModel.cs` or `ToolsViewModel.cs` (use format.com with /FS:FAT32 or direct diskpart scripting with explicit cluster size for large volumes), `src/PartitionPilot/Dialogs/FormatPartitionDialog.xaml.cs` (remove 32GB guard if present, set appropriate cluster size)
  Acceptance: Formatting a 64GB+ volume as FAT32 works. Cluster size auto-scales (32KB for 32-64GB, 64KB for 64GB-2TB). Warning shown for >32GB about cross-platform compatibility considerations.
  Complexity: S

- [ ] P2 — Add SMART diagnostic report export
  Why: IT support workflows need formatted health reports for documentation and ticket attachment. GSmartControl generates HTML reports. HD Sentinel exports HTML/text.
  Evidence: GSmartControl HTML diagnostic report, HD Sentinel email reports with drive images.
  Touches: `src/PartitionPilot.Core/Services/SmartHistoryService.cs` (add FormatHtmlReport method), `src/PartitionPilot/ViewModels/DiskHealthViewModel.cs` (export button)
  Acceptance: "Export Report" button on Disk Health tab generates an HTML file with drive info, SMART attributes, health status, trend analysis, temperature history, and alignment audit. File opens in default browser after export.
  Complexity: M

- [ ] P2 — Add pseudo-locale resource completeness CI gate
  Why: i18n infrastructure exists with 130+ keys and pseudo-locale (qps-ploc), but no CI check prevents resource key drift. New UI strings added without resource keys would silently break translations.
  Evidence: v0.8.0 added pseudo-locale testing but no automated completeness gate.
  Touches: `.github/workflows/build.yml` (add step that compares XAML `{x:Static}` / `LocExtension` usage against `Strings.resx` keys), `tests/PartitionPilot.Tests/LocExtensionTests.cs` (expand key coverage test)
  Acceptance: CI fails if any UI-facing string is hardcoded instead of using a resource key. Existing LocExtensionTests verify all declared keys resolve.
  Complexity: S

- [ ] P2 — Expand recovery scanner filesystem signatures
  Why: PartitionRecoveryScanner recognizes 5 filesystem types (NTFS, FAT32, FAT16/12, exFAT, ReFS). TestDisk scans 30+ types. Adding common Linux and Apple signatures improves lost-partition detection on multi-boot or repurposed disks.
  Evidence: TestDisk partition type support list (30+ types), dual-boot user scenarios, GParted filesystem detection breadth.
  Touches: `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs` (add ext2/3/4 superblock signature at offset 1024+56, btrfs magic at offset 65536+64, XFS magic at offset 0, HFS+ at offset 1024, APFS container superblock, Linux swap magic at offset 4086)
  Acceptance: Recovery scan detects ext2/3/4, btrfs, XFS, HFS+, APFS, and Linux swap in addition to existing NTFS/FAT/exFAT/ReFS. Each match includes filesystem type, estimated size where available, and confidence score.
  Complexity: M

- [ ] P2 — Add TBW endurance gauge for SSDs
  Why: HD Sentinel calculates remaining lifetime from total writes vs manufacturer-rated TBW. PartitionPilot shows wear percentage but doesn't relate it to the drive's endurance rating.
  Evidence: HD Sentinel remaining lifetime estimation, CrystalDiskInfo Data Units Written display, Backblaze SSD SMART analysis.
  Touches: `src/PartitionPilot.Core/Models/SmartData.cs` (add TotalBytesWritten, RatedTbwBytes fields), `src/PartitionPilot/Views/DiskHealthView.xaml` (progress bar showing writes vs rated TBW), `src/PartitionPilot/ViewModels/DiskHealthViewModel.cs` (TBW lookup or user-configurable rated value)
  Acceptance: For SSDs with total bytes written data, Disk Health shows "X TB written of Y TB rated endurance (Z% consumed)" with a progress bar. Rated TBW is user-configurable per drive (no reliable way to query it from firmware).
  Complexity: M

- [ ] P2 — Erase target filesystem signatures before sector clone
  Why: GParted erases existing filesystem signatures on clone targets before copy to prevent ghost filesystem detection by Windows or other tools.
  Evidence: GParted v1.8.0 pre-copy signature erasure feature.
  Touches: `src/PartitionPilot.Core/Services/SectorCloneService.cs` (zero boot sector and FS signature regions on destination before copy begins)
  Acceptance: Before sector clone starts, the first 64KB of the destination disk is zeroed to remove any existing filesystem signatures. This prevents Windows from detecting stale partitions if the clone is interrupted.
  Complexity: S

- [ ] P2 — Add CLI temperature and diagnostic subcommands
  Why: CLI `pp diagnostics` exists but temperature monitoring and SMART export are GUI-only. Fleet management scripts need access to all diagnostic data.
  Evidence: smartctl provides comprehensive CLI health output. PartitionPilot's TemperatureMonitorService and SmartHistoryService already have the data.
  Touches: `src/PartitionPilot.Cli/Program.cs` (add `temperature` command showing current temps for all disks, add `smart-export` command for history JSON export)
  Acceptance: `pp temperature --json` shows current temperatures for all physical disks. `pp smart-export --disk 0 --output smart.json` exports SMART history to file.
  Complexity: S

### P3

- [ ] P3 — Add read-only hex sector viewer
  Why: DiskGenius's free hex sector viewer is a power-user differentiator for inspecting raw disk content, verifying wipe completion, and examining boot sectors.
  Evidence: DiskGenius hex viewer (free, read-only), Paragon Advanced hex editor.
  Touches: New `src/PartitionPilot/Views/HexViewerView.xaml` and `src/PartitionPilot/ViewModels/HexViewerViewModel.cs`, P/Invoke ReadFile for raw sector access
  Acceptance: New tab or tool window reads raw sectors from a selected disk. Displays hex + ASCII, navigates by sector offset, shows current LBA. Read-only — no write capability. Handles 512-byte and 4K sectors.
  Complexity: M

- [ ] P3 — Add ARM64 build target
  Why: Windows on ARM is growing (Surface Pro, Snapdragon X Elite devices). PartitionPilot is currently x64-only.
  Evidence: Rescuezilla issue #642 (ARM64 request), growing Windows ARM device market.
  Touches: `src/PartitionPilot/PartitionPilot.csproj` (add `win-arm64` RID), `src/PartitionPilot.Cli/PartitionPilot.Cli.csproj` (same), `.github/workflows/build.yml` (add ARM64 publish matrix), `installer/PartitionPilot.iss` (ARM64 variant)
  Acceptance: CI produces both x64 and ARM64 self-contained binaries. P/Invoke calls in SectorCloneService, MftScanner, SecureEraseService, VolumeLockService, and PartitionRecoveryScanner work on ARM64 (all use standard Win32 APIs).
  Complexity: M

- [ ] P3 — Add community translation starter (2-3 languages)
  Why: i18n infrastructure (130+ resource keys, LocExtension, pseudo-locale) is built but untested with real translations. Shipping 2-3 languages validates the pipeline.
  Evidence: All commercial competitors ship 10+ languages. GParted ships 40+ translations.
  Touches: `src/PartitionPilot/Properties/Strings.de.resx`, `Strings.es.resx`, `Strings.fr.resx` (new files), `src/PartitionPilot/ViewModels/MainViewModel.cs` or `ThemeService.cs` (language selector or auto-detect from OS culture)
  Acceptance: German, Spanish, and French resource files exist with all 130+ keys translated. App auto-detects OS culture and loads matching resources. Language can be changed in settings.
  Complexity: M

- [ ] P3 — Add image encryption for WIM/VHDX captures
  Why: Clonezilla added gocryptfs encryption. R-Drive Image uses AES-XTS. Disk images contain sensitive data and should support at-rest encryption.
  Evidence: Clonezilla gocryptfs integration (2025), R-Drive Image AES-XTS encryption.
  Touches: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` (encryption toggle and password prompt), image creation flow (encrypt output stream with AES-256-GCM via .NET cryptography APIs)
  Acceptance: Create Image dialog has an "Encrypt" checkbox. When enabled, prompts for password. Image is wrapped in an AES-256-GCM encrypted envelope. Restore prompts for decryption password. Encrypted images have `.enc` suffix.
  Complexity: L

- [ ] P3 — Add declarative partition layout spec for CLI
  Why: disko (3138 stars) uses declarative partition layout definitions applied idempotently. PartitionPilot's CLI could support "define desired state, apply diff" for deployment automation.
  Evidence: nix-community/disko declarative disk partitioning model.
  Touches: `src/PartitionPilot.Cli/Program.cs` (new `apply-layout` command), `src/PartitionPilot.Core/Models/` (PartitionLayoutSpec model), `src/PartitionPilot.Core/Services/` (LayoutDiffService comparing spec to current state)
  Acceptance: `pp apply-layout --file layout.json --disk 0` reads a JSON layout spec (partitions with sizes, filesystems, labels), computes diff against current disk state, shows plan, and applies with `--apply` flag. Dry-run by default.
  Complexity: L

- [ ] P3 — Add real-time disk I/O performance counters
  Why: Windows PhysicalDisk/LogicalDisk performance counters provide live read/write rates, queue depth, and latency. Useful for diagnosing slow disks during operations.
  Evidence: Windows Performance Monitor disk counters, DiskMon (Sysinternals) real-time monitoring.
  Touches: New `src/PartitionPilot.Core/Services/DiskPerfCounterService.cs` using `System.Diagnostics.PerformanceCounter`, new section in Disk Health or Tools view
  Acceptance: Disk Health tab shows live read/write MB/s, IOPS, average latency, and queue depth for selected disk. Updates every 2 seconds. Can be toggled on/off.
  Complexity: M

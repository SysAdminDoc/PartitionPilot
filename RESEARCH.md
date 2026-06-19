# Research — PartitionPilot

## Executive Summary
PartitionPilot is a Windows .NET 10 WPF disk administration tool for power users and IT admins. Its strongest shape is the integrated shell: partition operations with snapshots, disk health, maintenance tools, disk images, usage scanning, cloning, activity logging, and recently-hardened safety paths (WMI escaping, EncodedCommand, BitLocker preflights, volume locking, NVMe sanitize, operation cleanup scopes). The codebase is clean, well-tested (893 lines across 16 test files), and has no known vulnerable dependencies.

Top 10 opportunities in priority order:
1. **Pending operations queue** — table-stakes safety gap vs every competitor
2. **Richer SMART via LibreHardwareMonitorLib** — 8 attributes vs CrystalDiskInfo's 30+
3. **DiskSpd-backed benchmarking** — current custom I/O is incomparable to CrystalDiskMark
4. **Velopack auto-updates** — current UpdateService only checks, doesn't download/apply
5. **Partition merge** — every commercial tool supports merging adjacent NTFS volumes
6. **.NET 10 Fluent theme** — eliminate ~830 lines of custom theme XAML
7. **Standalone disk initialization** — new/raw disks need a clear initialization workflow
8. **Multi-pass wipe patterns** — DoD 5220.22-M expected by security-conscious users
9. **Benchmark result export** — results are display-only, no save/compare
10. **Non-admin diagnostic mode** — read-only disk info without requiring elevation

## Product Map
- Core workflows: inspect physical disks/partitions/volumes; create/resize/extend/split/format/delete/change letters; capture/compare/export partition snapshots; run SMART/alignment/benchmark/surface/wipe/boot/dev-drive tools; mount/create VHD/VHDX/ISO and clone/restore disk images; disk usage analysis with treemap.
- User personas: Windows power users, homelab operators, help desk technicians, IT admins, recovery-focused users.
- Platforms and distribution: Windows 10/11, elevated WPF desktop app targeting `net10.0-windows`, self-contained `win-x64`, Inno Setup installer, GitHub Actions CI, GitHub Releases update check.
- Key integrations: WMI Storage/CIM/BitLocker providers (`Services/WmiDiskService.cs`); DiskPart/PowerShell/native process execution (`Services/ProcessRunner.cs`); partition snapshots (`Services/PartitionTableBackup.cs`); volume locking via FSCTL (`Services/VolumeLockService.cs`); NVMe sanitize via IOCTL (`Services/SecureEraseService.cs`).

## Competitive Landscape

### GParted
Does well: pending operation queue with apply/undo, detailed operation logs, filesystem-specific operations. Learn: queue every destructive change before applying — this is the single biggest safety gap in PartitionPilot. Avoid: Linux live-media assumptions.

### KDE Partition Manager / kpmcore
Does well: separates core partition library from GUI, supports broad filesystem identification, plugin architecture for filesystem backends. Learn: extract core services before CLI or alternate frontends. The existing P3 core extraction roadmap item is correct. Avoid: claiming write support for unsupported Windows filesystems.

### EaseUS Partition Master / AOMEI / MiniTool
Does well: pending operations with preview, partition merge, disk-to-disk clone, CLI automation (AOMEI), multi-pass wipe patterns (DoD 5220.22-M), non-admin read-only mode. What they paywall: partition recovery, disk-to-disk clone, dynamic disk conversion, OS migration — these are the high-value features. Learn: merge adjacent partitions, DoD wipe patterns, benchmark export, and non-admin diagnostics are all missing from PartitionPilot. Avoid: paywall-like upsell patterns, opaque "repair" semantics.

### CrystalDiskInfo / CrystalDiskMark
Does well: 30+ SMART attributes with vendor-specific decoding, NVMe health data, temperature monitoring with system tray alerts, SMART history/trending, DiskSpd-backed benchmark profiles with standardized output. Learn: current PartitionPilot SMART coverage (8 attributes from WMI StorageReliabilityCounter) is far below user expectations. DiskSpd is MIT-licensed and produces XML output — the existing roadmap item to adopt it is well-evidenced. Avoid: building a standalone SMART monitor — keep it integrated.

### DiskGenius / Paragon
Does well: partition recovery scanning, bad sector mapping with block visualization, sector-level clone, UEFI/boot repair workflows. Learn: trust comes from diagnostics and clear repair boundaries. Avoid: destructive "repair bad sectors" without strong data-loss warnings.

### WizTree
Does well: NTFS MFT-direct scanning via FSCTL_ENUM_USN_DATA for near-instant disk usage analysis (seconds vs minutes). Learn: the existing P3 MFT scanning roadmap item is well-evidenced. This is a pure speed improvement — current `Directory.EnumerateFiles` approach takes minutes on large drives. Avoid: over-investing before the basic disk usage UX is polished.

### Rescuezilla / Clonezilla
Does well: backup/restore focus, clone-compatible formats, recovery-oriented UX. Learn: make recovery artifacts obvious and exportable. The existing snapshot recovery plan export roadmap item addresses this. Avoid: turning PartitionPilot into a rescue OS.

## Security, Privacy, and Reliability

### Verified Safe
- `ProcessRunner`: PowerShell uses `-EncodedCommand`, diskpart scripts use temp files with finally-block cleanup, `SanitizeLabel` strips shell metacharacters, `EscapePowerShellString` wraps in single quotes, `ValidateNativePathArgument` rejects quotes/control chars, filesystem and allocation unit allowlists enforced.
- WMI: `WqlStringLiteral` helper for proper WQL escaping, `SanitizeProviderMessage` redacts local paths from error messages, consistent try/catch with `LogWmiFailure`.
- Volume operations: `VolumeLockService.RequireLock` fails closed when exclusive access unavailable, `OperationCleanupScope` provides reliable cleanup for temp VHD attachments and EFI access paths.
- NVMe sanitize: gated by disk capability check (`CanSanitizeDisk`), Windows build check, physical disk bus type verification, double confirmation dialog.
- BitLocker: comprehensive preflight system blocks mutations on protected volumes, blocks reads on locked volumes, adds stronger confirmations for destructive operations on encrypted data.
- Recovery partition: `GuardRecoveryPartitionOperationAsync` refuses delete/extend on Recovery partitions, records `reagentc /info` for diagnostics.
- Dependencies: `dotnet list package --vulnerable --include-transitive` reports clean. NuGet audit enabled at `low` level. Package restores locked via `RestorePackagesWithLockFile`.

### Risks Found
- `WmiDiskService.cs`: Every WMI query creates a new `ManagementScope` and calls `.Connect()`. On systems with many disks, tab switching triggers 4-6 WMI connections per refresh. A cached scope pattern would reduce connection overhead and improve responsiveness.
- `ToolsViewModel.cs` and `DiskCloningViewModel.cs`: `GetBitLockerProtectedTargetsAsync` is duplicated — both methods query partitions, fetch BitLocker status, and filter. A shared helper on WmiDiskService would reduce duplication and ensure consistent behavior.
- `SmartData.cs`: Wear thresholds (85%=Warning, 95%=Critical) treat the `Wear` field as "percent used" — this matches WMI `MSFT_StorageReliabilityCounter.Wear` documentation. Verified correct after the threshold fix in commit `0df585f`.
- `ThemeService.cs`: Settings writes to ProgramData catch and swallow exceptions silently — expected behavior when non-elevated, but could benefit from logging the failure path for support bundle diagnostics.

### Missing Guardrails
- No guard for Storage Spaces pools — operations on pooled disks could break pool integrity. WMI `MSFT_StoragePool` can detect pools.
- No explicit disk initialization workflow — uninitialized disks (no partition table) require users to know to wipe first. A "Initialize Disk" action with GPT/MBR choice would be safer.
- Benchmark results are ephemeral — no export or history. Users can't compare before/after an optimization or verify a new drive's performance baseline.

## Architecture Assessment

### Strengths
- Clean MVVM separation with testable `IDialogService`, `IProcessRunner`, `IWmiDiskService` interfaces.
- Consistent `IsBusy` + `CancellationTokenSource` pattern across all ViewModels.
- `OperationCleanupScope` provides reliable cleanup with recovery hints — better than most OSS partition tools.
- Good test coverage of safety-critical paths: ProcessRunner validation (106 lines), BitLocker preflight (57 lines), PartitionTableBackup (102 lines), DiskCloning preflights (143 lines).

### Areas for Improvement
- `Services/WmiDiskService.cs`: Single file handling all WMI queries (583 lines). Well-organized but will grow with LibreHardwareMonitorLib SMART and Storage Spaces detection. Extract to `PartitionPilot.Core` before adding new query surfaces.
- `ViewModels/ToolsViewModel.cs`: 1325 lines — the largest file. Contains MBR→GPT, FAT32→NTFS, filesystem check, optimize, wipe (3 modes), boot repair, surface test, benchmark, Dev Drive. Each could be a focused tool service. Operation queue will touch most of these.
- `ViewModels/PartitionsViewModel.cs`: 837 lines of partition operations. Operation queue will significantly restructure this — plan the queue architecture before any operation-specific changes.
- `Controls/TreemapControl.cs`: Custom `FrameworkElement` with hardcoded palette, mouse-only selection, no automation peer, no keyboard navigation. The existing P2 accessibility roadmap item is correct and important.
- `Controls/DiskBarControl.xaml.cs`: Sets `AutomationProperties.Name` on Border elements, which is good, but segments are not focusable/selectable — they're visual-only with tooltips.
- No UI automation test project — only unit tests. Deterministic simulation mode (existing roadmap item) is prerequisite for reliable UI testing.

### Test Gaps
- No tests for disk initialization flow (doesn't exist yet).
- No tests for partition merge (doesn't exist yet).
- No WPF UI automation smoke tests (existing roadmap item).
- No performance/stress tests for WMI connection overhead.

## Rejected Ideas

- **Full Linux filesystem write support**: PartitionPilot is Windows-focused; identification and guarded read-only handling is sufficient. Source: KDE/GParted filesystem lists.
- **Bootable rescue media / WinPE builder**: Clonezilla/Rescuezilla own that workflow. Adding a separate OS/distribution surface would dilute focus. Source: Clonezilla docs, Macrorit WinPE feature.
- **Automatic destructive snapshot restore**: Premature until operation queue, disk identity checks, and recovery-plan export exist. Source: `PartitionTableBackup.BuildRecoveryCommands()`.
- **VDS-first rewrite**: Microsoft documents VDS as superseded by Storage Management API. Keep as later exploration only. Source: Microsoft VDS transition docs.
- **Plugin ecosystem**: The app is a high-risk local admin disk tool with no stable core/plugin boundary. Source: current single-project architecture.
- **Mobile or multi-user/cloud features**: Product is a local elevated Windows desktop utility. Source: README platform claims, `net10.0-windows` TFM.
- **Dynamic disk management**: Deprecated technology. Microsoft recommends Storage Spaces instead. Source: Microsoft deprecation notice.
- **Disk scheduling / task automation**: Feature creep for a disk admin tool. CLI companion (existing roadmap item) covers scripted automation needs. Source: AOMEI scheduler feature (rarely used per community feedback).
- **Built-in file recovery / undelete**: This is a separate product category (Recuva, TestDisk, R-Studio). Adding it would bloat scope and create liability. Source: r/DataHoarder consensus.
- **Keyboard shortcuts**: Project rule prohibits them. Source: `Roadmap_Blocked.md`.

## Sources

### Repo
- https://github.com/SysAdminDoc/PartitionPilot

### OSS Tools
- https://gparted.org/display-doc.php?name=help-manual
- https://github.com/KDE/partitionmanager
- https://github.com/KDE/kpmcore
- https://github.com/GNOME/gnome-disk-utility
- https://clonezilla.org/
- https://rescuezilla.com/
- https://github.com/pbatard/rufus
- https://github.com/microsoft/diskspd
- https://github.com/hiyohiyo/CrystalDiskInfo
- https://github.com/smartmontools/smartmontools
- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- https://github.com/awesome-foss/awesome-sysadmin

### Commercial Tools
- https://www.easeus.com/partition-manager/epm-free.html
- https://www.diskpart.com/help/cmd.html
- https://www.partitionwizard.com/
- https://www.diskgenius.com/
- https://www.paragon-software.com/us/home/hdm-windows/
- https://www.wiztreefree.com/

### Platform & Dependencies
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-partition
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_lock_volume
- https://learn.microsoft.com/en-us/windows/compatibility/vds-is-transitioning-to-windows-storage-management-api
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control
- https://www.nuget.org/packages/CommunityToolkit.Mvvm
- https://www.nuget.org/packages/System.Management/
- https://github.com/velopack/velopack
- https://github.com/FlaUI/FlaUI
- https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations

## Open Questions
- Needs live validation: exact Administrator Protection behavior on the target Windows 11 channel, especially elevated profile paths and update/snapshot locations.
- Needs live validation: whether LibreHardwareMonitorLib admin ring driver requirement conflicts with PartitionPilot's existing elevation model on Windows 11 24H2+.
- Needs operator decision: whether to ship unsigned public artifacts before Azure Trusted Signing is unblocked, or keep artifacts internal until signing is available.

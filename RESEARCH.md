# Research — PartitionPilot

## Executive Summary

PartitionPilot is a native Windows .NET 10 WPF disk administration console for power users and IT admins. At v0.8.0 it has shipped most parity features: queued partition operations with crash-recovery journals, VSS-backed image capture, SMART history with trend alerts, temperature monitoring, DiskSpd benchmarks, sector cloning, lost-partition scanning, MFT disk usage analysis, i18n infrastructure, CLI companion with plan/apply, and CI provenance with SBOMs. The codebase is well-architected (Core/GUI/CLI split, interface-driven services, 231 tests) and occupies a genuine market gap — no established open-source tool combines partition management + cloning + SMART health monitoring as a native Windows app with a modern dark-mode UI.

**Top opportunities, in priority order:**

1. Add post-clone verification checksum to catch incomplete or corrupt clones.
2. Add bad-sector rescue mode so sector clones degrade gracefully instead of failing hard.
3. Expose SMART self-test triggers (short/extended) — table-stakes for health monitoring.
4. Surface the full NVMe health log (unsafe shutdowns, controller busy time, error log entries, critical warning bits, per-sensor temperatures).
5. Show BitLocker encryption progress and method (XTS-AES-128 vs AES-256) using existing WMI APIs.
6. Log journal-save failures in OperationQueue instead of silently swallowing them.
7. Expose DiskSpd benchmarks and SMART history from the CLI companion.
8. Add a filesystem support matrix view showing which operations each filesystem supports.
9. Enable FAT32 formatting above 32 GB (standard Windows cap users constantly hit).
10. Add SMART diagnostic report export for IT support workflows.

## Product Map

- **Core workflows:** Inspect disks/partitions/volumes; queue and apply partition operations (create, delete, format, resize, split, merge, change letter, hide, extend); inspect SMART health, temperature, alignment; run repair, wipe (single/DoD3/DoD7/NVMe sanitize), benchmark, boot repair, Dev Drive, MBR-to-GPT conversion tools; create/restore WIM/VHDX images with VSS; sector-clone disks; scan for lost partitions; review snapshots, disk usage treemap, support bundles, activity logs.
- **User personas:** Windows power users, homelab operators, help desk technicians, endpoint/server admins, and recovery-oriented users who value transparent safeguards over consumer upsell.
- **Platforms and distribution:** Windows 10/11, elevated WPF desktop app targeting `net10.0-windows`, self-contained `win-x64`, Inno Setup installer, Velopack auto-updates via GitHub Releases, CLI companion `pp.exe`, CI build/test/publish with artifact attestation and CycloneDX SBOMs.
- **Key integrations:** WMI Storage/CIM/BitLocker providers via `WmiDiskService.cs`; LibreHardwareMonitorLib for extended SMART; native diskpart, PowerShell, DISM, DiskSpd, cipher, chkdsk, bcdboot, mbr2gpt via `ProcessRunner.cs`; ProgramData-based persistence (snapshots, journals, SMART history); Velopack + GitHub Releases for updates.

## Competitive Landscape

### GParted
The gold standard for pending-operation-queue UX. Broadest open-source filesystem support (28 filesystems with per-operation granularity). Learn: the filesystem support matrix dialog is a first-class feature — users should see what operations are available per FS. Pre-copy filesystem signature erasure prevents ghost detection on clone targets. Avoid: Linux/live-media assumptions and write support for Windows-unsupported filesystems.

### AOMEI / EaseUS / MiniTool
Strong Windows partition UX with wizards, full operation queues, and CLI automation (AOMEI's `partassist.exe`). OS migration and WinPE builder are universally paywalled — the highest-value commercial features. Chinese origin drives privacy-conscious users to seek alternatives. Learn: CLI write automation with structured plan/apply is table-stakes. FAT32 >32GB formatting is a common free feature. Avoid: upsell dark patterns, broad PC-optimization scope creep.

### DiskGenius
Deepest clone/recovery feature set: hex sector viewer, resumable sector copy with bad-sector threshold adjustment, auto-TRIM before cloning, virtual RAID reconstruction, ~200 file type recovery signatures. v6.2.0 (2026) added disk speed test and boot repair. Learn: clone tools need explicit error policy, rescue modes, and verification. The free hex sector viewer (read-only) is a power-user differentiator. Avoid: turning PartitionPilot into a full data-recovery suite.

### CrystalDiskInfo / HD Sentinel
CrystalDiskInfo: complete NVMe health log display (20+ attributes), vendor-specific SMART decoding via lookup tables, tray alerts + email + sound notifications, graph history. HD Sentinel: multiplicative health scoring algorithm, remaining lifetime estimation from writes vs rated TBW, SMART self-test trigger (short/extended/conveyance), surface testing with write-repair, email/SMS/popup/external-app alerts. Learn: SMART self-test triggering is a gap — PartitionPilot reads but cannot initiate tests. TBW endurance gauges give users actionable remaining-life estimates. Avoid: becoming a tray-first background monitor.

### Clonezilla / Rescuezilla / Partclone
Partclone: filesystem-aware cloning (copies only used blocks — dramatically faster than raw sector copy). Clonezilla: XXH128 checksum verification during imaging, `-rescue` mode (continue on read error, log bad sectors), gocryptfs image encryption, multicast network cloning, 4Kn-to-512n disk conversion. Rescuezilla: GUI frontend with Clonezilla interop, VDI/VMDK/VHDx/QCOW2 format support, automated end-to-end test suite. Learn: clone verification and bad-sector rescue are the two biggest missing pieces in PartitionPilot's sector clone. Avoid: bootable rescue OS scope.

### smartmontools
The most comprehensive SMART coverage: ATA/SATA/SCSI/SAS/NVMe, self-test execution (short/extended/conveyance/selective), 3000+ vendor-specific attribute mappings in drivedb.h, JSON output, USB bridge passthrough, RAID controller support. Learn: self-test triggers and the device database for vendor-specific attribute names are features PartitionPilot should adopt incrementally. Avoid: duplicating smartctl's complete device support matrix.

### Macrium Reflect (Discontinued)
Macrium Reflect Free was retired January 2024, leaving a vacuum in trusted free Windows imaging/cloning. No single tool has filled this gap. PartitionPilot's clone + image + SMART combination positions it as a potential successor for users who need a trustworthy open-source alternative.

## Security, Privacy, and Reliability

- **OperationQueue journal save failures silenced:** `src/PartitionPilot.Core/Services/OperationQueue.cs` lines 85, 95, 105, 119 — four `try { await OperationJournalService.SaveAsync(journal); } catch { }` blocks silently swallow journal write failures. If the journal directory is unwritable or disk full, the user gets no indication that crash-recovery protection has failed. These should log warnings.
- **27 empty catch blocks total:** Most are legitimate cleanup (file deletion, process termination in finally blocks), but several in `ThemeService.cs` (lines 110, 127, 136), `SmartHistoryService.cs` (lines 172, 179, 193, 232, 237), and `PartitionTableBackup.cs` (lines 108, 157) could mask failures during settings/data persistence. Diagnostic logging would aid troubleshooting without changing behavior.
- **Sector clone lacks post-copy verification:** `SectorCloneService.cs` writes sectors but performs no checksum or byte-count verification after completion. An incomplete clone due to OS-level write caching or hardware error would be undetected. Clonezilla uses XXH128; at minimum a total-bytes-written vs total-bytes-read comparison should be enforced.
- **Sector clone has no rescue mode:** `SectorCloneService.CopyLoop` throws on any read failure. For recovery scenarios (dying source drive), a rescue mode that zeroes unreadable sectors and logs their offsets is essential. This is the most-requested feature in Clonezilla/Rescuezilla issue trackers.
- **NVMe health data partial:** `SmartQueryService.cs` queries LibreHardwareMonitor sensors, which expose a subset of the NVMe SMART/Health Information Log. Missing: Unsafe Shutdowns, Controller Busy Time, Error Information Log Entry Count, Critical Warning bit flags, Temperature Sensors 2-8, Thermal Management Transition Counts. These are available via `IOCTL_STORAGE_QUERY_PROPERTY` with `NVMeDataTypeLogPage`.
- **BitLocker shows only on/off/unknown:** `BitLockerPreflight.cs` maps `ProtectionStatus` to three states. `Win32_EncryptableVolume` exposes `ConversionStatus` (with EncryptionPercentage), `EncryptionMethod` (XTS-AES-128/256), and `LockStatus` — all useful for users managing encryption lifecycle.
- **No SMART self-test capability:** CrystalDiskInfo, HD Sentinel, and smartmontools all support triggering SMART self-tests (short/extended/conveyance). PartitionPilot is read-only for SMART data. smartctl can be invoked via ProcessRunner.

## Architecture Assessment

- **Strengths:** The Core/GUI/CLI split is clean and well-maintained. Interface-driven services (IWmiDiskService, IProcessRunner, IDialogService, IActivityLog) keep safety-critical code testable. SimulatedDiskService enables deterministic UI testing. The operation queue + journal model is more robust than GParted's in-memory-only approach. CommunityToolkit.Mvvm adoption is complete. 231 tests cover validation, SMART trends, clone boundaries, and journal lifecycle.
- **Refactor candidates:**
  - `ToolsViewModel.cs` (1478 lines) and `PartitionsViewModel.cs` (1121 lines) are the largest files — consider extracting wipe/benchmark/boot-repair orchestration into Core services.
  - `DiskCloningViewModel.cs` (735 lines) mixes VSS snapshot orchestration, DISM invocation, and robocopy orchestration — image workflow logic should move to a Core service.
  - `MainViewModel.cs` support-bundle generation (lines 280-360) is a view-model concern that belongs in a Core service.
- **Test gaps:** CLI command parsing untested; PartitionRecoveryScanner untested; no pseudo-locale completeness CI gate; no test for sector clone verification (because verification doesn't exist yet); EnvironmentDiagnostics untested; UI smoke tests run in CI but with `continue-on-error: true`.
- **Documentation accuracy:** README accurately describes v0.8.0 features. CLI section lists all commands. CLAUDE.md is current.

## Rejected Ideas

- **Full data-recovery suite (file undelete, deep scan):** TestDisk/PhotoRec and DiskGenius own this category. PartitionPilot's read-only lost-partition scanning is the right boundary. Source: TestDisk, DiskGenius.
- **Bootable rescue media / WinPE builder:** Every commercial tool paywalls this because it's high-value, but it adds a second OS/distribution surface. Defer until local release safety is mature and signing is available. Source: AOMEI, EaseUS, MiniTool, Paragon.
- **Plugin ecosystem:** Local admin disk mutation is too risky without a stable sandbox, permission model, and compatibility contract. Source: KDE kpmcore backend plugin architecture.
- **OS migration to SSD:** Universally paywalled (highest commercial signal) but requires system partition cloning + boot repair orchestration + BCD manipulation — L/XL effort with high data-loss risk. Defer until sector clone verification and rescue mode are proven. Source: AOMEI Pro, EaseUS Pro, MiniTool Pro.
- **Automatic destructive snapshot restore:** PartitionTableBackup intentionally emits non-destructive recovery guidance. Automatic restore would be unsafe until stronger disk identity checks and user consent workflows exist. Source: internal architecture.
- **Dynamic disk management:** Microsoft documents VDS (the Dynamic Disk API) as superseded. PartitionPilot already targets the Storage Management API. Source: Microsoft Learn, Roadmap_Blocked.md.
- **Virtual RAID reconstruction:** DiskGenius paywalls this. Extremely niche, requires deep RAID parity knowledge. Not aligned with PartitionPilot's transparent-safeguards philosophy. Source: DiskGenius Pro.
- **Full partition move (positional relocation):** GParted and all commercial tools support this, but on Windows it requires byte-level sector copy + filesystem fixup + boot record update — XL effort with high data-loss risk. The right enabler is proven sector clone + verification first. Source: GParted, AOMEI, EaseUS.
- **Filesystem-aware cloning (Partclone-style):** Copying only used blocks requires per-filesystem metadata parsing (NTFS bitmap, ext4 block groups, etc.). The performance win is large but the engineering surface is XL. Keep as a long-term aspiration after sector clone is battle-tested. Source: Partclone.
- **Email/SMS notifications:** Enterprise polish from Paragon/HD Sentinel. Low value for a local admin utility until there's a background-monitoring daemon. Source: Paragon, HD Sentinel.
- **Commercial-style cleanup/optimizer bundles:** AOMEI/MiniTool include junk cleaner, duplicate finder, app mover. Scope creep that dilutes the disk-operations focus. Source: AOMEI Pro, MiniTool Free.
- **Mobile, cloud, multi-user workflows:** TFM, README, and privilege model define a local Windows admin utility. Source: internal.

## Sources

### OSS Tools
- https://gparted.org/features.php
- https://gparted.org/news.php
- https://github.com/KDE/kpmcore
- https://www.cgsecurity.org/wiki/TestDisk
- https://github.com/cgsecurity/testdisk
- https://clonezilla.org/downloads/stable/changelog.php
- https://github.com/rescuezilla/rescuezilla/releases
- https://github.com/smartmontools/smartmontools
- https://github.com/smartmontools/smartmontools/blob/master/smartmontools/drivedb.h
- https://github.com/ashaduri/gsmartcontrol
- https://github.com/Thomas-Tsai/partclone
- https://github.com/nix-community/disko
- https://github.com/hiyohiyo/CrystalDiskInfo

### Commercial Tools
- https://www.aomeitech.com/pa/
- https://kb.easeus.com/partition-master/20016.html
- https://www.partitionwizard.com/
- https://www.diskgenius.com/manual/copy-sectors.php
- https://www.paragon-software.com/home/hdm-windows/
- https://www.drive-image.com/
- https://macrorit.com/disk-partition-expert/free-edition.html

### Platform & Standards
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter
- https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume
- https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_query_property
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_reinitialize_media
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
- https://csrc.nist.gov/pubs/sp/800/88/r1/final

### Community Signal
- https://alternativeto.net/software/gparted/?platform=windows
- https://www.bleepingcomputer.com/forums/t/805501/need-partition-manager/
- https://techcommunity.microsoft.com/discussions/windows11/what-is-the-best-disk-partition-software-for-windows-1110-now/4425389
- https://www.hdsentinel.com/help/en/52_cond.html
- https://diskanalyzer.com/about
- https://crystalmark.info/en/software/crystaldiskinfo/crystaldiskinfo-health-status-setting/

## Open Questions

None that block prioritization or implementation.

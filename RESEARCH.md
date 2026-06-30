# Research — PartitionPilot

## Executive Summary
PartitionPilot v0.9.5 is a Windows-only .NET 10 WPF and CLI disk administration tool for local, elevated partition, image, clone, wipe, recovery, and health workflows. Its strongest current shape is the safety-heavy Core/WPF/CLI architecture: stable disk identity checks, queued partition operations, redacted journals/support bundles, VSS-backed image capture, SMART/NVMe telemetry, verified DiskSpd download, and local installer releases. Highest-value direction: keep turning destructive disk workflows into evidence-producing, verifiable, locally testable operations. Priority opportunities: 1) keep existing P1 work on filesystem capability gates, VSS writer health, release/update verification, and recovery scan modes; 2) add whole-disk pre-destruction snapshots for image restore, clone, and wipe flows; 3) verify WIM/VHDX/encrypted user images at capture and restore time; 4) make `smartctl` availability, version, and device-mode handling visible before SMART self-tests; 5) add post-clone/restore bootability audits; 6) finish existing localization, UI smoke, WinPE rescue, Core-service extraction, drive-health metadata, and operator documentation items.

## Product Map
- Core workflows: inspect disks/partitions/volumes; queue and apply partition mutations; export partition snapshots and recovery guidance; create/restore WIM/VHDX images; run sector clones; wipe/sanitize media; repair boot/filesystems; scan for lost partitions; inspect raw sectors.
- User personas: Windows endpoint admins, repair technicians, homelab operators, and advanced users who need transparent local disk tooling without bundled cleanup/upsell workflows.
- Platforms and distribution: Windows 10/11 x64, elevated WPF GUI, `pp` CLI, `net10.0-windows`, self-contained publish, Inno Setup installer, Velopack updates from GitHub Releases.
- Key integrations and data flows: Windows Storage Management WMI (`MSFT_Disk`, partitions, volumes, storage pools), BitLocker WMI, VSS via `vssadmin`, DiskPart/PowerShell/DISM/robocopy/chkdsk/mbr2gpt/bcdboot/cipher, LibreHardwareMonitor and smartctl, ProgramData journals/snapshots/SMART history/support bundles.

## Competitive Landscape
- GParted/KDE Partition Manager: strong operation queues, filesystem capability matrices, and reusable partition-management engines. Learn from capability-as-policy and preview-first mutation; avoid Linux-only assumptions for a Windows admin app.
- Clonezilla/partclone/Rescuezilla: mature backup/restore/clone flows with used-block imaging, CRC/image checks, rescue modes, and active requests for checksum packages and automated clone tests. Learn from image verification and restore-test evidence; avoid requiring a Linux appliance for everyday Windows workflows.
- TestDisk/PhotoRec/ddrescue/forensics tools: prioritize read-only recovery, bad-sector tolerance, evidence export, and careful damaged-media handling. Existing resumable recovery-scan work remains correct; avoid expanding into full file-carving/RAID recovery.
- smartmontools/CrystalDiskInfo: turn raw SMART/NVMe data into health guidance using drive databases, JSON output, and device-specific behavior. Learn from explicit smartctl version/device-mode handling; avoid treating all USB/NVMe bridges as equivalent.
- AOMEI/EaseUS/MiniTool/DiskGenius: commercial Windows suites make resize/move/clone/recovery/boot media/health checks discoverable and often paywall migration/rescue features. Learn from workflow completeness and boot repair surfacing; avoid optimizer, junk-cleaner, app-mover, and marketing bundle drift.
- Macrium Reflect/backup products: treat image validation, rescue media, and restore drills as first-class trust features. Learn from explicit restore validation; avoid turning PartitionPilot into a scheduled backup service.
- Digler/awesome-forensics ecosystem: demonstrates plugin and forensic-image patterns, but PartitionPilot should keep plugins rejected until its Core safety policies are centralized and testable.

## Security, Privacy, and Reliability
- Verified: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` clears target disks during restore and overwrites target disks during sector clone without first saving a target partition snapshot through `PartitionTableBackup`; `src/PartitionPilot/ViewModels/ToolsViewModel.cs` whole-disk wipe paths likewise do not show snapshot creation before destructive execution.
- Verified: WIM capture/apply in `DiskCloningViewModel.CreateImageAsync` and `RestoreImageAsync` does not pass DISM `/CheckIntegrity` or `/Verify`; VHDX capture/restore uses `robocopy` and has no generated manifest/checksum for later validation.
- Verified: sector clone has optional byte verification in `src/PartitionPilot.Core/Services/SectorCloneService.cs`, but file-image workflows do not have equivalent capture/restore validation.
- Verified: `src/PartitionPilot.Core/Services/SmartTestService.cs` invokes `smartctl` and exposes `IsSmartctlAvailableAsync`; `src/PartitionPilot.Core/Services/EnvironmentDiagnostics.cs` checks DiskPart, PowerShell, DISM, VSS, chkdsk, DiskSpd cache, and WMI but not smartctl.
- Verified: after image restore or sector clone, `DiskCloningViewModel` reports success without auditing EFI System Partition, BCD, WinRE/reagentc state, or whether the restored Windows target has a boot repair recommendation. Community reports around clones and boot partition/BCD failures make this a practical reliability gap.
- Verified: current NuGet audit and outdated checks reported no vulnerable or newer packages for `src/PartitionPilot/PartitionPilot.csproj` from nuget.org; no dependency-update roadmap item is justified today.
- Verified: existing roadmap items already cover recovery scanner performance/resume, release/update verification, XAML/i18n, WinPE rescue packaging, oversized orchestration extraction, filesystem capability gates, VSS writer health, UI smoke gating, and SMART advisory metadata.

## Architecture Assessment
- Module boundaries: whole-disk destructive workflows bypass the partition-operation queue's snapshot habits; add a Core service that saves pre-destruction target evidence for restore, clone, sanitize, and wipe callers.
- Module boundaries: image capture/restore validation should be a Core image service, not more WPF-only orchestration in `DiskCloningViewModel`; this also supports CLI parity when image commands are added later.
- Refactor candidates remain `src/PartitionPilot/ViewModels/ToolsViewModel.cs` (1504 lines), `src/PartitionPilot/ViewModels/PartitionsViewModel.cs` (1181), `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` (868), `src/PartitionPilot.Cli/Program.cs` (848), and `src/PartitionPilot.Core/Services/WmiDiskService.cs` (820).
- Test gaps: unit coverage is strong for layout diffs, journals, encryption, SMART history, sector clone, DiskSpd, and update parsing, but image verification, smartctl diagnostics, bootability audits, and whole-disk pre-destruction snapshot behavior need direct tests.
- Documentation gaps: existing roadmap already captures layout specs, encrypted image format, release verification, and recovery scan modes; new image-integrity work should add only the minimal README/CLAUDE notes tied to the implementation.
- Category coverage: security/data safety are addressed by snapshot and image verification items; accessibility/i18n/testing/docs/distribution are already represented in current roadmap; observability is addressed through diagnostics/support-bundle evidence; plugin ecosystem, mobile, and multi-user remain rejected; migration/upgrade strategy is covered by release/update verification and image format docs.

## Rejected Ideas
- Full file undelete, carving, and RAID reconstruction: TestDisk/PhotoRec/Digler/forensics tools cover this; PartitionPilot should keep lost-partition scanning read-only and evidence-oriented.
- Linux rescue appliance as the primary distribution: existing WinPE-compatible rescue work is a better fit for a Windows-only elevated tool.
- Cloud backup, scheduled backup service, or multi-user management portal: Macrium/Acronis-style backup-suite behavior conflicts with PartitionPilot's local admin tool shape.
- Plugin ecosystem for disk operations: Digler-style plugins are interesting, but external mutation plugins would widen the trust boundary before Core capability gates are centralized.
- Third-party write support for ext/APFS/HFS+/LUKS: GParted can rely on Linux filesystem tools; PartitionPilot should detect and guard these on Windows rather than bundling risky write drivers.
- Commercial optimizer/duplicate-file/app-mover features: AOMEI/MiniTool expose them, but they dilute the disk safety and recovery focus.
- VDS COM API: already rejected in `Roadmap_Blocked.md` because Microsoft superseded VDS with Storage Management APIs.
- Keyboard shortcuts and remote build workflows: rejected by repo policy and blocked-roadmap state.

## Sources
OSS and adjacent:
- https://gparted.org/features.php
- https://github.com/KDE/partitionmanager
- https://github.com/KDE/kpmcore
- https://clonezilla.org/clonezilla-live/doc/01_Save_disk_image/advanced/09-advanced-param.php
- https://clonezilla.org/clonezilla-live/doc/02_Restore_disk_image/advanced/09-advanced-param.php
- https://partclone.org/features/
- https://github.com/rescuezilla/rescuezilla
- https://github.com/rescuezilla/rescuezilla/issues/441
- https://github.com/rescuezilla/rescuezilla/issues/480
- https://www.cgsecurity.org/wiki/TestDisk
- https://www.gnu.org/software/ddrescue/manual/ddrescue_manual.html
- https://cugu.github.io/awesome-forensics/
- https://github.com/ostafen/digler
- https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5
- https://github.com/smartmontools/smartmontools/blob/master/smartmontools/drivedb.h

Commercial and community:
- https://www.aomeitech.com/pa/standard.html
- https://www.easeus.com/partition-manager/
- https://www.partitionwizard.com/free-partition-manager.html
- https://www.diskgenius.com/manual/
- https://kbx.macrium.com/macrium-reflect-x/validating-backups-images-can-be-restored
- https://superuser.com/questions/347693/clonezilla-verify-image-fails

Windows platform, dependencies, and advisories:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-overview
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/vssadmin-list-writers
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-image-management-command-line-options-s14?view=windows-11
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-create-usb-bootable-drive?view=windows-11
- https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume
- https://docs.velopack.io/integrating/overview
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
- https://api.xunit.net/v3/3.0.1/v3.3.0.1-Xunit.Assert.SkipWhen.html

## Open Questions
None that block prioritization. Code-signing credentials, WinPE ADK availability, and any smartctl redistribution choice are implementation prerequisites that can be handled during the relevant roadmap items.

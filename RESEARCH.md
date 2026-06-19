# Research — PartitionPilot

## Executive Summary
PartitionPilot is a Windows-only .NET 10 WPF disk administration console for power users and IT administrators, with a companion read-only CLI, a separated `PartitionPilot.Core`, queued partition changes, SMART monitoring, DiskSpd benchmarking, snapshots, support bundles, WIM/VHDX workflows, disk usage scanning, and sector cloning. Its strongest current shape is the integrated safety-first storage console: most earlier parity gaps are already shipped in v0.7.0. Highest-value direction: fix remaining release/data-safety root causes before adding more tools. Top opportunities: 1) fix installer/project version drift that breaks CI release validation, 2) make sector clone fail closed and add verification/rescue behavior, 3) persist queued-operation journals for crash recovery, 4) sign release artifacts and broaden provenance/SBOM coverage, 5) make image capture VSS-backed, 6) activate deterministic WPF UI/accessibility tests in CI, 7) version persisted JSON artifacts, 8) make Storage Spaces membership/health diagnostics precise, 9) move the CLI from read-only inventory to guarded plan/apply automation, 10) finish i18n coverage beyond shell strings.

## Product Map
- Core workflows: inspect disks/partitions/volumes; queue and apply partition operations; inspect SMART/temperature/alignment; run repair, wipe, benchmark, boot, Dev Drive, and conversion tools; create/restore images and sector clones; review snapshots, disk usage, support bundles, and logs.
- User personas: Windows power users, homelab operators, help desk technicians, endpoint/server admins, and recovery-oriented users who need transparent safeguards more than consumer upsell.
- Platforms and distribution: Windows 10/11, elevated WPF desktop app targeting `net10.0-windows`, self-contained `win-x64`, Inno installer, Velopack updates from GitHub Releases, CLI companion `pp.exe`, CI build/test/publish artifacts.
- Key integrations and data flows: WMI Storage/CIM/BitLocker providers in `src/PartitionPilot.Core/Services/WmiDiskService.cs`; native `diskpart`, PowerShell, DISM, robocopy, DiskSpd, cipher, chkdsk, bcdboot, mbr2gpt in `ProcessRunner.cs` and view models; ProgramData/portable JSON state in `PartitionTableBackup.cs`, `SmartHistoryService.cs`, and `ActivityLog.cs`; support ZIP redaction in `src/PartitionPilot/ViewModels/MainViewModel.cs`.

## Competitive Landscape
- GParted / KDE Partition Manager: strong pending-operation model, operation detail logs, broad filesystem awareness, and clear backup/restore language. Learn: durable operation details and rollback-oriented evidence are table stakes. Avoid: Linux/live-media assumptions and claiming write support for Windows-unsupported filesystems.
- AOMEI / EaseUS / MiniTool: strong Windows UX around apply queues, wizards, merge/split, recovery, migration, diagnostics, and CLI automation. Learn: guarded CLI write automation and guided flows are valuable. Avoid: opaque repair promises, upsells, and broad claims that exceed verified implementation.
- DiskGenius: strong clone/recovery feature depth, including sector-copy controls for bad-sector handling. Learn: raw clone tools need explicit error policy, rescue modes, and verification. Avoid: turning PartitionPilot into a full data-recovery suite without the recovery engineering surface.
- Clonezilla / Rescuezilla: strong imaging/recovery positioning and community scrutiny around bad-sector behavior and backup verification. Learn: clone and image workflows must expose integrity verification and failure detail. Avoid: bootable rescue OS scope until local release safety is mature.
- CrystalDiskInfo / smartmontools: strong drive-health baseline with SMART detail, alerting, and device coverage. Learn: PartitionPilot's SMART history is on the right track; export/import and retention controls would make it more operational. Avoid: becoming a tray-first health monitor.
- DiskSpd / CrystalDiskMark-style benchmarking: strong standardized workload output. Learn: keep DiskSpd results comparable and machine-readable; release dependency hashing matters because DiskSpd is downloaded at runtime. Avoid: custom benchmark semantics that users cannot compare.
- TestDisk: strong lost-partition recovery niche. Learn: read-only lost-partition scanning is a future opportunity. Avoid: automatic destructive partition-table repair until identity checks, recovery UX, and testing are much deeper.
- WizTree: strong proof that MFT-direct scanning is the expected fast path for NTFS usage analysis. Learn: the shipped MFT scanner should keep fallback messaging and exportability clear. Avoid: overinvesting in generic file cleanup features outside disk-management scope.

## Security, Privacy, and Reliability
- Verified: `installer/PartitionPilot.iss` still declares `AppVersion=0.6.0` and `OutputBaseFilename=PartitionPilot-0.6.0-Setup`, while project files and README are v0.7.0; `.github/workflows/build.yml` validates installer metadata against the project version, so release CI is currently blocked.
- Verified: `src/PartitionPilot.Core/Services/SectorCloneService.cs` breaks out of the copy loop on failed or zero-byte `ReadFile` and still logs "Sector clone complete"; this can silently produce an incomplete clone. Add hard failure, final byte-count validation, optional verify, and rescue-mode policy.
- Verified: `src/PartitionPilot.Core/Services/OperationQueue.cs` stores pending operations only in an in-memory `ObservableCollection`; a crash or elevation/session boundary loses the user's queued operation evidence.
- Verified: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` captures live volumes directly through DISM/robocopy/VHDX flows; Microsoft VSS exists specifically to coordinate point-in-time backup consistency.
- Verified: `PartitionTableBackup.cs` writes anonymous snapshot payloads and `SmartHistoryService.cs` writes raw `List<SmartReading>` JSON without schema versions, migration policy, or corruption quarantine.
- Likely: `WmiDiskService.GetStoragePoolMembershipAsync()` maps non-primordial pools imprecisely by assigning the first pool name to every non-auto-select physical disk; Storage Spaces exposes pool health, operational status, and read-only reasons that should be surfaced precisely.
- Verified: CI generates SHA256SUMS and attests only `./publish/PartitionPilot.exe`; it does not sign the GUI, CLI, installer, updater packages, or attach an SBOM. NuGet vulnerability checks are clean today, but release trust is incomplete for an elevated disk utility.
- Verified: `tests/PartitionPilot.UiTests/SmokeTests.cs` has all tests skipped, and `.github/workflows/build.yml` runs only unit tests. UIA coverage is therefore manual despite WPF custom controls and destructive-action surfaces.

## Architecture Assessment
- Strengths: the `PartitionPilot.Core` split is the right boundary for services, CLI, and tests; interfaces for WMI, dialogs, logs, and process execution keep safety logic testable; simulation mode exists and is the right foundation for deterministic UI tests.
- Refactor candidates: move image/clone orchestration out of `DiskCloningViewModel.cs` into a core service; turn `OperationQueue.cs` into a serializable operation model plus execution journal; wrap snapshot/history/support data in versioned envelopes; extract support-bundle generation from `MainViewModel.cs`; tighten Storage Spaces association queries in `WmiDiskService.cs`.
- Test gaps: UI tests are skipped; no test asserts sector clone incomplete-read failure; no crash-recovery/journal tests; no persistence migration tests; no CI gate for installer metadata outside the existing workflow script; no pseudo-locale/resource completeness gate.
- Documentation gaps: README accurately lists many v0.7.0 features but still describes the CLI as "scripted disk management" while `src/PartitionPilot.Cli/Program.cs` exposes read-only inventory/health/snapshot commands only.

## Rejected Ideas
- Full Linux/macOS filesystem write support: KDE/GParted support broad filesystems, but PartitionPilot is a Windows storage tool; keep identification and guarded read-only handling.
- Bootable rescue media / WinPE builder now: EaseUS/AOMEI paywall this because it is high-value, but it adds a second OS/distribution/recovery surface before local release safety is mature.
- Automatic destructive snapshot restore: `PartitionTableBackup.cs` intentionally emits non-destructive recovery guidance; automatic restore would be unsafe until durable journals and stronger disk identity checks exist.
- Full file undelete suite: TestDisk/PhotoRec and DiskGenius own that product category; add read-only lost-partition scanning later, not file recovery.
- Plugin ecosystem: local admin disk mutation is too risky without a stable sandbox, permission model, and compatibility contract.
- Mobile, cloud, or multi-user workflows: current TFM, README, and privilege model define a local Windows admin utility.
- Dynamic disk conversion priority: commercial tools advertise it, but PartitionPilot already targets modern Storage Management APIs and Storage Spaces warnings; dynamic-disk work is lower value than precise Storage Spaces diagnostics.
- Commercial-style cleanup/optimizer bundles: AOMEI/MiniTool include broad PC optimization, but PartitionPilot should stay focused on transparent disk operations, diagnostics, and recovery evidence.

## Sources
### Repo
- https://github.com/SysAdminDoc/PartitionPilot

### OSS and Community Tools
- https://gparted.org/display-doc.php%3Fname%3Dhelp-manual
- https://gparted.org/display-doc.php%3Fname%3Dgparted-live-manual
- https://github.com/KDE/partitionmanager
- https://github.com/KDE/kpmcore
- https://www.cgsecurity.org/wiki/TestDisk
- https://clonezilla.org/fine-print-live-doc.php?path=clonezilla-live%2Fdoc%2F98_ocs_related_command_manpages
- https://github.com/rescuezilla/rescuezilla
- https://www.smartmontools.org/
- https://sourceforge.net/projects/crystaldiskinfo/
- https://github.com/microsoft/diskspd/wiki
- https://diskanalyzer.com/

### Commercial Windows Tools
- https://www.aomeitech.com/pa/
- https://www.diskpart.com/help/cmd.html
- https://kb.easeus.com/partition-master/20016.html
- https://www.partitionwizard.com/
- https://www.diskgenius.com/
- https://www.diskgenius.com/manual/copy-sectors.php

### Platform, Security, and Dependencies
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/storage-management-api-classes
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagepool
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea
- https://learn.microsoft.com/en-us/windows/win32/fileio/file-buffering
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/volume-shadow-copy-service
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations
- https://github.com/CycloneDX/cyclonedx-dotnet
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
- https://csrc.nist.gov/pubs/sp/800/88/r1/final
- https://github.com/microsoft/winget-create

## Open Questions
None that block prioritization or implementation.

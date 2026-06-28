# Research - PartitionPilot

## Executive Summary
PartitionPilot is a native Windows .NET 10 WPF disk administration console for power users and IT administrators, combining partition operations, SMART/NVMe health, imaging, cloning, recovery scanning, CLI automation, and local release packaging. Verified: v0.9.4 has shipped most parity gaps from prior research, fails closed on unsafe declarative layout spec fields, keeps matching layout plans idempotent, and streams encrypted images with bounded memory, so the highest-value direction is no longer feature breadth; it is hardening the remaining dangerous edges of shipped automation and making recovery, update, localization, and identity workflows reliable at real disk scale. Top opportunities: replace sector-by-sector recovery scanning with bounded/resumable scan modes; add stable disk identity to destructive confirmations and persisted plans; add release/update artifact verification; finish localization coverage for hardcoded XAML and automation names; add a WinPE/rescue distribution profile; and extract oversized view-model orchestration into Core services.

## Product Map
- Core workflows: inspect disks/partitions/volumes; queue partition mutations; run SMART/NVMe health, self-tests, history, temperature, I/O counters, alignment, benchmark, repair, wipe, boot repair, Dev Drive, image, clone, snapshot, and recovery-scan workflows; automate via `pp.exe`.
- User personas: Windows power users, help-desk technicians, homelab operators, endpoint admins, and recovery-minded users who prefer transparent local tooling over upsell-driven partition suites.
- Platforms and distribution: Windows 10/11, admin-required WPF GUI, `pp.exe` CLI, `net10.0-windows`, self-contained `win-x64`, Inno Setup installer, Velopack update path, local build/release policy.
- Key integrations and data flows: WMI Storage/CIM/BitLocker providers in `src/PartitionPilot.Core/Services/WmiDiskService.cs`; DiskPart/PowerShell/DISM/robocopy/DiskSpd/smartctl through `ProcessRunner`; ProgramData persistence for logs, journals, snapshots, SMART history, and TBW settings; GitHub Releases for updates.

## Competitive Landscape
- GParted / KDE Partition Manager: mature pending-operation UX and granular filesystem support matrices. Learn from their conservative operation preview and capability visibility. Avoid Linux/live-media assumptions as the default user path.
- AOMEI, EaseUS, MiniTool: strong Windows wizard flows, WinPE media, OS migration, partition recovery, and command-line automation. Learn that rescue media and migration are high-value features. Avoid upsell bundles, cleanup utilities, and paywall-driven UX.
- DiskGenius: deep clone/recovery tooling, sector editor, bad-sector handling, virtual RAID, and partition recovery. Learn from its explicit recovery and low-level inspection affordances. Avoid turning PartitionPilot into a full undelete/RAID reconstruction suite.
- Clonezilla / Rescuezilla / Partclone: proven imaging, rescue behavior, checksum verification, and filesystem-aware copy paths. Learn from bounded recovery modes, checksums, and bootable rescue distribution. Avoid requiring a separate Linux environment for normal Windows workflows.
- smartmontools / CrystalDiskInfo / HD Sentinel: broad SMART/NVMe decoding, self-tests, history, health scoring, and alerts. PartitionPilot now has much of this; the next lesson is vendor database depth and evidence export rather than more dashboard widgets.
- Windows built-in storage tooling: Disk Management, Storage Management API, BitLocker, VSS, Dev Drive, and DiskPart remain the safest backend contract for Windows-only mutation. Learn from platform identity fields and capability reporting. Avoid deprecated VDS/dynamic-disk investment.
- New small OSS Windows tools: OpenPart, Parq, clonr, and driveforge show renewed interest in no-telemetry Windows disk tools, but their public surface is young. PartitionPilot's advantage is its broader tested WPF/Core/CLI architecture; it should protect that lead with safety and release trust.

## Security, Privacy, and Reliability
- Resolved in v0.9.2: `src/PartitionPilot.Core/Services/LayoutDiffService.cs` now validates and normalizes `PartitionLayoutSpec.Style`, `PartitionSpec.SizeMB`, and `PartitionSpec.DriveLetter` before any DiskPart script is emitted. Invalid or injection-shaped JSON values return clear CLI errors before disk lookup or execution.
- Resolved in v0.9.3: `LayoutDiffService.ComputeDiff` now returns no changes for matching layouts, creates only missing partitions when an existing prefix matches the spec, and blocks destructive populated-disk replacement unless `--replace` is supplied.
- Resolved in v0.9.4: `src/PartitionPilot.Core/Services/ImageEncryptionService.cs` now writes chunked `PPENC2` encrypted images with bounded memory and authenticated per-chunk tags while preserving `PPENC1` read compatibility.
- Verified: `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs` steps by 512 bytes across the whole disk and reads 4096 bytes per step. A 1-4 TB disk produces billions of reads. The scanner needs fast/common-boundary and deep/resumable modes before users rely on it during recovery.
- Verified: `src/PartitionPilot.Core/Models/DiskInfo.cs` stores disk number, friendly name, size, style, and pool state but not `MSFT_Disk.UniqueId`, serial, path, bus type, or partition-table GUID. Destructive confirmations and persisted plans should anchor on stable identity, because Windows disk numbers can change across boots and attach order.
- Verified: `installer/PartitionPilot.iss` has no SignTool integration and `UpdateService` accepts GitHub/Velopack release metadata without a project-level hash/signature verification gate. NuGet reports no vulnerable packages for the GUI project on the current source, but release/update trust still depends on local artifact hygiene.
- Verified: many user-visible XAML strings and `AutomationProperties.Name` values remain hardcoded in `MainWindow.xaml`, `Views/*.xaml`, and `Dialogs/*.xaml` despite `.resx` resources. This limits i18n and screen-reader localization.

## Architecture Assessment
- Strengths: Core/GUI/CLI split is real; service interfaces keep storage operations testable; WMI failures are logged; operation journals exist; destructive operations are queued in the GUI; release docs now match the local-build policy; `rtk dotnet list ... package --vulnerable --include-transitive` found no vulnerable GUI packages.
- Boundary improvements: move layout validation/diff execution from `src/PartitionPilot.Cli/Program.cs` and `LayoutDiffService.cs` into a Core planning service that can be shared by CLI and GUI import/export flows.
- Refactor candidates: `ToolsViewModel.cs` (1477 lines), `PartitionsViewModel.cs` (1141 lines), `DiskCloningViewModel.cs` (839 lines), `WmiDiskService.cs` (777 lines), and `Program.cs` (767 lines) are large enough to hide safety regressions; extract orchestration for wiping, imaging, cloning, layout plans, and support bundles into Core services with focused tests.
- Test gaps: CLI command parsing and `PartitionRecoveryScanner` still need focused coverage; CLI parsing is implemented in top-level `Program.cs`, which makes command behavior hard to unit test.
- Documentation gaps: README lists rich features but does not document layout-spec schema, encryption container limitations/migration, release verification, or recovery-scan mode constraints.
- Category coverage: security and reliability need the P0/P1 items below; accessibility is mostly instrumented but localization of automation names is incomplete; observability should add structured recovery/layout plan logs; testing needs CLI/core unit coverage; docs need schema/release/recovery sections; distribution needs signed/verifiable artifacts and a rescue profile; plugin ecosystem, mobile, cloud, and multi-user are intentionally rejected for this local admin utility; migration/upgrade work is limited to encrypted image container compatibility and update verification.

## Rejected Ideas
- Full file undelete and deep data recovery: TestDisk/PhotoRec and DiskGenius are better references; PartitionPilot should keep read-only partition recovery and evidence export as its boundary.
- Virtual RAID reconstruction: rare, risky, and dominated by DiskGenius/R-Studio style tools; not aligned with a transparent Windows partition console.
- Third-party plugin ecosystem: local admin disk mutation needs a sandbox, permission model, and compatibility contract first; current architecture should stay closed and testable.
- Mobile, web, cloud, or multi-user management: the privilege model, WPF target, and README define a local Windows admin tool.
- Deprecated VDS/dynamic-disk expansion: Microsoft directs new work to the Storage Management API; PartitionPilot already uses the newer API.
- Commercial cleanup/optimizer bundles: AOMEI/MiniTool-style add-ons dilute the disk safety focus.
- Automatic destructive restore from snapshots: current non-destructive guidance is the right posture until stable disk identity and stronger restore simulations exist.

## Sources
### OSS and Adjacent Tools
- https://gparted.org/features.php
- https://invent.kde.org/system/kpmcore
- https://clonezilla.org/downloads/stable/changelog.php
- https://rescuezilla.com/
- https://github.com/rescuezilla/rescuezilla/releases
- https://github.com/Thomas-Tsai/partclone
- https://www.cgsecurity.org/wiki/TestDisk
- https://github.com/smartmontools/smartmontools
- https://github.com/smartmontools/smartmontools/blob/master/smartmontools/drivedb.h
- https://github.com/hiyohiyo/CrystalDiskInfo
- https://github.com/nix-community/disko
- https://docs.ansible.com/ansible/latest/collections/community/general/parted_module.html

### Commercial Tools
- https://www.aomeitech.com/pa/
- https://www.easeus.com/partition-manager/
- https://www.partitionwizard.com/free-partition-manager.html
- https://www.diskgenius.com/manual/copy-sectors.php
- https://www.diskgenius.com/manual/recover-lost-partitions.php
- https://www.paragon-software.com/home/hdm-windows/
- https://www.macrium.com/reflectfree

### Platform, Security, and Dependencies
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_query_property
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_property_query
- https://learn.microsoft.com/en-us/windows/win32/api/nvme/ns-nvme-nvme_health_info_log
- https://learn.microsoft.com/en-us/windows-server/storage/file-server/dev-drive
- https://learn.microsoft.com/en-us/dotnet/api/system.io.file.readallbytes
- https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm
- https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
- https://github.com/velopack/velopack

## Open Questions
None that block prioritization. Code signing certificate availability affects whether release trust lands as full Authenticode signing or a hash/signature verification gate first.

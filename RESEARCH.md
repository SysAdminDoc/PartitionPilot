# Research — PartitionPilot

## Executive Summary
PartitionPilot is a Windows-only .NET 10 WPF disk administration tool for power users and IT admins, with its strongest current shape in the integrated WPF shell: partition actions, snapshots, disk health, tools, images, usage scanning, cloning, activity logging, release metadata checks, and recent hardening around WMI, PowerShell, DiskPart, BitLocker, WinRE, VHD, NVMe sanitize, and volume locks. Verified: the highest-value direction is to finish the safety model that commercial and OSS partition tools treat as table stakes: queued preview-before-apply operations, richer disk health, standard benchmark methodology, update/release trust, accessible custom visualizations, exportable diagnostics, and guarded handling of unsupported partition types. Top opportunities: P1 pending operations queue; P1 richer SMART via a maintained hardware path; P1 DiskSpd-backed benchmarking; P1 update/release trust with Velopack plus attestations/checksums; P1 Administrator Protection validation; P2 accessible disk map/treemap; P2 support bundle and structured native-command records; P2 unsupported partition-type guardrails; P2 UI automation/simulation coverage; P2 `System.Management` 10.x compatibility update.

## Product Map
- Core workflows: inspect physical disks/partitions/volumes; create/resize/extend/split/format/delete/change letters; capture/compare/export partition snapshots; run SMART/alignment/benchmark/surface/wipe/boot/dev-drive tools; mount/create VHD/VHDX/ISO and clone/restore disk images.
- User personas: Windows power users, homelab operators, help desk technicians, IT admins, and recovery-focused users who need visibility and guardrails before destructive storage operations.
- Platforms and distribution: Windows 10/11, elevated WPF desktop app targeting `net10.0-windows`, self-contained `win-x64`, Inno Setup installer, GitHub Actions Windows build/test pipeline, GitHub Releases update check.
- Key integrations and data flows: WMI Storage/CIM/BitLocker providers in `src/PartitionPilot/Services/WmiDiskService.cs`; DiskPart/PowerShell/native process execution via `src/PartitionPilot/Services/ProcessRunner.cs`; partition snapshots in `%TEMP%\PartitionPilot\backups` or portable `backups`; GitHub Releases API in `src/PartitionPilot/Services/UpdateService.cs`; activity records in `src/PartitionPilot/Models/ActivityLog.cs`.

## Competitive Landscape
- GParted: does pending operation queues, visible apply flow, details logs, and cautious filesystem operations well. Learn: queue every destructive change before applying. Avoid: Linux/live-media assumptions that do not fit a Windows-native admin app.
- KDE Partition Manager/KPMcore: separates a core partition/filesystem library from the GUI and supports broad filesystem identification. Learn: extract core services before CLI or alternate shells. Avoid: claiming unsupported Windows write support for Linux/LUKS/Btrfs filesystems.
- GNOME Disks: combines disk inspection, image mounting, SMART-style health, and simple media tasks in one calm utility. Learn: keep PartitionPilot integrated and task-oriented. Avoid: hiding high-risk actions behind generic menus without explicit Windows-specific preflights.
- Rescuezilla/Clonezilla: focuses on backup, bare-metal recovery, and clone-compatible restore paths. Learn: make recovery artifacts obvious and exportable. Avoid: turning PartitionPilot into a rescue OS or boot-media product.
- EaseUS/AOMEI/MiniTool: make pending operations, undo/apply, CLI automation, boot/reboot operations, recovery, and commercial support flows visible. Learn: preview, recovery guidance, CLI, and support bundles are not optional in this category. Avoid: paywall-like upsell patterns and opaque "magic" repair copy.
- DiskGenius/Paragon: bundle partition management with disk health, bad-sector scanning, backup/restore, recovery, and UEFI/boot repair workflows. Learn: trust comes from diagnostics, warnings, and clear repair boundaries. Avoid: destructive "repair bad sectors" semantics without strong data-loss warnings.
- CrystalDiskInfo/DiskSpd/WizTree: set user expectations for SMART depth, repeatable storage benchmark profiles, and fast NTFS usage analysis. Learn: expose standard metrics and use proven engines. Avoid: custom benchmark numbers that look comparable but are not methodologically comparable.

## Security, Privacy, and Reliability
- Verified: `ProcessRunner`, WMI escaping, volume-lock fail-closed behavior, NVMe sanitize preflight, BitLocker preflights, WinRE guardrails, image destination preflights, package locks, and release metadata checks were recently improved per `CHANGELOG.md` and current source.
- Verified: `dotnet list ... --vulnerable --include-transitive` reports no vulnerable packages for both `src/PartitionPilot/PartitionPilot.csproj` and `tests/PartitionPilot.Tests/PartitionPilot.Tests.csproj` with `https://api.nuget.org/v3/index.json`.
- Verified: `System.Management` is core to WMI integration and is outdated: current `9.0.6`, latest `10.0.9`; update requires WMI compatibility smoke coverage across `WmiDiskService.cs`.
- Verified: code signing and WinGet remain blocked by external credentials/release prerequisites in `Roadmap_Blocked.md`; a public GitHub repo can still add artifact attestations, checksums, and release verification without a signing certificate.
- Verified: partition snapshots are discoverable and exportable, but `PartitionTableBackup.BuildRecoveryCommands()` intentionally emits diagnostic guidance rather than a validated recovery plan; a safer next step is mismatch-checked planning, not automatic restore.
- Verified: custom-drawn `TreemapControl` is mouse-only, uses hardcoded brushes, and has no automation peer; `DiskBarControl` sets names on visual borders but lacks selectable segment semantics. Existing roadmap accessibility work remains valid.
- Likely: support bundles and structured native-command audit records should redact serial numbers, full user profile paths, temp script paths, and exact native command arguments by default before sharing.
- Likely: Administrator Protection can change elevated profile paths and app-data assumptions; validation should cover portable mode, logs, snapshots, updates, and temp command cleanup under both legacy UAC and Administrator Protection.

## Architecture Assessment
- `src/PartitionPilot/Services/WmiDiskService.cs`: still owns WMI discovery, SMART fallback, image discovery, BitLocker status, and helper parsing. Keep hardening here, then extract to `PartitionPilot.Core` before adding the CLI.
- `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`, `DiskCloningViewModel.cs`, `DiskImagesViewModel.cs`, `ToolsViewModel.cs`: native command orchestration remains spread across view models. Operation queue, structured audit IDs, and core extraction should reduce duplicated safety logic.
- `src/PartitionPilot/Services/PartitionTableBackup.cs` and `Views/SnapshotBrowserView.xaml`: snapshot browsing is a strong recovery foundation; add disk identity/size mismatch checks and exportable recovery plans before any executable restore flow.
- `src/PartitionPilot/Controls/TreemapControl.cs` and `DiskBarControl.xaml.cs`: custom visuals need keyboard navigation, automation peers, high-contrast handling, and test coverage; this is both accessibility and release-quality work.
- `tests/PartitionPilot.Tests`: unit coverage is broad for helpers and view-model safety paths, but there is no WPF UI automation smoke project. Add a deterministic simulation/demo provider so UI tests and screenshots do not depend on the operator's live disks.
- `.github/workflows/build.yml`: build/test and metadata validation are present; release trust still lacks installer artifact upload, SHA256 manifests, provenance attestations, and verifier instructions.
- `PartitionPilot.ps1`: legacy prototype remains at repo root and still shells out to DiskPart/PowerShell; quarantine/removal remains valid to reduce user and agent confusion.

## Rejected Ideas
- Full Linux filesystem write support: rejected because PartitionPilot is Windows-focused; source: KDE/GParted broad filesystem lists. Recommend identification and guarded read-only handling instead.
- Bootable rescue media: rejected because Clonezilla/Rescuezilla already own that workflow and it would add a separate OS/distribution surface; source: Clonezilla/Rescuezilla docs.
- Automatic destructive snapshot restore: rejected until the operation queue, disk identity checks, and recovery-plan export exist; source: `PartitionTableBackup.BuildRecoveryCommands()` and commercial recovery tooling.
- VDS-first rewrite: rejected as a near-term direction because Microsoft says VDS is superseded by the Storage Management API; source: Microsoft VDS transition docs. Keep VDS only as later exploration.
- Keyboard shortcuts: rejected because project rules block shortcuts; source: `Roadmap_Blocked.md`.
- Code signing / WinGet submission now: rejected as actionable roadmap work because both require external release/signing prerequisites; source: `Roadmap_Blocked.md`.
- Plugin ecosystem: rejected for now because the app is a high-risk local admin disk tool and no stable core/plugin boundary exists; source: current single WPF project architecture.
- Mobile or multi-user/cloud features: rejected because the product is a local elevated Windows desktop utility; source: README platform claims and `net10.0-windows` target.

## Sources
### Repo
- https://github.com/SysAdminDoc/PartitionPilot

### OSS and Analogous Tools
- https://gparted.org/display-doc.php%3Fname%3Dhelp-manual
- https://github.com/KDE/partitionmanager
- https://github.com/KDE/kpmcore
- https://github.com/GNOME/gnome-disk-utility
- https://clonezilla.org/
- https://rescuezilla.com/
- https://github.com/awesome-foss/awesome-sysadmin

### Commercial and Community Tools
- https://www.easeus.com/support/download/docs/pdf/easeus_partition_master_user_guide.pdf
- https://www.diskpart.com/help/cmd.html
- https://www.partitionwizard.com/
- https://www.diskgenius.com/
- https://www.paragon-software.com/us/home/hdm-windows/
- https://www.wiztreefree.com/

### Platform, Dependencies, Security
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-partition
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/createpartition-msft-disk
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_lock_volume
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_dismount_volume
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/
- https://learn.microsoft.com/en-us/windows/compatibility/vds-is-transitioning-to-windows-storage-management-api
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control
- https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview
- https://github.com/microsoft/diskspd
- https://github.com/hiyohiyo/CrystalDiskInfo
- https://github.com/FlaUI/FlaUI
- https://www.nuget.org/packages/System.Management/
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
- https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds

## Open Questions
- Needs live validation: exact Administrator Protection behavior on the target Windows 11 rollout channel, especially elevated profile paths and update/snapshot locations.
- Needs live validation: whether the release pipeline should produce unsigned public artifacts before Azure Trusted Signing is unblocked, or keep artifacts internal until signing is available.

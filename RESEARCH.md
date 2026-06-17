# Research - PartitionPilot

## Executive Summary
PartitionPilot is a Windows-only WPF disk administration tool for power users and IT administrators, currently strongest as a polished local console over Windows Storage WMI, DiskPart, PowerShell, DISM, MBR2GPT, VHD/VHDX, disk usage, cloning, and secure-wipe workflows. The highest-value direction is not feature breadth for its own sake; it is safer execution, release trust, recovery confidence, and accessible custom visualization. Top opportunities, in order: fail closed when exclusive volume locks cannot be acquired; gate NVMe sanitize by actual device capability, not OS build alone; fix v0.3.0 release metadata drift in the Inno installer and screenshots; expose partition table snapshots as a recovery workflow; centralize confirmations through `IDialogService`; make package restore deterministic and migrate off deprecated wildcard test dependencies; add accessibility peers and keyboard selection for the treemap/disk map; export support bundles; harden WMI query construction; and retire or quarantine the legacy root `PartitionPilot.ps1`.

## Product Map
- Core workflows: inspect disks and partitions; create/delete/format/resize/extend/split partitions; run maintenance tools; inspect health and BitLocker state; clone/image disks; scan disk usage with table and treemap views.
- User personas: Windows IT administrators, homelab/power users, repair technicians, and developers creating Dev Drives or resizing storage for build workloads.
- Platforms and distribution: Windows 10/11 desktop, `.NET 10`, WPF, self-contained `win-x64`, Inno Setup installer, GitHub Actions build/test, GitHub Releases update check.
- Key integrations and data flows: `WmiDiskService` reads `MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume`, `MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter`, and BitLocker WMI; `ProcessRunner` invokes encoded PowerShell, DiskPart scripts, DISM, MBR2GPT, `bcdboot`, `chkdsk`, `defrag`, `format`, and `cipher`; `PartitionTableBackup` writes JSON snapshots before destructive partition operations.

## Competitive Landscape
- GParted and KDE Partition Manager: both reinforce that serious partition software should make dangerous changes reviewable, support broad filesystem identification, and put backup/restore warnings in the foreground. PartitionPilot should learn the pending-operation/safety posture, but avoid chasing Linux filesystem write support that does not fit a Windows admin tool.
- GNOME Disks: does a smaller set of workflows well by combining partitioning, SMART, benchmarks, and image USB flows. PartitionPilot should learn its unified "inspect, act, recover" utility shape, but avoid hiding advanced Windows-specific constraints behind oversimplified controls.
- Rescuezilla and Clonezilla: the open-source recovery space wins trust through bootable recovery, image interoperability, and clear restore paths. PartitionPilot should learn the recovery-first language and support-bundle discipline, but should not attempt a full live recovery OS in the near roadmap.
- EaseUS, MiniTool, AOMEI, DiskGenius, and Paragon: commercial tools converge on operation queues, wizards, partition recovery, bootable media, cloning, logs/error reports, and tiered destructive confirmations. PartitionPilot should learn the guardrails and support flows, while intentionally avoiding upsells, ads, bundleware, and vague "one-click safe" claims.
- CrystalDiskInfo, smartmontools, DiskSpd, CrystalDiskMark, WizTree: adjacent tools show table-stakes expectations for disk health depth, benchmark methodology, and fast NTFS usage analysis. Existing roadmap items already cover SMART expansion, DiskSpd benchmarking, and MFT-direct scanning; the new work should focus on deterministic packaging and accessible visualization around those features.
- Velopack, WPF Fluent, CommunityToolkit.Mvvm, and NuGet Audit: current ecosystem support aligns with the existing WPF/.NET 10 direction. These are evolution paths, not reasons to rewrite the frontend or abandon WPF.

## Security, Privacy, and Reliability
- Verified: `src/PartitionPilot/Services/VolumeLockService.cs` returns `null` and logs "Proceeding without lock" when a target volume cannot be opened; destructive callers such as `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, and `src/PartitionPilot/ViewModels/ToolsViewModel.cs` continue after nullable locks. Microsoft documents `FSCTL_LOCK_VOLUME` as the exclusive-access signal disk utilities use, and failed locks usually mean system/page-file/open-handle risk.
- Verified: `src/PartitionPilot/Services/SecureEraseService.cs` exposes NVMe sanitize support with only `Environment.OSVersion.Version.Build >= 22000`; Microsoft and NVMe guidance show sanitize behavior is device/driver/method dependent, so PartitionPilot needs a per-disk capability preflight before presenting block erase or crypto erase as available.
- Verified: release metadata is inconsistent: `src/PartitionPilot/PartitionPilot.csproj` is `0.3.0`, `README.md` shows a v0.3.0 badge but still references `assets/screenshots/partitionpilot-main-v0.2.3.png`, and `installer/PartitionPilot.iss` still emits `AppVersion=0.2.3` and `PartitionPilot-0.2.3-Setup`.
- Verified: `src/PartitionPilot/Services/PartitionTableBackup.cs` writes pre-operation JSON snapshots, silently catches failures, purges snapshots after 30 days, and has no UI to view, export, compare, or guide recovery. This creates a trust signal without a matching recovery workflow.
- Likely: `src/PartitionPilot/Services/WmiDiskService.cs` interpolates WQL values directly in several queries; integers are low-risk, and one image path is escaped, but `DeviceId` in the `MSFT_StorageReliabilityCounter` query is not escaped. Add one WQL literal helper and tests instead of relying on caller provenance.
- Verified: `tests/PartitionPilot.Tests/PartitionPilot.Tests.csproj` uses wildcard package versions and `xunit` v2, while NuGet now supports lock files/auditing and NuGet marks the v2 `xunit` package as legacy with future work moved to v3.
- Verified: dialogs and views still call `MessageBox.Show` directly in `src/PartitionPilot/Dialogs/*.xaml.cs` and `src/PartitionPilot/Views/PartitionsView.xaml.cs`, bypassing the existing `IDialogService`; this makes confirmation copy, accessibility, and tests inconsistent.

## Architecture Assessment
- Verified: the WPF app already has a useful MVVM/service split, but `src/PartitionPilot/ViewModels/ToolsViewModel.cs` is still a large multi-tool coordinator; future maintenance will be easier if high-risk operations are split behind services before expanding secure wipe, benchmark, and boot repair.
- Verified: custom visualization has accessibility debt. `src/PartitionPilot/Controls/TreemapControl.cs` derives from `FrameworkElement`, draws all content manually, handles only mouse clicks, and has no `AutomationPeer`; Microsoft WPF guidance says custom controls need automation peers for screen readers and UI automation. `DiskBarControl` sets names on visual borders, but its segments are not keyboard-selectable controls.
- Verified: CI at `.github/workflows/build.yml` restores, builds, and tests, but does not publish, package, verify installer metadata, produce checksums, or smoke-test the packaged app. That gap matters because the current installer version is stale.
- Verified: `PartitionPilot.ps1` remains at repo root as a large legacy PowerShell GUI/prototype. It is not part of the current WPF distribution path and can confuse users, packaging, and future agents unless it is explicitly quarantined or removed.
- Likely: `ActivityLog`, `PartitionTableBackup`, build metadata, WMI errors, update status, and selected disk/volume state are enough to generate a useful support bundle without adding third-party telemetry. This fits the app's local-admin privacy posture.

## Rejected Ideas
- Mobile companion app, source: competitor/platform scan. Rejected because Windows Storage WMI, DiskPart, admin elevation, and destructive local disk access do not map to mobile.
- Web or multi-user server mode, source: architecture scan. Rejected because this is a local elevated desktop utility; remote/shared administration would create a new threat model.
- Plugin ecosystem for arbitrary partition operations, source: adjacent tool scan. Rejected because third-party destructive-operation plugins would undermine the safety model before a stable core API and operation queue exist.
- Full Linux filesystem/LUKS/Btrfs/ZFS write support on Windows, source: GParted/KDE/blivet feature scan. Rejected for now; PartitionPilot should identify and protect unsupported/non-Windows partitions rather than attempting broad cross-platform write support.
- Full data-recovery engine, source: DiskGenius/TestDisk/Rescuezilla comparison. Rejected because partition/table recovery guidance and snapshot export are in scope, but file carving and damaged-media recovery are specialized products.
- WinPE/live-USB builder in the near term, source: AOMEI/EaseUS/Rescuezilla/Clonezilla scan. Rejected for this roadmap pass because release packaging, signing/provenance, operation safety, and recovery UX must land first.
- WPF-to-WinUI/Avalonia rewrite, source: .NET 10 WPF research. Rejected because WPF now has current Fluent/accessibility improvements and the repo already targets WPF successfully.
- Full localization immediately, source: WPF/Velopack localization scan. Rejected until user-facing strings are stabilized behind centralized dialog/resource boundaries; add i18n readiness later instead of translating hardcoded strings now.

## Sources
OSS and analogous tools:
- https://gparted.org/
- https://github.com/KDE/partitionmanager
- https://apps.gnome.org/DiskUtility/
- https://storaged.org/blivet-gui/
- https://rescuezilla.com/features
- https://clonezilla.org/
- https://github.com/LumiToad/GUIForDiskpart

Commercial competitors:
- https://kb.easeus.com/partition-master/20016.html
- https://www.partitionwizard.com/free-partition-manager.html
- https://www.diskpart.com/changelog.html
- https://www.diskgenius.com/manual/clone-disk.php
- https://www.paragon-software.com/us/free/pm-express/

Community signal:
- https://www.reddit.com/r/opensource/comments/1tepj3g/why_is_there_no_open_source_partition_manager_on/
- https://www.reddit.com/r/kde/comments/n10ry3/does_kde_have_a_qtbased_equivalent_to_gnome_disks/

Windows storage and elevation:
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_lock_volume
- https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_dismount_volume
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-partition
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/createpartition-msft-disk
- https://learn.microsoft.com/en-us/windows/win32/api/nvme/ne-nvme-nvme_secure_erase_settings
- https://learn.microsoft.com/en-us/answers/questions/1074525/nvme-sanitize-call-returns-unexpected-error-0x13d
- https://learn.microsoft.com/en-us/windows/dev-drive/
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/
- https://blogs.windows.com/windowsdeveloper/2025/05/19/enhance-your-application-security-with-administrator-protection/
- https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/settings-and-configuration

.NET, packaging, and accessibility:
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/overview
- https://velopack.io/
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages

## Open Questions
None that block prioritization. The highest-risk implementation details are verifiable locally: actual NVMe capability probing on representative hardware, Administrator Protection behavior on an enrolled Windows 11 machine, and installer/update smoke tests from packaged artifacts.

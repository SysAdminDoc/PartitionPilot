# Research - PartitionPilot

## Executive Summary
PartitionPilot v0.9.5 is a Windows-first .NET 10 WPF and CLI disk administration tool with strong recent safety work: disk identity checks, idempotent layout application, encrypted image streaming, operation journals, support-bundle redaction, SMART/NVMe telemetry, and local installer releases. The highest-value direction is to convert remaining trust surfaces from "visible guidance" into enforced, testable gates. Top opportunities: enforce filesystem-operation capability centrally; add VSS writer-health preflights before image capture; make the existing UI smoke tests runnable and fail-loud in local release validation; enrich SMART advice with curated drive/attribute metadata; continue the existing roadmap items for resumable recovery scans, release artifact verification, localization, WinPE rescue packaging, Core service extraction, and operator documentation.

## Product Map
- Core workflows: inspect disks/partitions, queue and apply partition operations, capture partition snapshots/recovery evidence, clone or image disks/volumes, wipe or sanitize media, inspect sectors, monitor disk health.
- User personas: Windows system administrators, repair technicians, homelab operators, and power users who need local, auditable disk operations rather than cloud-managed backup.
- Platforms and distribution: Windows 10/11 x64 desktop, WPF GUI, `pp` CLI, self-contained .NET 10 publish, Inno Setup installer, Velopack update integration.
- Key integrations and data flows: Windows Storage Management WMI (`MSFT_Disk`, partitions, storage pools), DiskPart/PowerShell/native tools, VSS via `vssadmin`, DISM/WIM/VHDX capture paths, BitLocker WMI, LibreHardwareMonitor/WMI/NVMe health enrichment, ProgramData journals/support bundles.

## Competitive Landscape
- GParted: does filesystem capability mapping well with per-filesystem detect/read/create/grow/shrink/move/copy/check/label support. Learn from its capability matrix; avoid Linux-only tooling assumptions in the Windows GUI/CLI path.
- KDE Partition Manager/kpmcore: separates the partition-manager UI from reusable operation logic. Learn from that boundary; avoid letting WPF view models own safety rules that the CLI must duplicate.
- Clonezilla/partclone: makes used-block, filesystem-aware imaging a default performance path with raw fallback. Learn from used-block planning; avoid making a full offline appliance the immediate product surface.
- Rescuezilla: makes rescue media and restore UX understandable to non-experts. Learn from its simple recovery flow; avoid hiding dangerous restore choices behind generic dialogs.
- TestDisk/PhotoRec/ddrescue: show why recovery tools need scan modes, resumability, evidence exports, and bad-sector tolerance. Existing roadmap item "Replace recovery scan with fast/deep/resumable modes" is correctly prioritized.
- smartmontools/CrystalDiskInfo: use curated drive knowledge to turn raw SMART attributes into actionable warnings. Learn from vendor/attribute metadata; avoid pretending unknown attributes are definitive.
- AOMEI/EaseUS/MiniTool/DiskGenius: treat resize/move/clone/recovery/boot media/health checks as table-stakes commercial flows. Learn from their broad workflow coverage; avoid bundling consumer cleanup tools that dilute PartitionPilot's admin focus.
- Macrium/Paragon: emphasize image verification, rescue media, VSS consistency, and restore confidence. Learn from restore verification and preflight evidence; avoid backup-suite expansion.

## Security, Privacy, and Reliability
- Verified: `src/PartitionPilot.Core/Services/VssSnapshotService.cs` only checks `vssadmin list providers` for availability and creates shadows by parsing `vssadmin create shadow`; it does not inspect `vssadmin list writers` health before image capture.
- Verified: `src/PartitionPilot/Dialogs/FilesystemSupportDialog.xaml.cs` contains a hardcoded support matrix, while operation gates in `src/PartitionPilot/ViewModels/PartitionsViewModel.cs` are ad hoc and type-focused. The matrix should become executable policy used by GUI and CLI.
- Verified: `tests/PartitionPilot.UiTests/SmokeTests.cs` has five FlaUI smoke tests with failure screenshots, but the current local run skipped all five because no interactive desktop session was available. Release validation needs an explicit executable UI lane and a controlled headless skip path.
- Verified: `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs` scans every 512-byte sector with a 4096-byte buffer and stops on read or seek failure; the existing roadmap already covers fast/deep/resumable modes and duplicate candidate handling.
- Verified: `src/PartitionPilot.Core/Services/UpdateService.cs` uses Velopack `GithubSource` and fallback GitHub latest-release checks; the existing roadmap already covers project-level hash/signature verification.
- Verified: `dotnet list .\src\PartitionPilot\PartitionPilot.csproj package --vulnerable --include-transitive` and `dotnet list ... --outdated` reported no vulnerable packages and no available updates from nuget.org at research time.
- Verified: support-bundle redaction exists in `src/PartitionPilot/ViewModels/MainViewModel.cs` and tests cover user paths/serial redaction in `tests/PartitionPilot.Tests/MainViewModelTests.cs`.

## Architecture Assessment
- The largest orchestration files remain `src/PartitionPilot/ViewModels/ToolsViewModel.cs` (1504 lines), `src/PartitionPilot/ViewModels/PartitionsViewModel.cs` (1181), `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs` (868), `src/PartitionPilot.Cli/Program.cs` (848), and `src/PartitionPilot.Core/Services/WmiDiskService.cs` (820). The existing Core-service extraction roadmap item is still valid.
- Filesystem support should move from dialog-local rows into a Core service that returns operation availability, reason text, and environment prerequisites for both GUI and CLI.
- VSS should gain a small parser/preflight boundary that records providers, writers, failed states, and stale shadows before `DiskCloningViewModel` captures a WIM/VHDX.
- SMART health should separate raw telemetry collection from advisory metadata so vendor-specific SATA/NVMe attributes can be explained without hardcoding all UX text in view models.
- Test gap: unit coverage is broad for layout diff, journals, encryption, SMART history, sector clone, and update version parsing, but UI automation currently provides no pass/fail signal in a noninteractive run.
- Documentation gap: existing roadmap correctly calls for layout spec, encrypted image format, release verification, and recovery-scan documentation; no extra docs item is needed here.

## Rejected Ideas
- VDS COM API: rejected because the repo already uses the Windows Storage Management API and `Roadmap_Blocked.md` records VDS as deprecated/superseded by Microsoft.
- Keyboard shortcuts: rejected because repo/global rules prohibit shortcuts and `Roadmap_Blocked.md` already captures the blocked item.
- Remote build/test/release workflows: rejected because the repo history removed GitHub Actions and current policy requires local builds and local release artifacts.
- Immediate dependency bump work: rejected because current `dotnet list ... --outdated` and NuGet audit checks show no available package updates or vulnerable packages.
- Full Linux rescue appliance: rejected because the existing WinPE-compatible rescue profile is a better fit for a Windows admin tool.
- Multi-user/cloud backup portal: rejected because PartitionPilot's design is local privileged disk administration, not fleet backup management.
- Mobile companion app: rejected because disk operations require local Windows storage APIs and elevation.
- Plugin ecosystem for partition operations: rejected for now because external operation plugins would expand the safety and trust boundary before Core capability gates are centralized.
- Third-party write support for ext/APFS/HFS+/LUKS: rejected because current Windows-first policy should detect/report these filesystems, not perform risky writes through bundled drivers.

## Sources
OSS and adjacent tools:
- https://gparted.org/features.php
- https://gparted.org/livecd.php
- https://apps.kde.org/partitionmanager/
- https://invent.kde.org/system/kpmcore
- https://clonezilla.org/
- https://partclone.org/features/
- https://rescuezilla.com/
- https://www.cgsecurity.org/wiki/TestDisk
- https://www.cgsecurity.org/wiki/PhotoRec
- https://www.gnu.org/software/ddrescue/manual/ddrescue_manual.html
- https://www.smartmontools.org/
- https://github.com/smartmontools/smartmontools/blob/master/smartmontools/drivedb.h
- https://crystalmark.info/en/software/crystaldiskinfo/

Commercial tools:
- https://www.aomeitech.com/pa/standard.html
- https://www.easeus.com/partition-manager/
- https://www.partitionwizard.com/free-partition-manager.html
- https://www.diskgenius.com/
- https://www.paragon-software.com/home/hdm-windows/
- https://www.macrium.com/reflect

Windows platform and dependencies:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-overview
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/vssadmin-list-writers
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-create-usb-bootable-drive?view=windows-11
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-image-management-command-line-options-s14?view=windows-11
- https://learn.microsoft.com/en-us/windows/win32/secprov/win32-encryptablevolume
- https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/reagentc-command-line-options?view=windows-11
- https://docs.velopack.io/integrating/overview
- https://jrsoftware.org/ishelp/index.php?topic=setup_signtool
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
- https://api.xunit.net/v3/3.0.1/v3.3.0.1-Xunit.Assert.SkipWhen.html

## Open Questions
None that block prioritization. Code-signing credentials and WinPE ADK availability remain implementation prerequisites already represented by existing roadmap or blocked-roadmap items.

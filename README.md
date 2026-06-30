![Version](https://img.shields.io/badge/version-0.9.14-4CC2FF)
![License](https://img.shields.io/badge/license-MIT-5EE0A0)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-F4C96A)

# PartitionPilot

PartitionPilot is a Windows disk partition management tool for power users and IT administrators. It provides a WPF interface for partition operations, disk health checks, maintenance tools, and disk image workflows.

![PartitionPilot main window](assets/screenshots/partitionpilot-main.png)

## Features

- Partition overview with disk map, partition table, and contextual actions.
- Pending operations queue: partition changes are queued and previewed before applying.
- Partition snapshot history with JSON export, mismatch-checked recovery plans, and guided recovery notes.
- Required pre-destruction partition snapshots before image restore, sector clone, whole-disk wipe, DoD wipe, and NVMe sanitize workflows.
- Lost-partition recovery scanning with fast boundary probes, resumable deep mode, duplicate candidate coalescing, and coverage reporting.
- Create, delete, format, resize, extend, split, hide, and drive-letter operations.
- Disk initialization for RAW/unpartitioned disks (GPT).
- Extended SMART health monitoring via LibreHardwareMonitorLib: reallocated sectors, pending sectors, power cycles, total writes, NVMe available spare, NVMe media errors, and vendor-specific attributes.
- 4K alignment review and disk health classification (Good/Warning/Critical).
- BitLocker encryption status with mutation and destruction preflights.
- Storage Spaces pool detection with integrity warnings on pooled disks.
- Unsupported partition type identification (Linux, LUKS, HFS+, APFS) with guarded actions.
- Shared filesystem capability policy blocks unsupported create, format, resize, extend, check, and label operations before native disk tools run.
- Maintenance tools: MBR to GPT conversion, filesystem repair, optimization/TRIM, secure wipe (single-pass, DoD 3-pass, DoD 7-pass, NVMe sanitize), boot repair, surface test, Dev Drive creation, and DiskSpd-backed benchmarking.
- Benchmark result export as JSON or text with drive metadata.
- Disk image workflows for mounting, dismounting, and creating VHD/VHDX images.
- Disk usage analysis with squarified treemap visualization and top-folder size breakdown.
- Disk cloning: create and restore WIM/VHDX images.
- VSS writer-health preflight before live volume image capture, with explicit degraded-mode confirmation on failed writers.
- Disk image sidecar manifests with image SHA256, source-volume evidence, sampled source file hashes, encrypted-image rebinding, and restore-time validation before target clearing.
- Post-restore and post-clone bootability audit for Windows targets, with BCD/WinRE checks and a non-destructive repair plan.
- Privacy-preserving support bundle export (redacted serial numbers and user paths).
- Structured native-command audit records with path redaction.
- Auto-updates via Velopack with delta packages and GitHub Releases integration.
- Release artifact verification with SHA256 manifests, optional Authenticode signing, and explicit unsigned local-test status.
- Release-gated UI smoke tests with TRX logs, screenshots, and fail-closed all-skipped detection.
- .NET 10 Fluent theme with dark, light, and system (follows OS setting) modes.
- CLI companion (pp.exe) for scripted disk management with JSON output.
- SMART attribute history tracking with trend alerts for degradation detection.
- smartctl diagnostics with path/version reporting, remediation, and disk-aware self-test gating for physical, NVMe, and USB bridge modes.
- Real-time disk temperature monitoring with configurable threshold alerts.
- MFT-direct NTFS scanning for near-instant disk usage analysis.
- Sector-level disk-to-disk clone with progress reporting and cancel support.
- i18n-ready string resources with LocExtension markup for localization.
- Activity log with export, filtering, and auto-save.
- Cancellable long-running operations with progress and rate reporting.
- Screen reader accessibility (AutomationProperties on all interactive controls).
- Administrator Protection compatible (ProgramData-based data paths).
- Local release packaging for self-contained Windows builds and installer artifacts.

## Requirements

- Windows 10 or Windows 11.
- Administrator privileges for disk operations.
- .NET 10 SDK to build from source.

## Build

```powershell
dotnet build .\src\PartitionPilot\PartitionPilot.csproj -m:1
```

The project targets `net10.0-windows` and publishes as a self-contained Windows app. Release artifacts are built locally.

```powershell
dotnet publish .\src\PartitionPilot\PartitionPilot.csproj -c Release -r win-x64 --self-contained true
dotnet publish .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -c Release -r win-x64 --self-contained true
dotnet run --project .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -- release-manifest --artifacts .\artifacts
```

Set `PARTITIONPILOT_SIGN_CERT_THUMBPRINT` before `release-manifest` to Authenticode-sign `.exe` artifacts with `signtool.exe`; without it, manifests are marked `UnsignedLocalTest`.

Run release UI smoke tests from an interactive desktop session:

```powershell
.\tools\run-ui-smoke.ps1
```

The gate builds the WPF app, runs simulation-mode FlaUI smoke tests, writes `artifacts\ui-smoke\ui-smoke.trx`, and saves failure screenshots under `artifacts\ui-smoke\screenshots`. Noninteractive verification must opt in to skipped UI tests with `-AllowHeadlessSkip`; without that flag, an all-skipped run fails the release gate.

## Run

```powershell
dotnet run --project .\src\PartitionPilot\PartitionPilot.csproj
```

For real disk operations, run the built executable from an elevated session so Windows storage APIs and native tools have the required permissions.

## CLI

```powershell
dotnet run --project .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -- disks
dotnet run --project .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -- partitions --disk 0
dotnet run --project .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -- health --json
```

Commands: `disks`, `partitions`, `volumes`, `smart`, `smart-history`, `smart-trends`, `health`, `alignment`, `temperature`, `benchmark`, `snapshot`, `diagnostics`, `boot-audit`, `plan`, `apply-layout`, `recovery-scan`, `version`. All support `--json` for scripted automation.

Recovery scans default to fast mode:

```powershell
pp recovery-scan --disk 1 --mode fast
pp recovery-scan --disk 1 --mode deep --state C:\ProgramData\PartitionPilot\recovery\scan-disk1.json
```

## Safety

Partition operations are queued and previewed before execution. Verify the selected disk, partition, and pending operations before clicking Apply. Keep current backups before resizing, formatting, deleting, or wiping disks.

## License

MIT. See [LICENSE](LICENSE).

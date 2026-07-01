![Version](https://img.shields.io/badge/version-0.9.20-4CC2FF)
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
- Extended SMART health monitoring via LibreHardwareMonitorLib: curated SATA/NVMe advisory metadata, reallocated sectors, pending sectors, power cycles, total writes, NVMe available spare, NVMe media errors, and vendor-specific attributes with raw fallback.
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
- Core workflow services centralize image preflight, support-bundle assembly, layout-plan generation, wipe prompts, and clone confirmations for GUI/CLI parity.
- Structured native-command audit records with path redaction.
- Auto-updates via Velopack with delta packages and GitHub Releases integration.
- Release artifact verification with SHA256 manifests, optional Authenticode signing, and explicit unsigned local-test status.
- Release-gated UI smoke tests with TRX logs, screenshots, and fail-closed all-skipped detection.
- WinPE rescue profile packaging with portable CLI launchers, source validation, and `diagnostics --rescue` checks for WMI, DiskPart, DISM, BitLocker, and storage APIs.
- .NET 10 Fluent theme with dark, light, and system (follows OS setting) modes.
- CLI companion (pp.exe) for scripted disk management with JSON output.
- SMART attribute history tracking with trend alerts for degradation detection.
- smartctl diagnostics with path/version reporting, remediation, and disk-aware self-test gating for physical, NVMe, and USB bridge modes.
- Real-time disk temperature monitoring with configurable threshold alerts.
- MFT-direct NTFS scanning for near-instant disk usage analysis.
- Sector-level disk-to-disk clone with progress reporting and cancel support.
- XAML localization resources with pseudo-locale coverage for visible labels and automation names.
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
dotnet run --project .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -- rescue-profile --source .\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish --output .\artifacts\rescue-profile
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

Commands: `disks`, `partitions`, `volumes`, `smart`, `smart-history`, `smart-trends`, `health`, `alignment`, `temperature`, `benchmark`, `snapshot`, `diagnostics`, `boot-audit`, `plan`, `apply-layout`, `recovery-scan`, `release-manifest`, `rescue-profile`, `version`. All support `--json` for scripted automation.

Recovery scans default to fast mode:

```powershell
pp recovery-scan --disk 1 --mode fast
pp recovery-scan --disk 1 --mode deep --state C:\ProgramData\PartitionPilot\recovery\scan-disk1.json
```

## Layout Specs

`pp apply-layout --file layout.json --disk N` reads a declarative JSON layout and prints a dry-run plan by default. Add `--apply` to execute the plan. Add `--replace` only when the current disk layout intentionally differs from the spec and the disk should be cleared and recreated.

```json
{
  "Style": "GPT",
  "TargetDisk": {
    "DiskNumber": 2,
    "FriendlyName": "Contoso USB SSD",
    "Size": 1024209543168,
    "PartitionStyle": "GPT",
    "UniqueId": "5000C500AABBCCDD",
    "SerialNumber": "SN123456",
    "Path": "\\\\?\\scsi#disk&ven_contoso",
    "BusType": "USB",
    "Location": "Port_#0004.Hub_#0001"
  },
  "Partitions": [
    {
      "SizeMB": "131072",
      "FileSystem": "NTFS",
      "Label": "System",
      "DriveLetter": "S"
    },
    {
      "UseMaximumSize": true,
      "FileSystem": "exFAT",
      "Label": "Data",
      "DriveLetter": "D"
    }
  ]
}
```

Rules enforced before DiskPart runs:

- `Style` must be `GPT` or `MBR`.
- Each partition must either set a positive whole-number `SizeMB` or set `UseMaximumSize: true`, not both.
- Filesystems are validated through the shared capability policy; unsupported create targets such as APFS are rejected before native tools run.
- `Label` is sanitized with the same label policy used by CLI plans.
- `DriveLetter` must be a single `A`-`Z` letter, with or without a trailing colon.
- `TargetDisk` is optional, but when present it must still match the current disk number, size, and stable identity fields. Use `pp disks --json` to capture the current identity block.

## Encrypted Images

PartitionPilot encrypted disk images are normal captured `.wim` or `.vhdx` files wrapped with a `.enc` suffix. Current writes use the chunked `PPENC2` container:

- AES-256-GCM with PBKDF2-SHA256 key derivation, 600,000 iterations, a 16-byte salt, 12-byte per-chunk nonces, and 16-byte authentication tags.
- Default plaintext chunks are 4 MiB; the encrypted header records chunk size and original plaintext length.
- Chunk authentication binds the container magic, plaintext length, chunk size, chunk index, and chunk length.
- Restores still read legacy `PPENC1` whole-file encrypted images for compatibility.
- A sidecar manifest is written next to the image as `<image>.ppmanifest.json`; encrypted captures rebind the manifest to the `.enc` file hash and keep the original plaintext hash as `PlainImageSha256`.

Restore behavior is fail-closed for mismatched image hashes. Missing or unreadable manifests, or plaintext hash mismatches after decrypting an encrypted image, require an explicit degraded-mode confirmation before the target disk can be cleared.

## Recovery Scan Modes

`pp recovery-scan` is read-only. It scans raw disk sectors for filesystem signatures and reports candidate partitions without changing the partition table.

- `--mode fast` probes common boot and alignment offsets, including legacy sector starts and 1 MiB boundaries. Use it first for quick triage.
- `--mode deep` scans every 512-byte sector, checkpoints progress, and can take hours on large disks.
- Deep scans default their resume file to `C:\ProgramData\PartitionPilot\recovery\recovery-scan-diskN.json`; pass `--state path` to use a specific file.
- Ctrl+C during deep mode saves resume state and exits with code 130. Resume with the command printed by the CLI.
- Completed deep scans remove their resume file automatically.
- Reports include scan mode, checked-offset count, coverage bytes and percent, completion status, resume path, candidate filesystem, offset, estimated size, confidence, and details.

## Release Verification

PartitionPilot releases are built locally. A release candidate should have fresh published GUI and CLI folders, an Inno Setup installer, and release manifests:

```powershell
dotnet publish .\src\PartitionPilot\PartitionPilot.csproj -c Release -r win-x64 --self-contained true
dotnet publish .\src\PartitionPilot.Cli\PartitionPilot.Cli.csproj -c Release -r win-x64 --self-contained true
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\PartitionPilot.iss
.\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish\pp.exe release-manifest --artifacts .\artifacts
.\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish\pp.exe rescue-profile --source .\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish --output .\artifacts\rescue-profile
.\tools\run-ui-smoke.ps1
```

`release-manifest` writes `SHA256SUMS` and `SHA256SUMS.json`, then verifies every listed artifact hash. Set `PARTITIONPILOT_SIGN_CERT_THUMBPRINT` or pass `--cert-thumbprint` to Authenticode-sign `.exe` artifacts; unsigned builds are marked `UnsignedLocalTest` and should be treated as local-test outputs, not trusted releases.

## Safety

Partition operations are queued and previewed before execution. Verify the selected disk, partition, and pending operations before clicking Apply. Keep current backups before resizing, formatting, deleting, or wiping disks.

## License

MIT. See [LICENSE](LICENSE).

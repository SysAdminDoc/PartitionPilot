# Changelog

## PartitionPilot v0.2.0 - 2026-06-16

### P0 Fixes
- Fixed FindImageDriveLetter: mounted ISO/VHD images now display their assigned drive letter.
- Replaced 10 silent catch blocks in WmiDiskService with ActivityLog error messages.
- Fixed boot repair EFI partition detection: auto-detects ESP by GPT type GUID instead of hardcoding /s S:.

### P1 Architecture
- Hardened ProcessRunner: non-zero exit code always throws, diskpart stdout scanned for error patterns, PowerShell stderr ignored on success.
- Added input sanitization (SanitizeLabel, ValidateDriveLetter) for all diskpart script interpolation points.
- Extracted IDialogService interface — all 20+ MessageBox.Show calls replaced with testable dialog methods.
- Added CancellationToken support for wipe, fscheck, optimize, and benchmark operations with Cancel button in busy overlay.
- Added progress status text for long-running tools operations.
- Extracted IProcessRunner and IWmiDiskService interfaces for unit test mocking.
- Added activity log export (timestamped .log file) and auto-save on app close to %TEMP%/PartitionPilot/.
- Added xUnit test project with 20 tests covering SizeUtil, ProcessRunner sanitization, and partition enrichment.
- Added GitHub Actions CI pipeline (build + test on push/PR to main).

### P2 Features
- Added dark/light theme switching with persistent preference (default: dark).
- Added BitLocker encryption status display per volume in partition details.
- Added Disk Usage analysis tab with top-30 folder size breakdown, cancellable scan.
- Added Inno Setup installer script for professional distribution.

### P3 Features
- Added disk surface test (Repair-Volume OfflineScanAndFix) in Tools tab.
- Added startup update check against GitHub Releases API.
- Added disk cloning tab with WIM/VHDX create and restore workflows.
- Added AutomationProperties.Name for screen reader accessibility across all views.

## PartitionPilot v0.1.0 - 2026-06-16

- Refined the WPF app shell with a cohesive dark theme, stronger hierarchy, and clearer status/log areas.
- Added shared control styling for buttons, inputs, combo boxes, lists, groups, focus states, empty states, and busy overlays.
- Improved Partitions, Disk Health, Tools, Disk Images, disk map, and partition dialogs with clearer layout, microcopy, and accessibility names.
- Added mode-specific Secure Wipe targets so free-space wipe selects a volume and full-disk wipe selects a physical disk.
- Added partition right-click context actions and fixed row selection before context commands run.
- Fixed benchmark random-read handling to require exact reads.
- Added repository hygiene, README, MIT license, and version metadata.

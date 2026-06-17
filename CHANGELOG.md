# Changelog

## Unreleased

### Release Trust
- Fixed release metadata drift: the update checker now reads the app version from assembly metadata, the Inno installer reports v0.3.0, README uses a non-versioned screenshot path, and CI validates installer/README version consistency.
- Made NuGet restores deterministic with lock files, explicit package versions, CI locked-mode restore, and migration of the test suite to xUnit v3.

### Safety & Reliability
- Changed destructive volume operations to fail closed when exclusive volume locking cannot be acquired, including format, resize, split, delete, extend, clone restore, free-space wipe, and disk wipe flows.
- Fixed VHDX image create/restore success handling so missing mounted/source/destination drive letters now stop the operation instead of silently skipping copy work.
- Added per-disk NVMe sanitize preflight so firmware erase is available only when the selected physical disk is verified as NVMe on a supported Windows build, with the UI showing the reason when unavailable.
- Added a Windows RE guard that refuses Recovery partition delete/extend operations, records `reagentc /info`, and tells the user to use a dedicated recovery relocation workflow instead of leaving WinRE disabled.
- Hardened WMI query filters with a shared WQL string literal helper and namespace/class-aware provider diagnostics that redact local paths from failure messages.

## PartitionPilot v0.3.0 - 2026-06-16

### Security Hardening (P0)
- Switched PowerShell execution from `-Command` to `-EncodedCommand` (Base64 UTF-16LE), eliminating shell metacharacter injection via outer argument parsing.
- Added `EscapePowerShellString()` helper that wraps values in single quotes with proper `'` → `''` escaping. Applied to all user-influenced file paths in DiskCloningViewModel and DiskImagesViewModel.
- Expanded `SanitizeLabel()` to strip shell metacharacters (`;`, `&`, `|`, `$`, `` ` ``, `(`, `)`) in addition to quotes and newlines.
- Sanitized double-quote characters from file paths interpolated into diskpart scripts and DISM commands.
- Pinned `System.Management` to explicit version `9.0.6` (was `9.*` wildcard). Enabled `<NuGetAudit>true</NuGetAudit>` for vulnerability scanning on restore.
- Added `VolumeLockService` using `FSCTL_LOCK_VOLUME` / `FSCTL_UNLOCK_VOLUME` P/Invoke. Destructive operations (format, delete, wipe, disk clone restore) now acquire exclusive volume locks before executing.
- Updated CI workflow to restore and audit both main project and test project.

### Safety & Reliability
- Added partition table backup service — saves JSON snapshots of disk layout to `%TEMP%/PartitionPilot/backups/` before every destructive operation (delete, format, extend, split). Snapshots retained for 30 days.
- Added format confirmation dialog — format now requires explicit "ALL DATA WILL BE ERASED" confirmation after parameter selection, matching the existing delete confirmation pattern.
- Fixed concurrent `LoadPartitionsAsync` race — rapid disk selection now cancels any in-flight load via `CancellationTokenSource`, preventing overlapping collection updates and UI flicker. Same fix applied to `DiskHealthViewModel.LoadHealthDataAsync`.
- Added critical partition protection — delete and format on System, Recovery, or Boot partitions now show an extra danger confirmation warning about unbootable risk before the standard confirmation.
- Added device presets for Format dialog — Camera (FAT32/32KB), Nintendo Switch (FAT32/64KB), Raspberry Pi (FAT32), Large USB (exFAT), General NTFS. Presets auto-fill file system and allocation unit size.
- Added disk health classification badge — colored Good (green) / Warning (yellow) / Critical (red) / Unknown (gray) badge on the Disk Health tab. Thresholds: Wear ≤5% or Temp ≥65°C = Critical; Wear ≤15% or Temp ≥55°C or uncorrected errors = Warning. Tooltip shows the specific reason.
- Added empty-state guidance to Disk Usage tab with "Select a drive and click Scan" prompt.
- Added portable mode — place a `portable.txt` file next to the exe to store settings, logs, and backups alongside the executable instead of in AppData/%TEMP%.
- Enabled PublishReadyToRun for ~20-30% faster cold startup on published builds.
- Prepared for Windows 11 Administrator Protection — theme preferences now mirror to ProgramData for shared access across elevation contexts.
- Added Dev Drive (ReFS) creation in Tools tab — formats a volume as Dev Drive with `Format-Volume -DevDrive` and designates it as trusted via `fsutil devdrv trust`. Greyed out on unsupported Windows versions (requires build 22621+).
- Added NVMe firmware erase (NIST 800-88 Purge) — new wipe mode using `IOCTL_STORAGE_REINITIALIZE_MEDIA` for Block Erase or Crypto Erase. Targets the drive controller directly for reliable SSD sanitization. Requires Windows 11+.
- Added squarified treemap visualization to Disk Usage tab — color-coded rectangles proportional to folder size, with click-to-select. Custom WPF control using DrawingContext for performance.

## PartitionPilot v0.2.3 - 2026-06-16

### Reference-Driven Console Polish
- Reworked the main shell into a denser disk-console layout with compact brand/command bar, session-status tiles, descriptive navigation, and a calmer bottom status strip.
- Added a global Refresh command that reloads the active workspace and auto-loads the partition workspace at startup to reduce first-run friction.
- Moved partition operations into a persistent right-side action rail with clearer selection context, safety copy, and disabled-state affordances.
- Redesigned the disk map as partition cards with accent strips, capacity text, type labels, and stronger empty-state feedback.
- Replaced the raw activity textbox with structured, filterable log entries, clear/export actions, live entry count, and table semantics.
- Cleaned user-facing placeholder dashes in partition rows and refreshed the README screenshot for the polished shell.

## PartitionPilot v0.2.2 - 2026-06-16

### Premium UX Polish
- Refined the app shell with a dark native title bar, custom PartitionPilot icon, stronger product hierarchy, and clearer status badges.
- Added semantic theme surfaces for notices, badges, empty states, disabled controls, busy panels, scrollbars, and dialog footers.
- Converted brush references to DynamicResource so dark/light theme switching applies live without requiring restart.
- Upgraded Partitions, Disk Health, Disk Usage, Disk Images, Tools, and Disk Cloning empty/loading/risk states for a calmer first-run and long-operation experience.
- Improved disk-map readability with a richer empty state, segment accessibility names, and contrast-aware segment labels.
- Standardized operation dialogs with clearer destructive notices, structured footers, and accessible field names.
- Added README screenshot artifact for the polished main window.

## PartitionPilot v0.2.1 - 2026-06-16

### Audit Fixes
- Fixed update checker using lexicographic version comparison (0.10.0 < 0.2.0 was wrong); now uses System.Version for semantic comparison.
- Fixed disk cloning crash: robocopy exit codes 1-7 are success (files copied), only >=8 is fatal. Every successful clone was throwing.
- Fixed Disk Usage "Share" column always showing zero-width bars (was using InverseBool converter on a double proportion; replaced with ProportionToWidthConverter).
- Fixed ActivityLog O(n^2) performance from string concatenation on every log call; replaced with StringBuilder.
- Fixed 11 hardcoded dark-mode hex colors in AppStyles.xaml that broke light theme (invisible list selections, unreadable column headers, dark overlay on light background). Extracted semantic theme tokens (ItemHover, ItemSelected, DropdownHighlight, NavHover, HeaderBg, Overlay, ToolbarBtn, DangerBtnBg/Fg, PrimaryBtnFg).
- Fixed NullReferenceException crash in Create Partition and Split Partition dialogs when no drive letters are available.
- Fixed DiskBarControl empty-state text using hardcoded RGB instead of MutedTextBrush theme token.
- Fixed theme toggle silently doing nothing (WPF StaticResource bindings don't re-resolve at runtime); now saves preference and informs user restart is needed.
- Added filesystem allowlist validation (NTFS/FAT32/exFAT/ReFS) to prevent injection via fs parameter in diskpart scripts.
- Added drive letter validation to resize operations.
- Removed dead code in DiskCloningViewModel (unused cmd variable).
- Added 13 new tests (version comparison, filesystem validation).

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

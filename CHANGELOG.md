# Changelog

## PartitionPilot v0.9.13 - 2026-06-30

### Bootability Audit
- Added a Core bootability audit for restored and cloned Windows targets covering partition style, EFI/system partition presence, BCD files, and WinRE status.
- Restore and sector-clone completion now include pass/warn/fail boot audit output plus a non-destructive bcdboot/reagentc repair plan when needed.
- Added `pp boot-audit --disk N [--windows C]` for rerunning the same audit from the CLI.

## PartitionPilot v0.9.12 - 2026-06-30

### SMART Diagnostics
- Environment diagnostics now report smartctl availability, version, path, and remediation when SMART self-tests are unavailable.
- Disk Health self-test buttons are gated by disk-aware smartctl capability, including NVMe mode selection, USB SAT bridge labeling, and unsupported-device warnings.
- Added unit coverage for smartctl discovery, missing-tool diagnostics, device-mode selection, and self-test command generation.

## PartitionPilot v0.9.11 - 2026-06-30

### Image Integrity
- WIM capture/apply now uses DISM `/CheckIntegrity` and `/Verify`.
- Image capture writes a `.ppmanifest.json` sidecar with image SHA256, source-volume metadata, source file counts/bytes, and sampled source file hashes.
- Encrypted image captures rebind the sidecar manifest to the encrypted file hash while preserving the plain-image hash, and restores validate manifests before clearing the target disk.

## PartitionPilot v0.9.10 - 2026-06-30

### Release Integrity
- Added `pp release-manifest` to generate `SHA256SUMS` and `SHA256SUMS.json` for local release artifacts.
- Release manifest generation Authenticode-signs `.exe` artifacts when a signing certificate thumbprint is configured and marks unsigned outputs as `UnsignedLocalTest`.
- Update checks now surface GitHub release asset digest/manifest status, and Velopack downloads require expected checksum metadata before apply.

## PartitionPilot v0.9.9 - 2026-06-30

### Recovery
- Replaced whole-disk 512-byte-stride recovery scans with default fast mode and explicit deep mode.
- Fast recovery scans probe common legacy and 1 MiB partition boundaries while still checking filesystem boot records and superblock offsets.
- Deep recovery scans now checkpoint progress to a resume state file, support Ctrl+C cancellation from the CLI, coalesce duplicate candidates, and include scan mode plus coverage in text and JSON reports.

## PartitionPilot v0.9.8 - 2026-06-30

### Safety & Reliability
- Added a shared filesystem capability policy for create, format, resize, extend, check, and label support.
- GUI partition, tools, VHD creation, CLI plan/apply, and layout-spec paths now fail closed before invoking native disk tools for unsupported filesystems.
- Updated filesystem support dialog data and tests to cover NTFS, FAT32, exFAT, ReFS, FAT16, ext, APFS, HFS+, Linux swap, and LUKS behavior.

## PartitionPilot v0.9.7 - 2026-06-29

### Safety & Reliability
- Added VSS writer-health parsing and preflight checks before live volume image capture.
- Image capture now requires healthy VSS writers or an explicit degraded-mode confirmation before proceeding without a consistent snapshot.
- Environment diagnostics now report VSS writer health separately from provider availability.

## PartitionPilot v0.9.6 - 2026-06-29

### Safety & Reliability
- Added required pre-destruction partition snapshots for image restore, sector clone destinations, whole-disk wipe, DoD wipe, and NVMe sanitize workflows.
- Snapshot write failures now block destructive disk actions before native overwrite commands run and log the snapshot path for recovery evidence.
- Support bundles now include the newest partition snapshots first.

## PartitionPilot v0.9.5 - 2026-06-28

### Safety & Reliability
- Added stable disk identity fields from `MSFT_Disk` to disk records, CLI JSON output, partition snapshots, and operation journals.
- Added target identity text to destructive wipe, sanitize, clone, restore, format, delete, layout, and queue-apply confirmations.
- Blocked queued operations and layout specs when the saved target identity no longer matches the current disk.
- Added tests for disk identity matching, journal persistence, recovery notes, and pre-execute queue validation.

## PartitionPilot v0.9.4 - 2026-06-28

### Safety & Reliability
- Replaced whole-file encrypted image writes with a chunked `PPENC2` AES-256-GCM container that keeps memory bounded for large WIM/VHDX images.
- Authenticated each encrypted chunk with header-bound associated data and preserved legacy `PPENC1` decrypt compatibility.
- Added encryption tests for chunked round-trip, legacy decrypt, tamper detection, wrong-password failure, and cancellation.

## PartitionPilot v0.9.3 - 2026-06-28

### Safety & Reliability
- Made declarative `apply-layout` idempotent for matching disk layouts.
- Blocked destructive layout replacement by default; populated-disk mismatches now require `--replace` plus the existing destructive confirmation.
- Added layout-diff tests for no-op, create-only, blocked mismatch, and explicit replacement plans.

## PartitionPilot v0.9.2 - 2026-06-28

### Safety & Reliability
- Added fail-closed validation for declarative layout specs before DiskPart scripts are emitted.
- Rejected invalid or injection-shaped partition style, size, and drive-letter values with clear errors.
- Added layout-diff tests covering valid normalization and unsafe JSON-shaped inputs.

## PartitionPilot v0.9.1 - 2026-06-27

### Documentation & Release Hygiene
- Drained stale completed items from the active roadmap after verifying the v0.9.0 feature work is present in the codebase.
- Replaced current README CI language with local build and release artifact instructions.
- Bumped app, CLI, core library, installer, and README version strings to v0.9.1.
- Included the published `pp.exe` CLI companion in the installer payload.

## PartitionPilot v0.9.0 - 2026-06-20

### Safety & Reliability
- Added post-clone verification pass: after sector clone, source and destination are re-read and compared block-by-block. Mismatches are reported with count and duration.
- Added bad-sector rescue mode for sector clone: when enabled, read failures zero the destination block and log the offset instead of aborting. Final report lists all bad sectors with count and percentage.
- Added journal-save failure logging: OperationQueue now logs warnings when crash-recovery journal writes fail instead of silently swallowing errors.
- Enhanced BitLocker status display: shows encryption method (XTS-AES-128, AES-256, etc.), conversion state (Encrypting/Decrypting/Paused with progress), and lock status. Mid-encryption volumes are now correctly treated as protected.
- Added pre-clone target signature erasure checkbox and verify-after-clone checkbox to the Disk Cloning UI.

### Features
- Added CLI benchmark command: `pp benchmark --drive C` runs DiskSpd profiles and outputs results in text or JSON.
- Added CLI SMART history command: `pp smart-history --disk N` shows recorded SMART readings over time.
- Added CLI SMART trends command: `pp smart-trends --disk N` shows trend analysis with severity levels.
- Added CLI temperature command: `pp temperature` shows current temperatures for all physical disks with threshold warnings.

### UX
- Added filesystem support matrix dialog showing which operations each filesystem supports, accessible from the command bar.
- Added read-only hex sector viewer tab: displays raw disk sectors in hex + ASCII, with sector navigation, read-only access to any physical disk.
- Added FAT32 >32GB formatting via PowerShell Format-Volume (bypasses Windows 32GB diskpart limitation with auto-scaled cluster size).
- Added pre-clone target signature erasure: first 64KB of destination disk is zeroed before sector clone to prevent ghost filesystem detection.

### Health & Monitoring
- Added full NVMe health log: Unsafe Shutdowns, Controller Busy Time, Error Information Log Entries, Critical Warning flags (spare low, temp exceeded, reliability degraded, read-only, backup failed) via IOCTL_STORAGE_QUERY_PROPERTY.
- Added SMART self-test triggers: "Short Test" and "Extended Test" buttons on Disk Health tab invoke smartctl for both SATA and NVMe drives. Status and estimated duration displayed inline.
- Added SMART diagnostic report export: HTML report with drive info, all SMART attributes, health status, trend analysis, temperature history, and alignment audit. Opens in default browser.
- Added SSD endurance gauge: shows total bytes written vs user-configurable rated TBW with progress bar. Rated TBW persists per drive in ProgramData.
- Expanded recovery scanner to detect ext2/3/4, btrfs, XFS, HFS+/HFSX, APFS, and Linux swap signatures in addition to existing NTFS/FAT/exFAT/ReFS.

### Quality
- Added 5 tests for SectorCloneResult (report formatting, verification, bad sectors, phase display).
- Expanded BitLocker tests from 5 to 17: encryption method mapping, conversion state handling, IsProtected for mid-encryption volumes.
- Added 4 tests for NVMe Critical Warning flag bitfield parsing.
- Added pseudo-locale resource completeness CI gate test verifying all 130+ keys have pseudo-locale translations.
- Added ARM64 build target: CI now produces both x64 and ARM64 self-contained binaries with separate attestation.
- Added real-time disk I/O performance counters: toggle "Start I/O Monitor" on Disk Health tab shows live read/write MB/s, IOPS, queue depth, and latency per physical disk. Uses Windows PhysicalDisk performance counters, updates every 2 seconds.
- Added image encryption for WIM/VHDX captures: AES-256-GCM with PBKDF2-SHA256 key derivation (600K iterations). Encrypted images use `.enc` suffix. Restore auto-detects encrypted images and prompts for password.
- Added German, Spanish, and French translations (130+ resource keys each) to validate the i18n pipeline. App auto-loads matching locale via .NET satellite assemblies.
- Added declarative partition layout spec for CLI: `pp apply-layout --file layout.json --disk N` reads a JSON layout spec, computes a diff against the current disk state, and applies with `--apply`. Dry-run by default.

## PartitionPilot v0.8.0 - 2026-06-19

### Safety & Reliability
- Added operation queue journaling for crash recovery: every Apply batch writes a redacted JSON journal to ProgramData. On startup, interrupted journals are detected and shown with per-operation status. Journals auto-purge after 30 days.
- Fixed Storage Spaces membership: replaced imprecise pool assignment with proper MSFT_StoragePoolToPhysicalDisk association query. Pool health, operational status, and read-only state are now exposed.
- Added VSS-backed live volume image capture: WIM/VHDX capture creates a VSS shadow copy for point-in-time consistency, with explicit user confirmation before fallback to live capture.
- Added operation impact preview before Apply: confirmation dialog now shows risk summary, affected targets, and per-operation type/risk breakdown.
- Added versioned JSON schemas for persisted files: snapshot and SMART history files include schema version envelopes, v0 files load seamlessly, corrupt files are quarantined to .corrupt.
- Fixed sector clone fail-open bug: read failures and zero-byte reads now throw with offset context. Partial writes handled correctly. Source pooled-disk guard added.

### Features
- Added CLI plan/apply automation: `pp plan create|delete|format|change-letter` with --apply flag, YES confirmation for destructive operations, and --json structured output.
- Added read-only lost-partition scanning: scans raw disk sectors for NTFS, FAT32, FAT16/12, exFAT, and ReFS filesystem signatures. CLI: `pp recovery-scan --disk N`.
- Added preflight environment diagnostics: checks elevation, .NET version, WMI providers, native tools, DiskSpd cache, and data directory. CLI: `pp diagnostics`.
- Added SMART history export, import, and retention controls: timestamped dedup import, path-redacted export, configurable retention.
- Expanded localization to 130+ resource keys with pseudo-locale (qps-ploc) for clipping testing.
- Added CycloneDX SBOM generation and full artifact provenance in CI: attests GUI EXE, CLI EXE, SBOMs, and SHA256SUMS.
- Activated WPF UI smoke tests in CI with screenshot capture and xUnit v3 runtime skip.

### Quality
- Added 76 tests for v0.7.0 services (145 -> 231 total): SMART trend analysis, sector clone validation, temperature monitor, localization keys, operation journal.

## PartitionPilot v0.7.0 - 2026-06-19

### Architecture
- Adopted .NET 10 Fluent theme with system dark/light tracking. Theme button now cycles Dark → Light → System, where System follows the OS Apps theme setting via registry change notifications. Removed ~95 lines of custom ScrollBar, RadioButton, CheckBox, and MenuItem templates now handled by the Fluent theme engine.
- Extracted PartitionPilot.Core library: all models and non-WPF services (13 models, 15 services) now live in a standalone net10.0-windows class library with no WPF dependency. Introduced IActivityLog interface to decouple core services from the WPF-bound ActivityLog.

### Features
- Added CLI companion (pp.exe) for scripted disk management. Commands: disks, partitions, volumes, smart, health, alignment, snapshot. All support --json for automation.
- Added SMART attribute history tracking with trend alerts. Records readings per device and analyzes the last 10 readings for degradation trends (reallocated sectors, NVMe media errors, wear, spare capacity, temperature). Trend alerts display in the Disk Health tab.
- Added real-time disk temperature monitoring with threshold alerts. Polls all disks every 30 seconds with Warning at 55 C and Critical at 65 C. Live temperatures and alert history display in the Disk Health tab.
- Added MFT-direct scanning for near-instant NTFS disk usage analysis via FSCTL_ENUM_USN_DATA. Automatically used on NTFS volumes with admin privileges; falls back to directory enumeration on non-NTFS or when unavailable.
- Added i18n readiness with .resx resource infrastructure. 70+ localized string keys, LocExtension markup for XAML bindings, MainWindow tab headers converted. Add Strings.{culture}.resx for translations.
- Added sector-level disk-to-disk clone. Raw sector copy with 1 MB buffer, progress reporting (rate, ETA), volume lock acquisition, triple-confirmation with BitLocker preflight, and cancel support.

### Safety & Reliability
- Preserved failed and skipped pending operations after a queue apply failure so users can review, retry, or remove the remaining work instead of losing the queue.
- Hardened DiskSpd benchmarking by verifying the downloaded ZIP and cached executable hashes, passing the required 1 GiB test-file creation argument, draining stderr, and falling back when all DiskSpd profiles fail.
- Fixed the fallback GitHub release update check to call the GitHub API endpoint instead of parsing the HTML release page.
- Redacted support-bundle activity logs and snapshots for user paths plus JSON/plain-text serial numbers before export.

### UX & Polish
- Fixed object-backed disk selectors so they show readable disk and volume labels instead of CLR type names.
- Cleared stale Disk Usage results at scan start so cancelled or failed scans do not leave old treemap data visible as if current.

## PartitionPilot v0.5.0 - 2026-06-19

### Architecture & Quality
- Added pending operations queue: partition operations (create, delete, format, resize, split, change letter) are now queued, previewed in the action rail, and executed only on Apply. Individual operations can be removed. Execution stops on first failure with a status report. This closes the #1 safety gap vs GParted/EaseUS/AOMEI.
- Expanded SMART monitoring via LibreHardwareMonitorLib 0.9.6: Disk Health tab now shows Reallocated Sectors, Pending Sectors, Power Cycles, Total Written/Read, NVMe Available Spare, NVMe Media Errors, and a full SMART attribute table. Health classification includes reallocated-sector and NVMe-spare thresholds. WMI data fills gaps.
- Replaced custom benchmark with DiskSpd-backed methodology: 8 standard profiles (SEQ1M Q1/Q8, RND4K Q1/Q32, read+write) with XML output parsing. DiskSpd auto-downloads from GitHub on first use. Falls back to built-in benchmark if unavailable.
- Synced operator docs, README, and blocked-roadmap items to reflect v0.5.0 architecture (7 tabs, pending queue, SMART expansion, DiskSpd).

## PartitionPilot v0.4.0 - 2026-06-18

### Architecture & Quality
- Integrated Velopack 1.2.0 for auto-updates with delta packages via GitHub Releases. Falls back to the existing version-check API when Velopack releases aren't published yet.
- Cached WMI scope connections per namespace (Storage, CIMV2, BitLocker) with automatic reconnect on failure, reducing 4-6 WMI connections per tab switch to at most 3.
- Deduplicated BitLocker status resolution into a shared WmiDiskService helper, removing identical copies from ToolsViewModel and DiskCloningViewModel.
- Upgraded System.Management 9.0.6 to 10.0.9 to match .NET 10 TFM.
- Added structured native-command audit records with path/profile redaction for every diskpart and PowerShell execution. Records include operation ID, command kind, target, exit code, and duration.
- Added CI release provenance with SHA256SUMS generation and GitHub artifact attestation via Sigstore.

### Safety & Reliability
- Added Administrator Protection compatibility: partition snapshots and activity logs now use ProgramData instead of %TEMP% so data persists across elevation contexts under SMAA. Elevation context detection (legacy UAC vs Administrator Protection) added to session info.
- Added Storage Spaces pool detection via MSFT_StoragePool. Pooled disks are labeled with pool name and destructive operations show pool integrity warnings.
- Expanded GPT type mapping to recognize Linux, Linux Swap, Linux Home, Linux Root, LUKS, HFS+, APFS, and LDM partitions. Destructive operations on unsupported types require stronger confirmation.
- Added DoD 5220.22-M 3-pass (zeros, ones, random) and 7-pass wipe patterns with per-pass progress and throughput reporting.
- Added mismatch-checked snapshot recovery plan export that verifies disk name/size/style against the current disk before generating recovery guidance.
- Added disk initialization workflow for RAW/unpartitioned disks (GPT partition table via Initialize-Disk).

### Features
- Added privacy-preserving support bundle export: ZIP containing redacted system info, activity log, disk summary, and up to 10 partition snapshots with serial numbers stripped.
- Added benchmark result export as JSON or text with drive metadata (letter, model, capacity, timestamp).
- Added progress rate and duration reporting for DoD wipe passes and disk usage scans.
- Removed legacy 1436-line PowerShell prototype (PartitionPilot.ps1) from the repository.

## Unreleased (pre-v0.3.0)

### Release Trust
- Fixed release metadata drift: the update checker now reads the app version from assembly metadata, the Inno installer reports v0.3.0, README uses a non-versioned screenshot path, and CI validates installer/README version consistency.
- Made NuGet restores deterministic with lock files, explicit package versions, CI locked-mode restore, and migration of the test suite to xUnit v3.

### Safety & Reliability
- Changed destructive volume operations to fail closed when exclusive volume locking cannot be acquired, including format, resize, split, delete, extend, clone restore, free-space wipe, and disk wipe flows.
- Fixed VHDX image create/restore success handling so missing mounted/source/destination drive letters now stop the operation instead of silently skipping copy work.
- Added per-disk NVMe sanitize preflight so firmware erase is available only when the selected physical disk is verified as NVMe on a supported Windows build, with the UI showing the reason when unavailable.
- Added a Windows RE guard that refuses Recovery partition delete/extend operations, records `reagentc /info`, and tells the user to use a dedicated recovery relocation workflow instead of leaving WinRE disabled.
- Hardened WMI query filters with a shared WQL string literal helper and namespace/class-aware provider diagnostics that redact local paths from failure messages.
- Routed remaining partition dialogs and code-behind prompts through `IDialogService` so validation, warnings, and destructive confirmations share one testable message surface.
- Added create-image destination preflights that block same-volume captures, missing destination folders, existing image files, unsupported extensions, and insufficient free space before DISM or DiskPart runs.
- Added BitLocker-aware preflights that block protected partition mutations until protection is suspended/unlocked and add stronger confirmations for encrypted format, delete, clone restore, Dev Drive, and wipe flows.
- Added an operation cleanup scope for temporary VHD attachments, restore image mounts, EFI access paths, and benchmark temp files so failure/cancel paths log recovery guidance and attempt cleanup consistently.
- Added a partition snapshot browser with read-only history, current-layout comparison, JSON export, and copyable non-destructive recovery guidance.
- Fixed shell version display drift by deriving the visible app version from assembly metadata instead of a hardcoded string.
- Fixed the Disk Usage results panel showing duplicate empty-state overlays before a scan.
- Improved Disk Usage feedback by preserving cancelled/failed scan summaries and selecting the first available drive after refresh.
- Hardened native disk-tool path handling by rejecting quotes/control characters instead of silently rewriting WIM/VHD paths, and validated SMART fallback device numbers before PowerShell interpolation.
- Fixed the Disk Images VHD type selector so choosing Fixed actually creates a fixed-size virtual disk instead of leaving the operation in dynamic mode.
- Hardened partition formatting and drive-letter operations by validating DiskPart allocation-unit and drive-letter inputs at the view-model command boundary.
- Corrected SSD wear health classification so low wear percentages are treated as healthy and high wear percentages near the documented limit trigger warnings or critical status.
- Fixed Secure Wipe mode state so Free space, Entire disk, and NVMe firmware erase behave as mutually exclusive choices with the correct target selector visible.
- Improved Tools refresh behavior so removed disks/volumes no longer remain selected and low-risk tools receive sensible current-volume defaults after refresh.
- Fixed Secure Wipe mode change notifications so the NVMe firmware erase option stays visually synchronized with programmatic mode changes.

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

# PartitionPilot Roadmap

## Research-Driven Additions

### P0 — Critical Fixes (ship-blocking)

### P1 — Architecture & Quality

- [ ] P1 — Add pending operations queue with preview-before-apply
  Why: Every serious partition tool researched (GParted, EaseUS, MiniTool, AOMEI) queues changes and shows a preview before writing. PartitionPilot executes immediately, which is the #1 safety gap. This is table-stakes UX for partition management.
  Evidence: GParted pending operations model, EaseUS/AOMEI/MiniTool user guides
  Touches: New `Services/OperationQueue.cs`, `ViewModels/PartitionsViewModel.cs`, `Views/PartitionsView.xaml`, new `Views/PendingOperationsPanel.xaml`
  Acceptance: Partition operations (create, delete, format, resize, extend, split) are queued, shown in a pending list with before/after preview, and only execute when user clicks Apply. Individual operations can be removed from the queue.
  Complexity: XL

- [ ] P1 — Expand SMART monitoring via LibreHardwareMonitorLib
  Why: Current SMART data (WMI StorageReliabilityCounter + PowerShell fallback) shows only 8 attributes. Missing critical indicators: Reallocated Sectors Count (the #1 failure predictor), Current Pending Sectors, NVMe Available Spare, NVMe Media Errors, Total LBAs Written. CrystalDiskInfo shows 30+ attributes with vendor-specific decoding.
  Evidence: CrystalDiskInfo attribute coverage, LibreHardwareMonitorLib 0.9.6 NuGet, smartmontools attribute reference
  Touches: `PartitionPilot.csproj` (add LibreHardwareMonitorLib), `Services/WmiDiskService.cs` (replace or augment GetSmartDataAsync), `Models/SmartData.cs` (expand), `ViewModels/DiskHealthViewModel.cs`, `Views/DiskHealthView.xaml`
  Acceptance: Disk Health tab shows Reallocated Sectors, Pending Sectors, Power Cycle Count, Total Writes, and NVMe-specific health attributes. Health classified as Good/Warning/Critical with threshold-based logic.
  Complexity: L

- [ ] P1 — Replace benchmark with DiskSpd-backed methodology
  Why: Current benchmark (256MB sequential + 500-op random 4K) uses custom file I/O that doesn't control queue depth or thread count, making results incomparable to CrystalDiskMark or any standard benchmark. DiskSpd is MIT-licensed, produces XML output, and is the engine behind CrystalDiskMark.
  Evidence: CrystalDiskMark source (uses DiskSpd), DiskSpd GitHub (MIT), AS SSD / ATTO methodology comparison
  Touches: `ViewModels/ToolsViewModel.cs` (replace RunBenchmarkCore), `Models/BenchmarkResult.cs` (expand), `Views/ToolsView.xaml` (add profile selection)
  Acceptance: Benchmark runs DiskSpd with at least SEQ1M-Q1T1, SEQ1M-Q8T1, RND4K-Q1T1, RND4K-Q32T1 profiles. Results show MB/s and IOPS. Test size is 1 GiB minimum. DiskSpd.exe bundled or downloaded on first use.
  Complexity: L

- [ ] P1 — Integrate Velopack for auto-updates with delta packages
  Why: Current UpdateService.cs only checks for new versions and shows a dialog. It doesn't download or apply updates. Velopack provides delta updates (only changed bytes), automatic apply + relaunch in ~2 seconds, and native GitHub Releases integration.
  Evidence: Velopack 1.2.0 docs, Velopack GithubSource reference
  Touches: `PartitionPilot.csproj` (add Velopack), `Services/UpdateService.cs` (replace), `App.xaml.cs` (add VelopackApp.Build().Run()), build pipeline
  Acceptance: App checks for updates on startup, downloads delta package in background, prompts user to restart to apply. Update cycle verified with a test GitHub Release.
  Complexity: M

- [ ] P1 — Add Administrator Protection compatibility validation
  Why: Windows Administrator Protection changes elevation profile behavior for admin apps, and PartitionPilot creates logs, backups, updates, and temporary command scripts while elevated.
  Evidence: Microsoft Administrator Protection docs, Windows app-security guidance, `src/PartitionPilot/ViewModels/MainViewModel.cs`, `src/PartitionPilot/Services/PartitionTableBackup.cs`, `src/PartitionPilot/Services/UpdateService.cs`
  Touches: `src/PartitionPilot/App.xaml.cs`, `src/PartitionPilot/ViewModels/MainViewModel.cs`, `src/PartitionPilot/Services/PartitionTableBackup.cs`, `src/PartitionPilot/Services/UpdateService.cs`, QA docs/tests
  Acceptance: A manual/automated checklist verifies app launch, portable mode, log export, snapshot location, update check, and destructive-operation prompts under legacy UAC and Administrator Protection; any profile-dependent paths are displayed clearly.
  Complexity: M

- [ ] P1 — Add unsigned release provenance and verification pack
  Why: Code signing and WinGet are blocked on external credentials, but a public admin disk utility can still provide checksums, artifact attestations, and installer verification so users can trust unsigned prerelease artifacts.
  Evidence: `.github/workflows/build.yml`, `installer/PartitionPilot.iss`, `Roadmap_Blocked.md`, GitHub artifact attestation docs
  Touches: `.github/workflows/build.yml`, `installer/`, release artifact scripts, `README.md`
  Acceptance: Release workflow produces installer artifacts, SHA256SUMS, and GitHub artifact attestations; CI verifies installer/version metadata before upload; README documents checksum and attestation verification without requiring a signing certificate.
  Complexity: M


### P2 — Features & UX



- [ ] P2 — Adopt .NET 10 Fluent theme with system dark/light tracking
  Why: The current custom theme system (ThemeService.cs + DarkTheme.xaml + LightTheme.xaml + 697-line AppStyles.xaml) works but is maintenance-heavy and doesn't track the OS theme setting. .NET 10 Fluent theme provides native light/dark/system with Mica backdrop in one line: `<Application ThemeMode="System">`. Eliminates ~830 lines of custom theme XAML.
  Evidence: .NET 10 WPF Fluent theme docs, WPF-UI (lepoco/wpfui) for additional controls
  Touches: `App.xaml`, `Themes/AppStyles.xaml`, `Themes/DarkTheme.xaml`, `Themes/LightTheme.xaml`, `Services/ThemeService.cs`, `MainWindow.xaml`, all Views and Dialogs (add `BasedOn` to custom styles)
  Acceptance: App follows OS dark/light preference automatically. Manual toggle still available. All existing custom control styles preserved via `BasedOn`. Mica backdrop visible on Windows 11. Theme switching works without restart.
  Complexity: L



- [ ] P2 — Add CLI companion for scripted operations
  Why: Sysadmins need automation. AOMEI offers CLI; PowerShell's native storage cmdlets lack merge/split/clone. A thin CLI wrapping the same service layer enables scripted disk management and enterprise adoption.
  Evidence: AOMEI CLI, community demand from r/sysadmin and Spiceworks
  Touches: New `PartitionPilot.Cli` console project sharing service layer, `PartitionPilot.sln`
  Acceptance: `pp.exe list-disks`, `pp.exe list-partitions --disk 0`, `pp.exe format --disk 0 --partition 2 --fs NTFS` work from an elevated command prompt. JSON output mode available. Shares ProcessRunner, WmiDiskService with GUI.
  Complexity: L


- [ ] P2 — Migrate to CommunityToolkit.Mvvm source generators
  Why: Manual `ViewModelBase` + `RelayCommand` pattern produces ~60% boilerplate in ViewModels. CommunityToolkit.Mvvm 8.4.2 source generators (`[ObservableProperty]`, `[RelayCommand]`) eliminate this while adding compile-time safety and 15 new analyzers.
  Evidence: CommunityToolkit.Mvvm 8.4 announcement, Microsoft MVVM source generators guide
  Touches: `PartitionPilot.csproj` (add CommunityToolkit.Mvvm), all `ViewModels/*.cs`, remove `ViewModels/ViewModelBase.cs` and `ViewModels/RelayCommand.cs`
  Acceptance: All ViewModels use `[ObservableProperty]` and `[RelayCommand]` attributes. `ObservableObject` replaces `ViewModelBase`. Existing functionality unchanged. Build succeeds with zero MVVMTK analyzer warnings.
  Complexity: M

- [ ] P2 — Make disk map and treemap fully keyboard and screen-reader accessible
  Why: The treemap is a custom drawn `FrameworkElement` with mouse-only selection and no automation peer, and disk-map segments are visual borders rather than selectable controls.
  Evidence: `src/PartitionPilot/Controls/TreemapControl.cs`, `src/PartitionPilot/Controls/DiskBarControl.xaml.cs`, Microsoft WPF custom UI Automation guidance
  Touches: `src/PartitionPilot/Controls/TreemapControl.cs`, `src/PartitionPilot/Controls/DiskBarControl.xaml`, `src/PartitionPilot/Controls/DiskBarControl.xaml.cs`, related views/tests
  Acceptance: Treemap and disk-map items expose names, roles, sizes, paths/types, selection state, and keyboard navigation; Narrator can identify selected items; high-contrast mode preserves non-color selection cues.
  Complexity: M

- [ ] P2 — Add privacy-preserving support bundle export
  Why: Commercial tools and mature open-source recovery workflows make logs/error reports discoverable; PartitionPilot already has activity logs and partition snapshots but no single diagnostic package.
  Evidence: `src/PartitionPilot/Models/ActivityLog.cs`, `src/PartitionPilot/Services/PartitionTableBackup.cs`, EaseUS error-report support flow, Rescuezilla recovery/support positioning
  Touches: `src/PartitionPilot/Models/ActivityLog.cs`, `src/PartitionPilot/Services/PartitionTableBackup.cs`, `src/PartitionPilot/ViewModels/MainViewModel.cs`, `src/PartitionPilot/Views/SettingsView.xaml`
  Acceptance: A user can export a zip containing app version, OS/build, recent activity log, selected redacted WMI disk/partition metadata, and chosen partition snapshots; serial numbers and full user profile paths are redacted by default.
  Complexity: M

- [ ] P2 — Add mismatch-checked snapshot recovery plan export
  Why: Snapshots are now visible and exportable, but recovery guidance stops at diagnostics; users need a safer, mismatch-checked recovery plan before any destructive restore work is considered.
  Evidence: `src/PartitionPilot/Services/PartitionTableBackup.cs`, `src/PartitionPilot/Views/SnapshotBrowserView.xaml`, MiniTool partition recovery positioning, Clonezilla/Rescuezilla restore workflows
  Touches: `src/PartitionPilot/Services/PartitionTableBackup.cs`, `src/PartitionPilot/ViewModels/SnapshotBrowserViewModel.cs`, `src/PartitionPilot/Views/SnapshotBrowserView.xaml`, tests
  Acceptance: Selecting a snapshot can export a read-only recovery plan that verifies disk number/name/size/style against the current disk, highlights mismatches, lists safe diagnostic commands first, and refuses to generate executable destructive restore commands.
  Complexity: M

- [ ] P2 — Add deterministic simulation mode for UI QA and screenshots
  Why: UI automation, screenshots, and accessibility checks should not depend on the operator's live disk layout or require destructive-capable services during test runs.
  Evidence: `src/PartitionPilot/Services/WmiDiskService.cs`, `tests/PartitionPilot.Tests/PartitionPilot.Tests.csproj`, FlaUI README, Microsoft WPF UI Automation docs
  Touches: `src/PartitionPilot/Services/IWmiDiskService.cs`, new simulated disk data provider, `src/PartitionPilot/App.xaml.cs`, view-model construction/tests
  Acceptance: A test-only or command-line flag loads deterministic sample disks, partitions, SMART data, snapshots, and disk-usage entries; destructive execution remains disabled; UI tests and screenshot capture can run without touching live disks.
  Complexity: M

- [ ] P2 — Add WPF UI automation smoke coverage
  Why: The app has unit coverage but no end-to-end UI smoke tests for shell navigation, destructive confirmation copy, or accessibility names across custom WPF controls.
  Evidence: `tests/PartitionPilot.Tests/PartitionPilot.Tests.csproj`, `src/PartitionPilot/Controls/TreemapControl.cs`, `src/PartitionPilot/Controls/DiskBarControl.xaml.cs`, FlaUI README, Microsoft WPF UI Automation docs
  Touches: new `tests/PartitionPilot.UiTests` project, app launch/test hooks, custom controls, `.github/workflows/build.yml`
  Acceptance: A Windows UI test launches PartitionPilot in simulation mode, verifies the main tabs load, critical controls expose stable automation names, destructive dialogs can be cancelled, and disk map/treemap accessible semantics are present.
  Complexity: M

- [ ] P2 — Identify and protect unsupported or non-Windows partition types
  Why: PartitionPilot is Windows-focused, while competitors expose broad filesystem awareness; the safe parity move is accurate identification and guarded actions, not unsupported write support.
  Evidence: `src/PartitionPilot/Models/PartitionInfo.cs`, `src/PartitionPilot/Services/WmiDiskService.cs`, GParted/KDE filesystem support lists, Microsoft `CreatePartition` partition-type docs
  Touches: `src/PartitionPilot/Models/PartitionInfo.cs`, `src/PartitionPilot/Services/WmiDiskService.cs`, `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`, `src/PartitionPilot/Views/PartitionsView.xaml`
  Acceptance: EFI/MSR/Recovery/Linux/Btrfs/LUKS/unknown GPT types are labeled distinctly where detectable, destructive actions are disabled or require stronger confirmation for unsupported types, and the UI explains that PartitionPilot will not edit unsupported filesystems.
  Complexity: M

- [ ] P2 — Upgrade System.Management to 10.x with WMI compatibility smoke
  Why: WMI is the app's core storage integration and the app package is behind the current stable `System.Management` release while vulnerability scanning currently reports clean.
  Evidence: `src/PartitionPilot/PartitionPilot.csproj`, `src/PartitionPilot/packages.lock.json`, `dotnet list src\PartitionPilot\PartitionPilot.csproj package --outdated`, NuGet `System.Management` 10.0.9
  Touches: `src/PartitionPilot/PartitionPilot.csproj`, `src/PartitionPilot/packages.lock.json`, `tests/PartitionPilot.Tests/packages.lock.json`, `src/PartitionPilot/Services/WmiDiskService.cs`, CI restore/test workflow
  Acceptance: `System.Management` is updated to 10.x, lock files are regenerated, build/tests pass in locked mode, vulnerable-package scans remain clean, and a Windows smoke check verifies disk/partition/volume/SMART/BitLocker/image queries.
  Complexity: S

- [ ] P2 — Quarantine or remove the legacy PowerShell prototype
  Why: `PartitionPilot.ps1` remains at repo root even though the product is now a WPF app, creating ambiguity for users, packaging, and future research agents.
  Evidence: `PartitionPilot.ps1`, `README.md`, `src/PartitionPilot/PartitionPilot.csproj`
  Touches: `PartitionPilot.ps1`, `README.md`, packaging scripts
  Acceptance: The legacy script is removed from release packaging and either deleted or moved into an explicitly deprecated tooling location with no user-facing claim that it is the current app.
  Complexity: S

- [ ] P2 — Add structured native-command audit records with redaction
  Why: `ProcessRunner` logs raw command strings and full paths, which is useful for diagnosis but hard to correlate, redact, or include safely in support bundles.
  Evidence: `src/PartitionPilot/Services/ProcessRunner.cs`, `src/PartitionPilot/Models/ActivityLog.cs`, support-bundle roadmap item
  Touches: `src/PartitionPilot/Services/ProcessRunner.cs`, `src/PartitionPilot/Models/ActivityLog.cs`, support bundle exporter, tests
  Acceptance: Every native command run has an operation ID, command kind, target disk/volume, redacted display command, exit code, duration, and cleanup status; support bundle export uses redacted records by default.
  Complexity: M

- [ ] P2 — Sync operator docs and blocked-roadmap dependencies after v0.3.0
  Why: Agent/operator notes still describe four tabs and blocked signing/WinGet items without linking them to the now-active release packaging work, which can mislead future implementation passes.
  Evidence: `CLAUDE.md`, `Roadmap_Blocked.md`, `README.md`, `ROADMAP.md`
  Touches: `CLAUDE.md`, `Roadmap_Blocked.md`, `README.md`, release workflow docs
  Acceptance: Internal working notes reflect current tabs/features, blocked items state the exact unblocking release artifact/signing prerequisites, and no future agent starts from stale v0.2.x assumptions.
  Complexity: S


### P3 — Future Considerations

- [ ] P3 — Add MFT-direct scanning for instant disk usage analysis on NTFS
  Why: Current disk usage scans via `Directory.EnumerateFiles` which takes minutes on large drives. WizTree reads the NTFS Master File Table directly via `FSCTL_ENUM_USN_DATA`, completing in seconds. Up to 46x faster on NTFS volumes.
  Evidence: WizTree performance claims, NTFS MFT documentation
  Touches: New `Services/MftScanner.cs` (P/Invoke to DeviceIoControl), `ViewModels/DiskUsageViewModel.cs`
  Acceptance: NTFS volumes scan in under 10 seconds regardless of file count. Falls back to directory enumeration for non-NTFS volumes. File count and total size match directory enumeration results within 1%.
  Complexity: L

- [ ] P3 — Extract core logic into a separate PartitionPilot.Core library
  Why: All disk operations, WMI queries, and models live in the GUI project. Extracting them into a class library enables: the CLI companion (P2), future WinUI 3 frontend, unit testing without WPF dependencies, and third-party integrations. KDE Partition Manager uses this pattern (kpmcore library + GUI shell).
  Evidence: KDE kpmcore architecture, separation of concerns principle
  Touches: New `PartitionPilot.Core` project, move `Services/`, `Models/` into it, update references
  Acceptance: `PartitionPilot.Core.dll` contains all services, models, and interfaces. GUI project references Core. CLI project references Core. All existing tests pass against Core.
  Complexity: M

- [ ] P3 — Explore VDS COM API for partition operations
  Why: Currently all partition operations shell out to diskpart via temp script files. The Virtual Disk Service (VDS) COM API provides programmatic partition creation/deletion/formatting without subprocess overhead. Rufus uses VDS for partition operations.
  Evidence: Rufus source code (vds.c), Microsoft VDS API documentation
  Touches: New `Services/VdsService.cs` (COM interop), `Services/IPartitionService.cs` interface
  Acceptance: Create, delete, and format operations can optionally use VDS instead of diskpart. VDS path benchmarked against diskpart path for correctness and speed.
  Complexity: XL

- [ ] P3 — Add i18n readiness after dialog and resource boundaries stabilize
  Why: Full localization is premature while strings are hardcoded, but release/update tooling and WPF both support localization paths that should not be blocked by scattered UI text.
  Evidence: `src/PartitionPilot/Views/`, `src/PartitionPilot/Dialogs/`, WPF .NET 10 localization/accessibility fixes, Velopack localization support
  Touches: `src/PartitionPilot/Views/`, `src/PartitionPilot/Dialogs/`, `src/PartitionPilot/Services/MessageBoxDialogService.cs`, resource files
  Acceptance: User-facing strings are extractable through resources or a string catalog, pseudo-localization catches clipping in major views/dialogs, and right-to-left layout is consciously documented as supported or unsupported.
  Complexity: M

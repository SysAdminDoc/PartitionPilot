# PartitionPilot Roadmap

## Research-Driven Additions

- [ ] P1 - Replace recovery scan with fast/deep/resumable modes
  Why: scanning every 512-byte sector across an entire disk can make large-disk recovery impractically slow.
  Evidence: `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs`; https://www.cgsecurity.org/wiki/TestDisk; https://www.diskgenius.com/manual/recover-lost-partitions.php
  Touches: `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs`, recovery CLI output, GUI recovery surface if added, tests
  Acceptance: fast mode checks common partition boundaries and filesystem superblock offsets; deep mode is cancellable and resumable; duplicate candidates are coalesced; exported reports include scan mode and coverage.
  Complexity: L

- [ ] P1 - Add release and update artifact verification
  Why: installer and update paths do not yet enforce project-level hash/signature verification, even though disk tools require high trust.
  Evidence: `installer/PartitionPilot.iss`; `src/PartitionPilot.Core/Services/UpdateService.cs`; https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool; https://github.com/velopack/velopack
  Touches: `installer/PartitionPilot.iss`, release build scripts/docs, `src/PartitionPilot.Core/Services/UpdateService.cs`, tests
  Acceptance: local release builds produce SHA256 manifests and use Authenticode signing when a cert is configured; update checks verify expected hashes/signatures before apply; unsigned builds display explicit local-test status.
  Complexity: M

- [ ] P2 - Finish localization for XAML and automation names
  Why: many visible strings and `AutomationProperties.Name` values remain hardcoded despite the `.resx` localization pipeline.
  Evidence: `src/PartitionPilot/MainWindow.xaml`; `src/PartitionPilot/Views/*.xaml`; `src/PartitionPilot/Dialogs/*.xaml`; `src/PartitionPilot/Properties/Strings.resx`
  Touches: XAML views/dialogs, `src/PartitionPilot/Properties/Strings*.resx`, `tests/PartitionPilot.Tests/LocExtensionTests.cs`
  Acceptance: hardcoded user-visible English strings are either converted to `LocExtension` or documented as non-localized product/protocol tokens; pseudo-locale tests cover automation names and dialog strings.
  Complexity: M

- [ ] P2 - Add WinPE-compatible rescue distribution profile
  Why: commercial partition tools and GParted/Rescuezilla all treat offline rescue media as a high-value recovery path.
  Evidence: https://rescuezilla.com/; https://gparted.org/livecd.php; https://www.aomeitech.com/pa/; https://www.easeus.com/partition-manager/
  Touches: publish scripts/docs, `src/PartitionPilot.Cli/`, native-tool discovery, release artifacts
  Acceptance: local build creates a portable rescue folder that runs in WinPE or reports missing prerequisites clearly; CLI diagnostics verifies WMI, DiskPart, DISM, BitLocker, and storage API availability in WinPE.
  Complexity: XL

- [ ] P2 - Extract oversized workflow orchestration into Core services
  Why: large view models and top-level CLI code hide safety logic and limit unit testing of disk operations.
  Evidence: `ToolsViewModel.cs` 1477 lines; `PartitionsViewModel.cs` 1141 lines; `DiskCloningViewModel.cs` 839 lines; `src/PartitionPilot.Cli/Program.cs` 767 lines
  Touches: `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/`, `src/PartitionPilot.Cli/Program.cs`, tests
  Acceptance: wipe, image, clone, layout-plan, and support-bundle orchestration have Core service boundaries with unit tests; view models only bind state and invoke services.
  Complexity: XL

- [ ] P3 - Document layout specs, encrypted image format, release verification, and recovery scan modes
  Why: shipped advanced workflows are discoverable in README but not specified enough for operators to automate or audit safely.
  Evidence: `README.md`; `src/PartitionPilot.Core/Models/PartitionLayoutSpec.cs`; `src/PartitionPilot.Core/Services/ImageEncryptionService.cs`; `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs`
  Touches: `README.md`, `CLAUDE.md`, `CHANGELOG.md`
  Acceptance: docs include layout JSON schema examples, encryption compatibility notes, recovery scan mode tradeoffs, and release verification steps without adding extra markdown files.
  Complexity: S

- [ ] P1 - Enforce filesystem-operation capability gates
  Why: the support matrix is currently dialog-local guidance, while GUI and CLI operation paths need a single fail-closed policy before queuing DiskPart or PowerShell work.
  Evidence: `src/PartitionPilot/Dialogs/FilesystemSupportDialog.xaml.cs`; `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`; https://gparted.org/features.php; https://invent.kde.org/system/kpmcore
  Touches: `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`, `src/PartitionPilot.Cli/Program.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: a Core capability service returns create/format/resize/extend/check/label availability plus localized reason text; GUI disables or blocks invalid actions; CLI plan/apply fails before invoking native tools; tests cover NTFS, FAT32, exFAT, ReFS, ext, APFS, HFS+, Linux swap, and LUKS.
  Complexity: M

- [ ] P1 - Add VSS writer-health preflight to image capture
  Why: current VSS availability only checks providers, but consistent live-volume images depend on writer health and should fail closed or explain the fallback.
  Evidence: `src/PartitionPilot.Core/Services/VssSnapshotService.cs`; `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`; https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/vssadmin-list-writers; https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-overview
  Touches: `src/PartitionPilot.Core/Services/VssSnapshotService.cs`, `src/PartitionPilot.Core/Services/EnvironmentDiagnostics.cs`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: image capture preflights providers and writers, parses healthy and failed writer states, records VSS evidence in diagnostics/support bundles, blocks or requires an explicit degraded-mode path for failed writers, and unit tests cover representative `vssadmin list writers` output.
  Complexity: M

- [ ] P2 - Make UI automation smoke tests release-gating
  Why: five FlaUI smoke tests exist and discover locally, but the current noninteractive run skipped all of them, leaving UI/accessibility regressions without a release signal.
  Evidence: `tests/PartitionPilot.UiTests/SmokeTests.cs`; `rtk dotnet test .\tests\PartitionPilot.UiTests\PartitionPilot.UiTests.csproj -c Release --no-restore`; https://api.xunit.net/v3/3.0.1/v3.3.0.1-Xunit.Assert.SkipWhen.html
  Touches: `tests/PartitionPilot.UiTests/`, release validation scripts/docs, `README.md`, `CLAUDE.md`
  Acceptance: a documented local command builds the app, runs simulation-mode UI tests in an interactive desktop session, fails when all tests skip unless an explicit headless flag is set, and saves screenshots/logs under the release artifact area on failure.
  Complexity: M

- [ ] P2 - Add curated drive-health advisory metadata
  Why: PartitionPilot collects SMART/NVMe data but still relies heavily on raw attributes and generic text, while mature health tools use curated drive and attribute metadata to turn telemetry into guidance.
  Evidence: `src/PartitionPilot.Core/Services/SmartQueryService.cs`; `src/PartitionPilot.Core/Services/WmiDiskService.cs`; `src/PartitionPilot.Core/Services/SmartHistoryService.cs`; https://www.smartmontools.org/; https://github.com/smartmontools/smartmontools/blob/master/smartmontools/drivedb.h; https://crystalmark.info/en/software/crystaldiskinfo/
  Touches: `src/PartitionPilot.Core/Models/SmartData.cs`, `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/DiskHealthViewModel.cs`, `src/PartitionPilot.Cli/Program.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: a local metadata layer maps known SATA/NVMe/USB attributes to names, severity, and explanations; unknown attributes remain visible as raw data; disk-health UI/CLI show advisory text with metadata version; tests cover known and unknown attribute fallback.
  Complexity: L

- [ ] P1 — Snapshot destructive whole-disk targets before restore, clone, and wipe
  Why: partition operations preserve recovery evidence, but whole-disk restore/clone/wipe paths can destroy the target layout without first saving a target snapshot.
  Evidence: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`; `src/PartitionPilot/ViewModels/ToolsViewModel.cs`; `src/PartitionPilot.Core/Services/PartitionTableBackup.cs`; https://kbx.macrium.com/macrium-reflect-x/validating-backups-images-can-be-restored
  Touches: `src/PartitionPilot.Core/Services/PartitionTableBackup.cs`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, `src/PartitionPilot/ViewModels/ToolsViewModel.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: image restore, sector clone destination, whole-disk wipe, DoD wipe, and NVMe sanitize flows save a timestamped target snapshot with disk identity before destructive execution; failures show the snapshot path; support bundles include the latest relevant snapshot; tests cover success and snapshot-write failure logging.
  Complexity: M

- [ ] P1 — Verify user-created and restored disk images
  Why: release artifacts have a verification item, but user WIM/VHDX/encrypted images still need capture/restore integrity proof.
  Evidence: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`; `src/PartitionPilot.Core/Services/ImageEncryptionService.cs`; https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-image-management-command-line-options-s14?view=windows-11; https://partclone.org/features/; https://github.com/rescuezilla/rescuezilla/issues/441
  Touches: `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, `src/PartitionPilot.Cli/Program.cs` if image commands are added, `tests/PartitionPilot.Tests/`
  Acceptance: WIM capture/apply uses DISM integrity/verification switches where supported; VHDX capture writes a manifest with source volume identity, file counts, byte totals, and hashes for sampled or full verification modes; encrypted images preserve/verifiably bind the manifest; restore validates the manifest before clearing the target unless explicitly bypassed with a logged degraded state.
  Complexity: L

- [ ] P2 — Add smartctl dependency diagnostics and self-test gating
  Why: SMART self-tests call `smartctl`, but diagnostics do not report its path/version or device-mode limitations before users click self-test actions.
  Evidence: `src/PartitionPilot.Core/Services/SmartTestService.cs`; `src/PartitionPilot.Core/Services/EnvironmentDiagnostics.cs`; https://github.com/smartmontools/smartmontools/releases/tag/RELEASE_7_5; https://github.com/smartmontools/smartmontools/issues/499
  Touches: `src/PartitionPilot.Core/Services/SmartTestService.cs`, `src/PartitionPilot.Core/Services/EnvironmentDiagnostics.cs`, `src/PartitionPilot/ViewModels/DiskHealthViewModel.cs`, `src/PartitionPilot/Views/DiskHealthView.xaml`, `tests/PartitionPilot.Tests/`
  Acceptance: diagnostics report `smartctl` availability, version, and remediation; Disk Health disables or labels self-test actions when unavailable; device-path selection accounts for physical disk, NVMe, and USB bridge cases; tests cover available, missing, and unsupported-device output.
  Complexity: M

- [ ] P2 — Add post-clone and post-restore bootability audit
  Why: clone/restore workflows can succeed at copying bytes while leaving Windows boot files, EFI entries, or WinRE state unusable.
  Evidence: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`; `src/PartitionPilot/ViewModels/ToolsViewModel.cs`; https://superuser.com/questions/347693/clonezilla-verify-image-fails; https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdboot-command-line-options-techref-di?view=windows-11
  Touches: `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, `src/PartitionPilot.Cli/Program.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: after WIM restore, VHDX restore, or sector clone, PartitionPilot audits target GPT/MBR style, EFI/System partition presence, BCD files, and WinRE status when a Windows installation is detected; the UI/CLI reports pass/warn/fail and offers a non-destructive boot-repair plan without auto-running destructive fixes.
  Complexity: M

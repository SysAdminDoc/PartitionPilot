# PartitionPilot Roadmap

## Research-Driven Additions

### P0
- [ ] P0 — Persist operation queue journals for crash recovery
  Why: Pending destructive operations currently live only in memory, so a crash or elevation/session boundary loses what was queued, completed, failed, or skipped.
  Evidence: `src/PartitionPilot.Core/Services/OperationQueue.cs`; GParted pending-operation details log
  Touches: `src/PartitionPilot.Core/Services/OperationQueue.cs`, `src/PartitionPilot/ViewModels/PartitionsViewModel.cs`, `src/PartitionPilot/Views/PartitionsView.xaml`, `tests/PartitionPilot.Tests`
  Acceptance: Queued/applied operations write a redacted journal in ProgramData or portable storage, startup detects interrupted journals, and users can review/retry/discard preserved pending operations.
  Complexity: L

### P1
- [ ] P1 — Sign all release artifacts
  Why: PartitionPilot is an elevated disk utility; hashes and attestations help provenance, but unsigned EXE/CLI/installer artifacts still create SmartScreen and tampering-trust friction.
  Evidence: Microsoft code-signing options; `.github/workflows/build.yml`
  Touches: `.github/workflows/build.yml`, `installer/PartitionPilot.iss`, release scripts, README release notes
  Acceptance: GUI EXE, CLI EXE, installer, and Velopack packages are signed in CI; signatures are verified before upload; unsigned release paths fail the workflow.
  Complexity: L

- [ ] P1 — Add SBOM and full artifact provenance coverage
  Why: CI attests only `PartitionPilot.exe`; consumers need dependency inventory and provenance for the CLI, installer, update packages, and checksums.
  Evidence: GitHub artifact attestations; CycloneDX .NET; `.github/workflows/build.yml`
  Touches: `.github/workflows/build.yml`, package lock files, release artifact layout
  Acceptance: CI emits CycloneDX SBOM, attests SBOM plus every released binary/package/checksum file, and uploads SBOM as a release artifact.
  Complexity: M

- [ ] P1 — Use VSS-backed live volume image capture
  Why: Current DISM/robocopy capture reads live volumes directly, while VSS is the Windows mechanism for point-in-time backup consistency.
  Evidence: Microsoft VSS documentation; `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`
  Touches: new core image service, `DiskCloningViewModel`, process runner abstractions, cleanup scope, tests
  Acceptance: WIM/VHDX capture creates and cleans a VSS snapshot when available, logs VSS writer/provider failures clearly, and falls back only after explicit user confirmation.
  Complexity: L

- [ ] P1 — Activate deterministic WPF UI and accessibility tests in CI
  Why: All FlaUI smoke tests are skipped and CI runs only unit tests, leaving the destructive-action shell, simulation mode, and UIA contract unverified.
  Evidence: `tests/PartitionPilot.UiTests/SmokeTests.cs`; `.github/workflows/build.yml`; Microsoft WPF UI Automation guidance
  Touches: `tests/PartitionPilot.UiTests`, `.github/workflows/build.yml`, `src/PartitionPilot/App.xaml.cs`, simulation data
  Acceptance: CI builds a testable app, runs non-skipped `--simulate` FlaUI smoke tests, verifies major tabs and key automation names, and captures screenshots/logs on failure.
  Complexity: M

- [ ] P1 — Version persisted JSON schemas and migrations
  Why: Snapshot and SMART-history files are unversioned JSON payloads, which makes future model changes brittle and can silently discard corrupted history.
  Evidence: `src/PartitionPilot.Core/Services/PartitionTableBackup.cs`; `src/PartitionPilot.Core/Services/SmartHistoryService.cs`
  Touches: snapshot models, SMART history models, support bundle redaction, tests
  Acceptance: Persisted files use schema-version envelopes, old v0 payloads still load, corrupt files are quarantined with a user-visible log entry, and migrations are unit-tested.
  Complexity: M

- [ ] P1 — Correct Storage Spaces membership and expose pool health
  Why: The current pool guard is useful but association is imprecise and does not show health, operational status, read-only reason, or virtual-disk mapping.
  Evidence: `src/PartitionPilot.Core/Services/WmiDiskService.cs`; Microsoft `MSFT_StoragePool`
  Touches: `WmiDiskService`, disk/physical disk models, Partitions and Disk Health views, tests
  Acceptance: Pooled disks are mapped to the correct pool/virtual disk, pool health/status/read-only reason are displayed, and destructive guards name the exact affected pool.
  Complexity: M

### P2
- [ ] P2 — Add guarded CLI plan/apply operation automation
  Why: README describes scripted disk management, but `pp.exe` currently exposes read-only inventory/health/snapshot commands; commercial competitors expose command-line partition operations.
  Evidence: `src/PartitionPilot.Cli/Program.cs`; AOMEI command-line documentation; README CLI section
  Touches: `src/PartitionPilot.Cli`, `PartitionPilot.Core` operation models, operation queue/journal, tests, README
  Acceptance: CLI supports `plan` output for at least create/delete/format/resize/change-letter operations, requires explicit `--apply` plus confirmations for destructive commands, and returns structured JSON.
  Complexity: L

- [ ] P2 — Finish localization coverage with pseudo-locale gating
  Why: The repo has a `LocExtension` and 77 neutral strings, but only `Strings.resx` exists and most views/dialogs still contain inline English copy.
  Evidence: `src/PartitionPilot/Properties/Strings.resx`; `src/PartitionPilot/Converters/LocExtension.cs`; XAML view/dialog literals
  Touches: XAML views/dialogs, view-model messages, resource files, test/lint script
  Acceptance: User-facing strings move to resources, at least one pseudo-locale renders without clipping in simulation mode, and CI fails on new hardcoded user-facing literals.
  Complexity: M

- [ ] P2 — Add preflight environment diagnostics
  Why: The app depends on WMI namespaces, native Windows tools, DiskSpd download/cache integrity, admin/elevation context, BitLocker provider access, VSS, and Storage Spaces providers, but diagnostics are mostly available only after a tool fails.
  Evidence: `CLAUDE.md`; `ProcessRunner.cs`; `WmiDiskService.cs`; `DiskSpdService.cs`; Microsoft Storage Management API
  Touches: new diagnostics service, Tools view, CLI command, support bundle, tests
  Acceptance: GUI and CLI expose a read-only diagnostics report with provider/tool availability, versions/hashes, elevation context, and remediation text; support bundles include the redacted report.
  Complexity: M

- [ ] P2 — Add SMART history export, import, and retention controls
  Why: SMART history and trend alerts exist, but operational users need portable evidence, retention tuning, and migration between portable/elevated contexts.
  Evidence: `src/PartitionPilot.Core/Services/SmartHistoryService.cs`; CrystalDiskInfo/smartmontools
  Touches: `SmartHistoryService`, Disk Health view, support bundle, tests
  Acceptance: Users can export/import redacted SMART history, adjust retention from the UI, and see retention/import failures as actionable log entries.
  Complexity: M

- [ ] P2 — Add operation impact preview before Apply
  Why: The queue lists pending operations, but users need a before/after disk-map preview and affected-volume summary before committing destructive changes.
  Evidence: `src/PartitionPilot.Core/Services/OperationQueue.cs`; `src/PartitionPilot/Views/PartitionsView.xaml`; GParted/AOMEI apply workflows
  Touches: operation queue model, partition view model, disk bar preview UI, tests
  Acceptance: Pending operations render an estimated post-apply layout and risk summary, with clear fallback text when exact prediction is unavailable.
  Complexity: M

### P3
- [ ] P3 — Explore read-only lost-partition scanning
  Why: Partition recovery is a high-value competitor feature, but PartitionPilot should begin with non-destructive GPT/MBR signature scanning and recovery evidence only.
  Evidence: TestDisk; DiskGenius lost-partition recovery; `PartitionTableBackup.cs`
  Touches: new recovery scan service, Snapshot tab or Recovery view, tests, support bundle
  Acceptance: A read-only scan reports candidate lost partitions with offsets/sizes/filesystem hints and exports evidence without writing partition-table changes.
  Complexity: XL

- [ ] P3 — Prepare WinGet distribution after signing
  Why: A signed installer can be made easier to discover and install through the Windows Package Manager community repository.
  Evidence: `installer/PartitionPilot.iss`; Microsoft `winget-create`
  Touches: release workflow, installer URL/version metadata, README install section
  Acceptance: Release artifacts provide stable signed installer URLs and a validated WinGet manifest can be generated/submitted without manual metadata edits.
  Complexity: M

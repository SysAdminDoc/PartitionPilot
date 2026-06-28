# PartitionPilot Roadmap

## Research-Driven Additions

- [ ] P1 - Add stable disk identity to destructive plans and confirmations
  Why: disk number, friendly name, and size are not enough to identify a target after reboot, hotplug, or storage reordering.
  Evidence: `src/PartitionPilot.Core/Models/DiskInfo.cs`; `src/PartitionPilot.Core/Services/WmiDiskService.cs`; https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
  Touches: `src/PartitionPilot.Core/Models/DiskInfo.cs`, `src/PartitionPilot.Core/Services/WmiDiskService.cs`, partition/clone/layout view models, CLI JSON output, tests
  Acceptance: disk records expose stable ID/path/serial fields where available; destructive confirmations and persisted layout/journal records include them; apply flows warn or block when identity no longer matches.
  Complexity: M

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

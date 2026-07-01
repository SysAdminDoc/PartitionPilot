# PartitionPilot Roadmap

## Research-Driven Additions

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

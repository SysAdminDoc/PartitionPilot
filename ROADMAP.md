# PartitionPilot Roadmap

## Research-Driven Additions

- [ ] P3 - Document layout specs, encrypted image format, release verification, and recovery scan modes
  Why: shipped advanced workflows are discoverable in README but not specified enough for operators to automate or audit safely.
  Evidence: `README.md`; `src/PartitionPilot.Core/Models/PartitionLayoutSpec.cs`; `src/PartitionPilot.Core/Services/ImageEncryptionService.cs`; `src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs`
  Touches: `README.md`, `CLAUDE.md`, `CHANGELOG.md`
  Acceptance: docs include layout JSON schema examples, encryption compatibility notes, recovery scan mode tradeoffs, and release verification steps without adding extra markdown files.
  Complexity: S

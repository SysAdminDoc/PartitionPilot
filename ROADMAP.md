# PartitionPilot Roadmap

## Research-Driven Additions

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

- [ ] P2 - Add post-clone and post-restore bootability audit
  Why: clone/restore workflows can succeed at copying bytes while leaving Windows boot files, EFI entries, or WinRE state unusable.
  Evidence: `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`; `src/PartitionPilot/ViewModels/ToolsViewModel.cs`; https://superuser.com/questions/347693/clonezilla-verify-image-fails; https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdboot-command-line-options-techref-di?view=windows-11
  Touches: `src/PartitionPilot.Core/Services/`, `src/PartitionPilot/ViewModels/DiskCloningViewModel.cs`, `src/PartitionPilot.Cli/Program.cs`, `tests/PartitionPilot.Tests/`
  Acceptance: after WIM restore, VHDX restore, or sector clone, PartitionPilot audits target GPT/MBR style, EFI/System partition presence, BCD files, and WinRE status when a Windows installation is detected; the UI/CLI reports pass/warn/fail and offers a non-destructive boot-repair plan without auto-running destructive fixes.
  Complexity: M

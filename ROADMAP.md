# PartitionPilot Roadmap

Actionable work only. Historical and completed roadmap material is archived in CHANGELOG.md; blocked work is kept in Roadmap_Blocked.md.

## Actionable Items

- [ ] P2 — SmartHistoryService HTML report does not HTML-encode interpolated values
  Why: Disk model names or attribute names with HTML metacharacters produce malformed HTML
  Where: src/PartitionPilot.Core/Services/SmartHistoryService.cs

- [ ] P2 — exFAT recovery scanner uses hardcoded 512-byte sector size
  Why: exFAT VolumeLength field's sector size comes from BytesPerSectorShift, not always 512
  Where: src/PartitionPilot.Core/Services/PartitionRecoveryScanner.cs

- [ ] P2 — MftScanner.EnumerateMft lacks record-length bounds validation
  Why: USN_RECORD fields read up to offset+58 without checking recordLength >= 60
  Where: src/PartitionPilot.Core/Services/MftScanner.cs

- [ ] P3 — Password prompt dialog in DiskCloningViewModel ignores theme
  Why: Inline Window construction doesn't apply DialogWindow style or theme resources
  Where: src/PartitionPilot/ViewModels/DiskCloningViewModel.cs

- [ ] P3 — ConfirmWorkflowPrompts and VerifyDiskIdentityBeforeExecuteAsync duplicated
  Why: Identical methods in ToolsViewModel and DiskCloningViewModel should be shared
  Where: src/PartitionPilot/ViewModels/ToolsViewModel.cs, DiskCloningViewModel.cs

- [ ] P3 — ThemeService.SystemEvents handler never unsubscribed
  Why: Can fire on background thread after dispatcher shutdown during app exit
  Where: src/PartitionPilot/Services/ThemeService.cs

- [ ] P3 — DiskHealthViewModel event subscriptions never unsubscribed
  Why: _tempMonitor and _perfCounters never stopped/disposed on window close
  Where: src/PartitionPilot/ViewModels/DiskHealthViewModel.cs

- [ ] P3 — boot-audit exit code 1 (Warning) conflicts with error convention
  Why: Scripts checking $LASTEXITCODE -ne 0 treat warnings as hard failures
  Where: src/PartitionPilot.Cli/Program.cs

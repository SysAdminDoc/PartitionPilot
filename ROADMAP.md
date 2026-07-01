# PartitionPilot Roadmap

- [ ] P1 — Hex viewer uses hardcoded 512-byte sector size for LBA offset
  Why: 4Kn drives use 4096-byte logical sectors; LBA-to-byte offset is wrong past LBA 0
  Where: src/PartitionPilot/ViewModels/HexViewerViewModel.cs

- [ ] P1 — SmartQueryService uses array index instead of disk number for LHM device lookup
  Why: LibreHardwareMonitor enumeration order may not match Windows disk numbering
  Where: src/PartitionPilot.Core/Services/SmartQueryService.cs

- [ ] P2 — DiskHealthViewModel perf-counter and temperature events fire on thread pool
  Why: DiskPerfCounterService.Updated and TemperatureMonitorService events not marshaled to UI
  Where: src/PartitionPilot.Core/Services/DiskPerfCounterService.cs, TemperatureMonitorService.cs

- [ ] P2 — PartitionsViewModel IsBusy race between LoadDisksAsync and LoadPartitionsAsync
  Why: Disk refresh sets IsBusy=false while partition loading is still in progress
  Where: src/PartitionPilot/ViewModels/PartitionsViewModel.cs

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

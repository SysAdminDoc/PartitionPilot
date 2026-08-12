# PartitionPilot Roadmap

Actionable work only. Historical and completed roadmap material is archived in CHANGELOG.md; blocked work is kept in Roadmap_Blocked.md.

## Actionable Items

- [ ] P3 — Password prompt dialog in DiskCloningViewModel ignores theme
  Why: Inline Window construction doesn't apply DialogWindow style or theme resources
  Where: src/PartitionPilot/ViewModels/DiskCloningViewModel.cs

- [ ] P3 — ConfirmWorkflowPrompts and VerifyDiskIdentityBeforeExecuteAsync duplicated
  Why: Identical methods in ToolsViewModel and DiskCloningViewModel should be shared
  Where: src/PartitionPilot/ViewModels/ToolsViewModel.cs, DiskCloningViewModel.cs

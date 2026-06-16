![Version](https://img.shields.io/badge/version-0.2.0-4CC2FF)
![License](https://img.shields.io/badge/license-MIT-5EE0A0)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-F4C96A)

# PartitionPilot

PartitionPilot is a Windows disk partition management tool for power users and IT administrators. It provides a WPF interface for partition operations, disk health checks, maintenance tools, and disk image workflows.

## Features

- Partition overview with disk map, partition table, and contextual actions.
- Create, delete, format, resize, extend, split, hide, and drive-letter operations.
- Disk health and SMART reliability data with 4K alignment review.
- BitLocker encryption status per volume.
- Maintenance tools: MBR to GPT conversion, filesystem repair, optimization/TRIM, secure wipe, boot repair, surface test, and benchmarking.
- Disk image workflows for mounting, dismounting, and creating VHD/VHDX images.
- Disk usage analysis with top-folder size breakdown.
- Disk cloning: create and restore WIM/VHDX images.
- Dark and light theme with persistent preference.
- Activity log with export and auto-save.
- Cancellable long-running operations with progress reporting.
- Startup update check against GitHub Releases.
- Screen reader accessibility (AutomationProperties on all interactive controls).

## Requirements

- Windows 10 or Windows 11.
- Administrator privileges for disk operations.
- .NET 10 SDK to build from source.

## Build

```powershell
dotnet build .\src\PartitionPilot\PartitionPilot.csproj -m:1
```

The project targets `net10.0-windows` and publishes as a self-contained Windows x64 app.

## Run

```powershell
dotnet run --project .\src\PartitionPilot\PartitionPilot.csproj
```

For real disk operations, run the built executable from an elevated session so Windows storage APIs and native tools have the required permissions.

## Safety

Partition changes can be destructive. Verify the selected disk, partition, and operation before applying changes, and keep current backups before resizing, formatting, deleting, or wiping disks.

## License

MIT. See [LICENSE](LICENSE).

# Third-party components

PartitionPilot's own code is covered by the [MIT License](LICENSE). Its bundled dependencies keep their original licenses and copyright notices. Copies are included in [licenses](licenses/), which ships beside the app and CLI in both release packages.

## Mozilla Public License components

The following libraries are used without source changes. Each source link opens the revision recorded by the published NuGet package. You can download its source through GitHub's **Code** menu or clone that repository and check out the listed revision. These source files are available under MPL 2.0, not PartitionPilot's MIT license.

| Component | Version | Source revision |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | [3d331e3](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701) |
| BlackSharp.Core | 1.0.7 | [c70b735](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0) |
| DiskInfoToolkit | 1.1.2 | [25319ea](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea) |
| RAMSPDToolkit-NDD | 1.4.2 | [3b47b96](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce) |

Each component's folder contains the upstream MPL 2.0 license. DiskInfoToolkit's upstream third-party notices are included too.

## Other dependencies

| Component | Version | License information |
| --- | --- | --- |
| HidSharp | 2.6.4 | Apache 2.0. Copyright James F. Bellinger. The full notice and license are copied from its NuGet package. |
| Velopack | 1.2.0 | MIT. Copyright Caelan Sayler and Velopack Ltd. [Package source](https://github.com/velopack/velopack/tree/f2edcbcafb81da5b3c884aaea330e225ad91d8b6). |
| Mono.Posix.NETStandard | 1.0.0 | Microsoft and Mono notices. Its NuGet license link points to [Mono's license collection](https://github.com/mono/mono/blob/0f53e9e151d92944cacab3e24ac359410c606df6/LICENSE). That collection and the patent grant are included unchanged. |
| System.IO.FileSystem.AccessControl | 5.0.0 | MIT and included third-party notices. |
| System.IO.Ports | 10.0.3 | MIT and included third-party notices. |
| System.Management | 10.0.11 | MIT and included third-party notices. |
| .NET and Windows Desktop runtimes | 10.0.11 | MIT and included third-party notices from the self-contained runtime packages. |

Microsoft components are published through the [.NET project](https://github.com/dotnet/dotnet). Their license texts and notices remain unchanged in the corresponding folders. The bundled .NET runtime also supplies the Windows performance-counter APIs used by PartitionPilot.

DiskSpd and smartctl are not bundled in these packages. DiskSpd is downloaded from Microsoft only when requested; smartctl is an optional separate installation.

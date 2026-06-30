[Setup]
AppName=PartitionPilot
AppVersion=0.9.7
AppPublisher=SysAdminDoc
AppPublisherURL=https://github.com/SysAdminDoc/PartitionPilot
DefaultDirName={autopf}\PartitionPilot
DefaultGroupName=PartitionPilot
OutputBaseFilename=PartitionPilot-0.9.7-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\PartitionPilot.exe
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts

[Files]
Source: "..\src\PartitionPilot\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish\pp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\PartitionPilot.Cli\bin\Release\net10.0-windows\win-x64\publish\pp.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PartitionPilot"; Filename: "{app}\PartitionPilot.exe"
Name: "{autodesktop}\PartitionPilot"; Filename: "{app}\PartitionPilot.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\PartitionPilot.exe"; Description: "Launch PartitionPilot"; Flags: nowait postinstall skipifsilent

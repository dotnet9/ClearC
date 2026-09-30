; ClearC Windows installer.
; Build from the repository root with Inno Setup 6 and pass /DAppVersion=x.y.z.
; /DAppArch selects the target architecture: x64 (default) or x86.

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

#ifndef AppArch
#define AppArch "x64"
#endif

#if AppArch != "x64" && AppArch != "x86"
#error AppArch must be x64 or x86
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-" + AppArch + "\ClearC"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{BD0B3FEE-9372-438C-8B55-E3CE4A4E9DB6}
AppName=ClearC
AppVersion={#AppVersion}
AppPublisher=Dotnet9
AppPublisherURL=https://github.com/dotnet9/ClearC
AppSupportURL=https://github.com/dotnet9/ClearC/issues
DefaultDirName={autopf}\ClearC
DefaultGroupName=ClearC
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=ClearC-v{#AppVersion}-win-{#AppArch}-setup
Compression=lzma2/ultra64
SolidCompression=yes
#if AppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
PrivilegesRequired=admin
ChangesAssociations=no
CloseApplications=yes
RestartApplications=yes
CloseApplicationsFilter=ClearC.Desktop.exe
UninstallDisplayIcon={app}\ClearC.Desktop.exe
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ClearC"; Filename: "{app}\ClearC.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ClearC"; Filename: "{app}\ClearC.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ClearC.Desktop.exe"; Description: "Launch ClearC"; Flags: nowait postinstall skipifsilent

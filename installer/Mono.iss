#define AppName "Mono"
#define AppVersion "0.3.0"
#define AppPublisher "sailxx"
#define AppUrl "https://github.com/sailxx/Mono"
#define AppExe "Mono.exe"

[Setup]
AppId={{7F20345C-235C-4EC5-BA4B-6D46404A53A9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases/latest
DefaultDirName={localappdata}\Programs\Mono
DefaultGroupName=Mono
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\Mono\Assets\Mono.ico
OutputDir=..\artifacts
OutputBaseFilename=Mono-Setup
VersionInfoVersion=0.3.0.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Mono Windows authenticator installer
VersionInfoProductName=Mono
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
Uninstallable=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык Mono на рабочем столе"; GroupDescription: "Ярлыки:"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\src\Mono\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Mono"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\Mono"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить Mono"; Flags: postinstall nowait skipifsilent

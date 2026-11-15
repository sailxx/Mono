#define AppName "WinOTP"
#define AppVersion "0.3.2"
#define AppPublisher "sailxx"
#define AppUrl "https://github.com/sailxx/WinOTP"
#define AppExe "WinOTP.exe"

[Setup]
AppId={{7F20345C-235C-4EC5-BA4B-6D46404A53A9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases/latest
DefaultDirName={localappdata}\Programs\WinOTP
DefaultGroupName=WinOTP
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\WinOTP\Assets\WinOTP.ico
OutputDir=..\artifacts
OutputBaseFilename=WinOTP-Setup
VersionInfoVersion=0.3.2.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=WinOTP Windows authenticator installer
VersionInfoProductName=WinOTP
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
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a WinOTP desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\src\WinOTP\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WinOTP"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\WinOTP"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch WinOTP"; Flags: postinstall nowait skipifsilent

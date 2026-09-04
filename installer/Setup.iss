; Inno Setup script for AOG CAN Bridge.
; Compile with: iscc Setup.iss /DMyAppVersion=1.2.3
; Expects these to already be built (see .github/workflows/release.yml):
;   ..\AogCanBridge\bin\Release\AogCanBridge.exe (+ Languages\*.lang, .exe.config)
;   ..\PcanBasicBridgeClient\build\Release\PCANBasic.dll  (the bridge proxy)

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

[Setup]
AppId={{3A194DDE-99B2-4FCE-80D8-77D2D321B606}
AppName=AOG CAN Bridge
AppVersion={#MyAppVersion}
AppPublisher=AgOpenGPS
AppPublisherURL=https://github.com/gunicsba/AOG-CAN-Bridge
DefaultDirName={autopf}\AOG CAN Bridge
DefaultGroupName=AOG CAN Bridge
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=AogCanBridge-Setup-{#MyAppVersion}
SetupIconFile=..\AogCanBridge\favicon.ico
UninstallDisplayIcon={app}\AogCanBridge.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "patchclients"; Description: "Patch AgOpenGPS Virtual Terminal / Task Controller now (they must already be installed; safe to re-run after updating either one)"; GroupDescription: "CAN bridge integration:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start AOG CAN Bridge automatically (minimized) when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\AogCanBridge\bin\Release\AogCanBridge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AogCanBridge\bin\Release\AogCanBridge.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AogCanBridge\bin\Release\Languages\*.lang"; DestDir: "{app}\Languages"; Flags: ignoreversion
Source: "..\Vendor\PCANBasic.dll"; DestDir: "{app}\Vendor"; Flags: ignoreversion
Source: "..\PcanBasicBridgeClient\build\Release\PCANBasic.dll"; DestDir: "{app}\Proxy"; Flags: ignoreversion
Source: "..\Install-Bridge.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Restore-DirectPcan.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AOG CAN Bridge"; Filename: "{app}\AogCanBridge.exe"
Name: "{group}\Uninstall AOG CAN Bridge"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AOG CAN Bridge"; Filename: "{app}\AogCanBridge.exe"; Tasks: desktopicon
Name: "{commonstartup}\AOG CAN Bridge"; Filename: "{app}\AogCanBridge.exe"; Parameters: "--autostart --minimized"; Tasks: autostart

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-Bridge.ps1"""; StatusMsg: "Patching Virtual Terminal and Task Controller..."; Tasks: patchclients; Flags: runhidden waituntilterminated
Filename: "{app}\AogCanBridge.exe"; Description: "Launch AOG CAN Bridge"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Restore-DirectPcan.ps1"""; RunOnceId: "RestoreDirectPcan"; Flags: runhidden waituntilterminated

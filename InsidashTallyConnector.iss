[Setup]
AppName=Insidash Tally Connector
AppVersion=1.0.0
AppPublisher=Insidash
DefaultDirName={commonpf}\InsidashTallyConnector
DefaultGroupName=Insidash
OutputDir=.\installer_output
OutputBaseFilename=InsidashTallyConnector_Setup_v1.0.0
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
; Require Windows 8.1+ (Service Control Manager APIs)
MinVersion=6.3

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Main executable and config from the project build folders
Source: ".\Insidash.TallyConnector\bin\Release\net48\Insidash.TallyConnector.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\Insidash.TallyConnector\bin\Release\net48\Insidash.TallyConnector.exe.config"; DestDir: "{app}"; Flags: ignoreversion
; All DLL dependencies
Source: ".\Insidash.TallyConnector\bin\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\Insidash.TallyConnector\bin\Release\net48\Insidash.BLL.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: ".\Insidash.TallyConnector\bin\Release\net48\Insidash.DAL.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: ".\Insidash.TallyConnector\bin\Release\net48\EntityFramework.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\Insidash.TallyConnector\bin\Release\net48\EntityFramework.SqlServer.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Shortcut in Start Menu for tray mode
Name: "{group}\Insidash Tally Connector"; Filename: "{app}\Insidash.TallyConnector.exe"
; Add to Windows startup so tray icon launches on login
Name: "{commonstartup}\Insidash Tally Connector"; Filename: "{app}\Insidash.TallyConnector.exe"

[Run]
; Install and start the Windows Service automatically after install
Filename: "{dotnet4032}\InstallUtil.exe"; Parameters: """{app}\Insidash.TallyConnector.exe"""; \
    StatusMsg: "Registering Windows Service..."; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start InsidashTallyConnector"; \
    StatusMsg: "Starting connector..."; Flags: runhidden waituntilterminated

; Launch the tray app immediately so user can see activation window
Filename: "{app}\Insidash.TallyConnector.exe"; \
    Description: "Launch Insidash Tally Connector"; Flags: postinstall nowait

[UninstallRun]
; Stop and uninstall service on uninstall
Filename: "sc.exe"; Parameters: "stop InsidashTallyConnector"; Flags: runhidden
Filename: "{dotnet4032}\InstallUtil.exe"; Parameters: "/u ""{app}\Insidash.TallyConnector.exe"""; \
    Flags: runhidden waituntilterminated

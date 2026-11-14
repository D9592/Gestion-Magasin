#define AppName "GestionMagasin"
#define AppVersion "1.0.0"
#define AppPublisher "MABIKA Dufrey"

[Setup]
AppId={{D85F7669-1224-4C49-9D98-0C68AD9C66A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\GestionMagasin
DefaultGroupName=GestionMagasin
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\GestionMagasin.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\Distribution
OutputBaseFilename=GestionMagasin_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=GestionMagasin.exe
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
LicenseFile=..\LICENSE
DisableProgramGroupPage=yes
Uninstallable=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: checkedonce

[Files]
Source: "..\Distribution\GestionMagasin.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\THIRD-PARTY-LICENSES.md"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\Distribution\Guide_Installation.pdf"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\Distribution\Guide_Deploiement.pdf"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\Distribution\Guide_Utilisateur.pdf"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "..\Distribution\PV_Recette_T01-T18.pdf"; DestDir: "{app}\Documentation"; Flags: ignoreversion

[Icons]
Name: "{group}\GestionMagasin"; Filename: "{app}\GestionMagasin.exe"
Name: "{autodesktop}\GestionMagasin"; Filename: "{app}\GestionMagasin.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GestionMagasin.exe"; Description: "Lancer GestionMagasin"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\GestionMagasin.exe')) then
  begin
    if not Exec(ExpandConstant('{app}\GestionMagasin.exe'), '--backup-before-update', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := 'La sauvegarde avant mise à jour n’a pas pu être démarrée. Aucune mise à jour n’a été effectuée.'
    else if ExitCode <> 0 then
      Result := 'La sauvegarde avant mise à jour a échoué. Aucune mise à jour n’a été effectuée.';
  end;
end;

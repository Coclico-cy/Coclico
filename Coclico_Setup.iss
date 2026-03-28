#define MyAppName             "Coclico"
#define MyAppVersion          "1.0.4"
#define MyAppPublisher        "coclico-cy"
#define MyAppURL              "https://coclico.app"
#define MyAppGitHub           "https://github.com/coclico-cy/coclico"
#define MyAppExeName          "Coclico.exe"
#define MyAppDescription      "Intelligent Windows System Manager"
#define MyAppCopyright        "© 2026 Coclico"
#define MyAppContact          "contact@coclico.app"
#define MyAppReadme           "README.md"
#define MyAppReadmeFr         "README.fr.md"
#define MySourceDir           "Coclico\publish"
#define MyIcon                "Coclico\Resources\icone\plage.ico"
#define MyIconDir             "Coclico\Resources\icone"
#define MyAiModelFile         "Coclico\resource\model\IA-support-chat.gguf"
#define MyCoclicoProgID       "CoclicoFlowChain"
#define MyAppMutex            "CoclicoSetupMutex_F3A2C1D0"

[Setup]
AppId={{F3A2C1D0-8B4E-4F7A-9C6D-2E5B0A1D3F8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppGitHub}/releases
AppComments={#MyAppDescription}
AppCopyright={#MyAppCopyright}
AppContact={#MyAppContact}
AppReadmeFile={#MyAppURL}/docs
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
DisableReadyMemo=no
DisableFinishedPage=no
OutputDir=Installer
OutputBaseFilename=Coclico_Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=1048576
LZMANumFastBytes=273
WizardStyle=modern
WizardResizable=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Coclico {#MyAppVersion} — Intelligent Windows System Manager
VersionInfoCopyright={#MyAppCopyright}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoTextVersion={#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#MyIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=yes
SetupMutex={#MyAppMutex}
CloseApplications=yes
RestartApplications=yes
DirExistsWarning=no
AlwaysRestart=no
ChangesEnvironment=no
ChangesAssociations=yes
MinVersion=10.0.19041
TouchDate=none
TouchTime=none
TimeStampsInUTC=yes
SetupLogging=yes
InfoBeforeFile=README.md
InfoAfterFile=README.fr.md

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Components]
Name: "main"; \
  Description: "Coclico — Core Application (required)"; \
  Types: full compact custom; \
  Flags: fixed

Name: "aimodel"; \
  Description: "Local AI Model — IA-support-chat.gguf (requires 4 GB+ RAM, large download)"; \
  Types: full; \
  Flags: disablenouninstallwarning

Name: "flowchains_samples"; \
  Description: "Sample Flow Chains — ready-to-use automation templates"; \
  Types: full compact; \
  Flags: disablenouninstallwarning

[CustomMessages]
en.OptionsGroup=Startup options
fr.OptionsGroup=Options de démarrage
de.OptionsGroup=Startoptionen
es.OptionsGroup=Opciones de inicio
it.OptionsGroup=Opzioni di avvio
pt.OptionsGroup=Opções de inicialização

en.RunAtStartup=Launch {#MyAppName} automatically when Windows starts
fr.RunAtStartup=Lancer {#MyAppName} automatiquement au démarrage de Windows
de.RunAtStartup={#MyAppName} automatisch beim Windows-Start ausführen
es.RunAtStartup=Iniciar {#MyAppName} automáticamente al arrancar Windows
it.RunAtStartup=Avviare {#MyAppName} automaticamente all'avvio di Windows
pt.RunAtStartup=Iniciar {#MyAppName} automaticamente com o Windows

en.MinimizeToTray=Minimize {#MyAppName} to the system tray when closing
fr.MinimizeToTray=Réduire {#MyAppName} dans le tray système à la fermeture
de.MinimizeToTray={#MyAppName} beim Schließen in die Taskleiste minimieren
es.MinimizeToTray=Minimizar {#MyAppName} a la bandeja del sistema al cerrar
it.MinimizeToTray=Riduci {#MyAppName} nell'area di notifica alla chiusura
pt.MinimizeToTray=Minimizar {#MyAppName} para a bandeja ao fechar

en.DesktopIcon=Create a shortcut on the desktop
fr.DesktopIcon=Créer un raccourci sur le bureau
de.DesktopIcon=Verknüpfung auf dem Desktop erstellen
es.DesktopIcon=Crear un acceso directo en el escritorio
it.DesktopIcon=Crea collegamento sul desktop
pt.DesktopIcon=Criar atalho na área de trabalho

en.TaskbarPin=Pin {#MyAppName} to the taskbar
fr.TaskbarPin=Épingler {#MyAppName} à la barre des tâches
de.TaskbarPin={#MyAppName} an die Taskleiste anheften
es.TaskbarPin=Anclar {#MyAppName} a la barra de tareas
it.TaskbarPin=Aggiungi {#MyAppName} alla barra delle applicazioni
pt.TaskbarPin=Fixar {#MyAppName} na barra de tarefas

en.LaunchAfterInstall=Launch {#MyAppName} now
fr.LaunchAfterInstall=Lancer {#MyAppName} maintenant
de.LaunchAfterInstall={#MyAppName} jetzt starten
es.LaunchAfterInstall=Abrir {#MyAppName} ahora
it.LaunchAfterInstall=Avvia {#MyAppName} ora
pt.LaunchAfterInstall=Abrir {#MyAppName} agora

en.WelcomeLabel2=This wizard will install [name/ver] on your computer.%n%nCoclico is an open-source intelligent Windows system manager: application library, visual automation (Flow Chains), installer, deep cleaning, RAM optimizer and local AI assistant.%n%nClick Next to continue.
fr.WelcomeLabel2=Cet assistant va installer [name/ver] sur votre ordinateur.%n%nCoclico est un gestionnaire système Windows open-source intelligent : bibliothèque d'applications, automatisation visuelle (Flow Chains), installateur, nettoyage profond, optimiseur RAM et assistant IA local.%n%nCliquez sur Suivant pour continuer.
de.WelcomeLabel2=Dieser Assistent installiert [name/ver] auf Ihrem Computer.%n%nCoclico ist ein intelligenter Open-Source-Windows-Systemmanager: App-Bibliothek, visuelle Automatisierung (Flow Chains), Installer, Tiefenreinigung, RAM-Optimierer und lokaler KI-Assistent.%n%nKlicken Sie auf Weiter, um fortzufahren.
es.WelcomeLabel2=Este asistente instalará [name/ver] en su equipo.%n%nCoclico es un gestor de sistema Windows open-source inteligente: biblioteca de aplicaciones, automatización visual (Flow Chains), instalador, limpieza profunda, optimizador RAM y asistente IA local.%n%nHaga clic en Siguiente para continuar.
it.WelcomeLabel2=Questa procedura installerà [name/ver] nel computer.%n%nCoclico è un gestore di sistema Windows open-source intelligente: libreria app, automazione visuale (Flow Chains), programma di installazione, pulizia avanzata, ottimizzatore RAM e assistente IA locale.%n%nFare clic su Avanti per continuare.
pt.WelcomeLabel2=Este assistente instalará [name/ver] no seu computador.%n%nO Coclico é um gerenciador open-source inteligente do sistema Windows: biblioteca de apps, automação visual (Flow Chains), instalador, limpeza profunda, otimizador de RAM e assistente IA local.%n%nClique em Avançar para continuar.

en.ReadyMemoTasks=Selected tasks:
fr.ReadyMemoTasks=Tâches sélectionnées :
de.ReadyMemoTasks=Ausgewählte Aufgaben:
es.ReadyMemoTasks=Tareas seleccionadas:
it.ReadyMemoTasks=Operazioni selezionate:
pt.ReadyMemoTasks=Tarefas selecionadas:

en.PrereqTitle=System Prerequisites
fr.PrereqTitle=Prérequis système
de.PrereqTitle=Systemvoraussetzungen
es.PrereqTitle=Requisitos del sistema
it.PrereqTitle=Requisiti di sistema
pt.PrereqTitle=Requisitos do sistema

en.PrereqDesc=Checking your system configuration:
fr.PrereqDesc=Vérification de la configuration système :
de.PrereqDesc=Systemkonfiguration wird geprüft:
es.PrereqDesc=Comprobando la configuración del sistema:
it.PrereqDesc=Verifica della configurazione di sistema:
pt.PrereqDesc=Verificando a configuração do sistema:

en.ComponentMain=Core application files (always installed)
fr.ComponentMain=Fichiers de l'application principale (toujours installés)
de.ComponentMain=Hauptanwendungsdateien (immer installiert)
es.ComponentMain=Archivos de la aplicación principal (siempre instalados)
it.ComponentMain=File dell'applicazione principale (sempre installati)
pt.ComponentMain=Arquivos do aplicativo principal (sempre instalados)

en.ComponentAiModel=Local AI model file — IA-support-chat.gguf
fr.ComponentAiModel=Fichier du modèle IA local — IA-support-chat.gguf
de.ComponentAiModel=Lokale KI-Modelldatei — IA-support-chat.gguf
es.ComponentAiModel=Archivo del modelo IA local — IA-support-chat.gguf
it.ComponentAiModel=File del modello IA locale — IA-support-chat.gguf
pt.ComponentAiModel=Arquivo do modelo IA local — IA-support-chat.gguf

en.ComponentSamples=Sample automation Flow Chain templates
fr.ComponentSamples=Modèles d'automatisation Flow Chain exemples
de.ComponentSamples=Beispiel-Automatisierungs-Flow-Chain-Vorlagen
es.ComponentSamples=Plantillas de Flow Chain de automatización de ejemplo
it.ComponentSamples=Modelli di Flow Chain di automazione di esempio
pt.ComponentSamples=Modelos de Flow Chain de automação de exemplo

en.AiModelWarning=The local AI model requires 4 GB+ RAM to operate correctly. If the model file is not bundled in this installer, download it separately from the project releases page.
fr.AiModelWarning=Le modèle IA local nécessite 4 Go+ de RAM pour fonctionner correctement. Si le fichier modèle n'est pas inclus dans cet installateur, téléchargez-le séparément depuis la page des versions du projet.
de.AiModelWarning=Das lokale KI-Modell benötigt 4 GB+ RAM. Falls die Modelldatei nicht in diesem Installer enthalten ist, laden Sie sie separat von der Projektseite herunter.
es.AiModelWarning=El modelo IA local requiere 4 GB+ de RAM. Si el archivo del modelo no está incluido en este instalador, descárguelo por separado desde la página de versiones del proyecto.
it.AiModelWarning=Il modello IA locale richiede 4 GB+ di RAM. Se il file del modello non è incluso in questo installer, scaricarlo separatamente dalla pagina delle versioni del progetto.
pt.AiModelWarning=O modelo IA local requer 4 GB+ de RAM. Se o arquivo do modelo não estiver incluído neste instalador, baixe-o separadamente na página de versões do projeto.

[Tasks]
Name: "desktopicon"; \
  Description: "{cm:DesktopIcon}"; \
  Flags: checkedonce

Name: "startup"; \
  Description: "{cm:RunAtStartup}"; \
  GroupDescription: "{cm:OptionsGroup}"; \
  Flags: unchecked

Name: "minimizetray"; \
  Description: "{cm:MinimizeToTray}"; \
  GroupDescription: "{cm:OptionsGroup}"; \
  Flags: unchecked

Name: "taskbarpin"; \
  Description: "{cm:TaskbarPin}"; \
  GroupDescription: "{cm:OptionsGroup}"; \
  Flags: unchecked

[Dirs]
Name: "{userappdata}\{#MyAppName}"
Name: "{userappdata}\{#MyAppName}\logs"
Name: "{userappdata}\{#MyAppName}\flow-chains"
Name: "{userappdata}\{#MyAppName}\rollback"
Name: "{userappdata}\{#MyAppName}\audit"
Name: "{userappdata}\{#MyAppName}\resource"
Name: "{userappdata}\{#MyAppName}\resource\model"

[Files]
Source: "{#MySourceDir}\*"; \
  DestDir: "{app}"; \
  Components: main; \
  Flags: ignoreversion recursesubdirs createallsubdirs

Source: "{#MyIcon}"; \
  DestDir: "{app}"; \
  Components: main; \
  Flags: ignoreversion

Source: "README.md"; \
  DestDir: "{app}"; \
  Components: main; \
  Flags: ignoreversion

Source: "README.fr.md"; \
  DestDir: "{app}"; \
  Components: main; \
  Flags: ignoreversion

Source: "{#MyAiModelFile}"; \
  DestDir: "{userappdata}\{#MyAppName}\resource\model"; \
  Components: aimodel; \
  Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; \
  Filename: "{app}\{#MyAppExeName}"; \
  IconFilename: "{app}\plage.ico"; \
  Comment: "{#MyAppDescription}"; \
  WorkingDir: "{app}"

Name: "{autoprograms}\{#MyAppName}\{cm:UninstallProgram,{#MyAppName}}"; \
  Filename: "{uninstallexe}"

Name: "{autodesktop}\{#MyAppName}"; \
  Filename: "{app}\{#MyAppExeName}"; \
  IconFilename: "{app}\plage.ico"; \
  Comment: "{#MyAppDescription}"; \
  WorkingDir: "{app}"; \
  Tasks: desktopicon

[Registry]
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Tasks: startup; \
  Flags: uninsdeletevalue

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "InstallPath"; \
  ValueData: "{app}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "Version"; \
  ValueData: "{#MyAppVersion}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "Publisher"; \
  ValueData: "{#MyAppPublisher}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "InstallDate"; \
  ValueData: "{code:GetInstallDate}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "CodePatcherAuditOnly"; \
  ValueData: "true"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; \
  ValueType: string; \
  ValueName: "RepositoryURL"; \
  ValueData: "{#MyAppGitHub}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\.coclico"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "{#MyCoclicoProgID}"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\.coclico"; \
  ValueType: string; \
  ValueName: "Content Type"; \
  ValueData: "application/x-coclico-flowchain"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\.coclico"; \
  ValueType: string; \
  ValueName: "PerceivedType"; \
  ValueData: "document"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\{#MyCoclicoProgID}"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "Coclico Flow Chain"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\{#MyCoclicoProgID}"; \
  ValueType: string; \
  ValueName: "FriendlyTypeName"; \
  ValueData: "Coclico Flow Chain Automation"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\{#MyCoclicoProgID}\DefaultIcon"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "{app}\{#MyAppExeName},0"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\{#MyCoclicoProgID}\shell\open\command"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: """{app}\{#MyAppExeName}"" ""%1"""; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\{#MyCoclicoProgID}\shell\open"; \
  ValueType: string; \
  ValueName: "FriendlyAppName"; \
  ValueData: "{#MyAppName}"; \
  Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchAfterInstall}"; \
  WorkingDir: "{app}"; \
  Flags: nowait postinstall skipifsilent shellexec


[UninstallRun]
Filename: "{cmd}"; \
  Parameters: "/C taskkill /IM ""{#MyAppExeName}"" /F 2>nul"; \
  Flags: runhidden; \
  RunOnceId: "KillCoclico"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[Code]

type
  TMemoryStatusEx = record
    dwLength                : DWORD;
    dwMemoryLoad            : DWORD;
    ullTotalPhys            : Int64;
    ullAvailPhys            : Int64;
    ullTotalPageFile        : Int64;
    ullAvailPageFile        : Int64;
    ullTotalVirtual         : Int64;
    ullAvailVirtual         : Int64;
    ullAvailExtendedVirtual : Int64;
  end;

var
  PrereqPage  : TWizardPage;
  PrereqMemo  : TNewMemo;
  RamMB       : Int64;
  DiskFreeMB  : Int64;
  OsBuild     : Integer;

function GlobalMemoryStatusEx(var lpBuffer: TMemoryStatusEx): BOOL;
  external 'GlobalMemoryStatusEx@kernel32.dll stdcall';

function GetDiskFreeSpaceEx(lpDirectoryName: String; var lpFreeBytesAvailableToCaller: Int64; var lpTotalNumberOfBytes: Int64; var lpTotalNumberOfFreeBytes: Int64): BOOL;
  external 'GetDiskFreeSpaceExW@kernel32.dll stdcall';

function GetPhysicalMemoryMB(): Int64;
var
  MemStatus : TMemoryStatusEx;
begin
  MemStatus.dwLength := SizeOf(MemStatus);
  Result := 0;
  if GlobalMemoryStatusEx(MemStatus) then
    Result := MemStatus.ullTotalPhys div (1024 * 1024);
end;

function GetInstallDate(Param: String): String;
begin
  Result := GetDateTimeString('yyyy-mm-dd', '-', ':');
end;

function InitializeSetup(): Boolean;
var
  WinVer : TWindowsVersion;
begin
  Result := True;
  GetWindowsVersionEx(WinVer);
  OsBuild := WinVer.Build;

  if WinVer.Major < 10 then
  begin
    MsgBox(
      '{#MyAppName} {#MyAppVersion} requires Windows 10 or later.' + #13#10 +
      '{#MyAppName} {#MyAppVersion} nécessite Windows 10 ou supérieur.',
      mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  if (WinVer.Major = 10) and (WinVer.Build < 19041) then
  begin
    MsgBox(
      '{#MyAppName} requires Windows 10 version 2004 (build 19041) or later.' + #13#10 +
      'Detected build: ' + IntToStr(WinVer.Build) + #13#10 + #13#10 +
      '{#MyAppName} nécessite Windows 10 version 2004 (build 19041) ou supérieur.' + #13#10 +
      'Build détecté : ' + IntToStr(WinVer.Build),
      mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  if not IsWin64 then
  begin
    MsgBox(
      '{#MyAppName} requires a 64-bit (x64) version of Windows.' + #13#10 +
      '{#MyAppName} nécessite une version 64 bits (x64) de Windows.',
      mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;
end;

procedure InitializeWizard();
var
  ReqText      : String;
  RamWarning   : String;
  FreeBytesA   : Int64;
  FreeBytesT   : Int64;
  TotalBytes   : Int64;
  WinVer       : TWindowsVersion;
begin
  GetWindowsVersionEx(WinVer);
  OsBuild    := WinVer.Build;
  RamMB      := GetPhysicalMemoryMB();
  DiskFreeMB := 0;

  GetDiskFreeSpaceEx(ExpandConstant('{autopf}'), FreeBytesA, TotalBytes, FreeBytesT);
  DiskFreeMB := FreeBytesA div (1024 * 1024);

  PrereqPage := CreateCustomPage(
    wpWelcome,
    ExpandConstant('{cm:PrereqTitle}'),
    ExpandConstant('{cm:PrereqDesc}'));

  if RamMB < 4096 then
    RamWarning := '  [WARNING] RAM < 4 GB detected — AI model may not function optimally.' + #13#10 +
                  '  [AVERTISSEMENT] RAM < 4 Go — le modèle IA peut ne pas fonctionner de manière optimale.' + #13#10
  else
    RamWarning := '';

  ReqText :=
    '================================================================' + #13#10 +
    '  COCLICO {#MyAppVersion} — System Compatibility Report' + #13#10 +
    '================================================================' + #13#10 +
    '' + #13#10 +
    '  OS Build         : ' + IntToStr(OsBuild) + #13#10 +
    '  RAM Installed    : ' + IntToStr(RamMB) + ' MB' + #13#10 +
    '  Free Disk Space  : ' + IntToStr(DiskFreeMB) + ' MB' + #13#10 +
    '' + #13#10 +
    '----------------------------------------------------------------' + #13#10 +
    '  CONFIGURATION MINIMALE REQUISE' + #13#10 +
    '  · Windows 10 build 19041+ ou Windows 11' + #13#10 +
    '  · Processeur x64 (64 bits)' + #13#10 +
    '  · 500 Mo d''espace disque libre' + #13#10 +
    '  · 2 Go RAM minimum' + #13#10 +
    '  · 4 Go RAM recommandés (requis pour le modèle IA)' + #13#10 +
    '' + #13#10 +
    '----------------------------------------------------------------' + #13#10 +
    '  STATUS' + #13#10 +
    RamWarning;

  if RamMB < 2048 then
    ReqText := ReqText +
      '  [ERROR] Insufficient RAM: ' + IntToStr(RamMB) + ' MB detected. Minimum is 2048 MB.' + #13#10
  else
    ReqText := ReqText +
      '  [OK] RAM: ' + IntToStr(RamMB) + ' MB' + #13#10;

  if DiskFreeMB < 500 then
    ReqText := ReqText +
      '  [ERROR] Insufficient disk space: ' + IntToStr(DiskFreeMB) + ' MB free. Minimum is 500 MB.' + #13#10
  else
    ReqText := ReqText +
      '  [OK] Disk: ' + IntToStr(DiskFreeMB) + ' MB available' + #13#10;

  ReqText := ReqText +
    '  [OK] OS build: ' + IntToStr(OsBuild) + #13#10 +
    '' + #13#10 +
    '----------------------------------------------------------------' + #13#10 +
    '  OPTIONAL' + #13#10 +
    '  · winget (App Installer) — for the built-in installer module' + #13#10 +
    '  · winget — pour le module installateur intégré' + #13#10 +
    '================================================================';

  PrereqMemo              := TNewMemo.Create(PrereqPage);
  PrereqMemo.Parent       := PrereqPage.Surface;
  PrereqMemo.Left         := 0;
  PrereqMemo.Top          := 0;
  PrereqMemo.Width        := PrereqPage.SurfaceWidth;
  PrereqMemo.Height       := PrereqPage.SurfaceHeight;
  PrereqMemo.ScrollBars   := ssVertical;
  PrereqMemo.ReadOnly     := True;
  PrereqMemo.Text         := ReqText;
  PrereqMemo.Font.Size    := 8;
  PrereqMemo.Font.Name    := 'Consolas';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PrereqPage.ID then
  begin
    if RamMB < 2048 then
    begin
      MsgBox(
        'Insufficient RAM: ' + IntToStr(RamMB) + ' MB detected.' + #13#10 +
        'Coclico requires at least 2048 MB of RAM to install.' + #13#10 + #13#10 +
        'RAM insuffisante : ' + IntToStr(RamMB) + ' Mo détectés.' + #13#10 +
        'Coclico nécessite au moins 2048 Mo de RAM pour l''installation.',
        mbCriticalError, MB_OK);
      Result := False;
      Exit;
    end;
    if DiskFreeMB < 500 then
    begin
      MsgBox(
        'Insufficient disk space: ' + IntToStr(DiskFreeMB) + ' MB available.' + #13#10 +
        'Coclico requires at least 500 MB of free disk space.' + #13#10 + #13#10 +
        'Espace disque insuffisant : ' + IntToStr(DiskFreeMB) + ' Mo disponibles.' + #13#10 +
        'Coclico nécessite au moins 500 Mo d''espace disque libre.',
        mbCriticalError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = wpSelectProgramGroup then
    Result := True;
end;

procedure WriteSettingsJson();
var
  SettingsDir     : String;
  SettingsFile    : String;
  MinimizeToTray  : Boolean;
  LaunchAtStartup : Boolean;
  LangCode        : String;
  MinStr          : String;
  StartStr        : String;
  Json            : String;
  JsonBuf         : AnsiString;
begin
  SettingsDir     := ExpandConstant('{userappdata}\{#MyAppName}');
  SettingsFile    := SettingsDir + '\settings.json';
  MinimizeToTray  := WizardIsTaskSelected('minimizetray');
  LaunchAtStartup := WizardIsTaskSelected('startup');

  if ActiveLanguage() = 'fr' then
    LangCode := 'fr'
  else if ActiveLanguage() = 'de' then
    LangCode := 'de'
  else if ActiveLanguage() = 'es' then
    LangCode := 'es'
  else if ActiveLanguage() = 'it' then
    LangCode := 'it'
  else if ActiveLanguage() = 'pt' then
    LangCode := 'pt'
  else
    LangCode := 'en';

  if not DirExists(SettingsDir) then
    ForceDirectories(SettingsDir);

  if MinimizeToTray then
    MinStr := 'true'
  else
    MinStr := 'false';

  if LaunchAtStartup then
    StartStr := 'true'
  else
    StartStr := 'false';

  if FileExists(SettingsFile) then
  begin
    if LoadStringFromFile(SettingsFile, JsonBuf) then
    begin
      Json := String(JsonBuf);
      if MinimizeToTray then
        StringChange(Json, '"minimizeToTray": false', '"minimizeToTray": true')
      else
        StringChange(Json, '"minimizeToTray": true', '"minimizeToTray": false');
      if LaunchAtStartup then
        StringChange(Json, '"launchAtStartup": false', '"launchAtStartup": true')
      else
        StringChange(Json, '"launchAtStartup": true', '"launchAtStartup": false');
      JsonBuf := AnsiString(Json);
      SaveStringToFile(SettingsFile, JsonBuf, False);
      Exit;
    end;
  end;

  Json :=
    '{' + #13#10 +
    '  "language": "' + LangCode + '",' + #13#10 +
    '  "theme": "Dark",' + #13#10 +
    '  "accentColor": "#6366F1",' + #13#10 +
    '  "themePreset": "Indigo",' + #13#10 +
    '  "backgroundMode": "UltraDark",' + #13#10 +
    '  "cardOpacity": 0.07,' + #13#10 +
    '  "fontSize": 13.0,' + #13#10 +
    '  "sidebarWidth": 220,' + #13#10 +
    '  "compactMode": false,' + #13#10 +
    '  "wingetScope": "machine",' + #13#10 +
    '  "launchAtStartup": ' + StartStr + ',' + #13#10 +
    '  "minimizeToTray": ' + MinStr + ',' + #13#10 +
    '  "firstRun": true,' + #13#10 +
    '  "launchMode": "Normal",' + #13#10 +
    '  "codePatcherAuditOnly": true,' + #13#10 +
    '  "auditRetentionDays": 90,' + #13#10 +
    '  "useVulkanAcceleration": true' + #13#10 +
    '}';

  JsonBuf := AnsiString(Json);
  SaveStringToFile(SettingsFile, JsonBuf, False);
end;

procedure CreateSampleFlowChains();
var
  FlowDir   : String;
  Json1     : String;
  Json2     : String;
  JsonBuf   : AnsiString;
begin
  if not WizardIsComponentSelected('flowchains_samples') then
    Exit;

  FlowDir := ExpandConstant('{userappdata}\{#MyAppName}\flow-chains');

  if not DirExists(FlowDir) then
    ForceDirectories(FlowDir);

  Json1 :=
    '{' + #13#10 +
    '  "Id": "a1b2c3d4-0001-0001-0001-000000000001",' + #13#10 +
    '  "Name": "Quick Temp Cleaner",' + #13#10 +
    '  "Description": "Cleans Windows temporary files and empties the Recycle Bin.",' + #13#10 +
    '  "CreatedAt": "2026-01-01T00:00:00Z",' + #13#10 +
    '  "Steps": [' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "b1c2d3e4-0001-0001-0001-000000000011",' + #13#10 +
    '      "NodeType": "CleanTemp",' + #13#10 +
    '      "Label": "Clear %TEMP% folder",' + #13#10 +
    '      "Parameters": {},' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 0' + #13#10 +
    '    },' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "b1c2d3e4-0001-0001-0001-000000000012",' + #13#10 +
    '      "NodeType": "EmptyRecycleBin",' + #13#10 +
    '      "Label": "Empty Recycle Bin",' + #13#10 +
    '      "Parameters": {},' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 1' + #13#10 +
    '    },' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "b1c2d3e4-0001-0001-0001-000000000013",' + #13#10 +
    '      "NodeType": "CleanPrefetch",' + #13#10 +
    '      "Label": "Clear Windows Prefetch cache",' + #13#10 +
    '      "Parameters": {},' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 2' + #13#10 +
    '    }' + #13#10 +
    '  ]' + #13#10 +
    '}';

  JsonBuf := AnsiString(Json1);
  SaveStringToFile(FlowDir + '\sample-temp-cleaner.coclico', JsonBuf, False);

  Json2 :=
    '{' + #13#10 +
    '  "Id": "a1b2c3d4-0002-0002-0002-000000000002",' + #13#10 +
    '  "Name": "RAM Optimizer",' + #13#10 +
    '  "Description": "Flushes the Windows standby list and optimizes working set memory.",' + #13#10 +
    '  "CreatedAt": "2026-01-01T00:00:00Z",' + #13#10 +
    '  "Steps": [' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "c1d2e3f4-0002-0002-0002-000000000021",' + #13#10 +
    '      "NodeType": "FlushStandbyList",' + #13#10 +
    '      "Label": "Flush Windows standby memory list",' + #13#10 +
    '      "Parameters": {},' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 0' + #13#10 +
    '    },' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "c1d2e3f4-0002-0002-0002-000000000022",' + #13#10 +
    '      "NodeType": "OptimizeWorkingSet",' + #13#10 +
    '      "Label": "Optimize process working sets",' + #13#10 +
    '      "Parameters": {},' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 1' + #13#10 +
    '    },' + #13#10 +
    '    {' + #13#10 +
    '      "Id": "c1d2e3f4-0002-0002-0002-000000000023",' + #13#10 +
    '      "NodeType": "LogEvent",' + #13#10 +
    '      "Label": "Log optimization result",' + #13#10 +
    '      "Parameters": { "Message": "RAM optimization cycle completed." },' + #13#10 +
    '      "OnError": "ContinueNext",' + #13#10 +
    '      "Order": 2' + #13#10 +
    '    }' + #13#10 +
    '  ]' + #13#10 +
    '}';

  JsonBuf := AnsiString(Json2);
  SaveStringToFile(FlowDir + '\sample-ram-optimizer.coclico', JsonBuf, False);
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Summary : String;
begin
  Summary := MemoDirInfo;

  if MemoComponentsInfo <> '' then
    Summary := Summary + NewLine + NewLine + MemoComponentsInfo;

  if MemoTasksInfo <> '' then
    Summary := Summary + NewLine + NewLine + '{cm:ReadyMemoTasks}' + NewLine + MemoTasksInfo;

  Summary := Summary + NewLine + NewLine +
    'RAM detected: ' + IntToStr(RamMB) + ' MB' + NewLine +
    'Disk free   : ' + IntToStr(DiskFreeMB) + ' MB';

  Result := Summary;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteSettingsJson();
    CreateSampleFlowChains();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#MyAppName}');

    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.coclico');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#MyCoclicoProgID}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\{#MyAppPublisher}\{#MyAppName}');
  end;
end;

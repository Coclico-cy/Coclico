; ================================================================
;  Coclico — Script d'installation Inno Setup 6 — ÉDITION COMPLÈTE
;  Application  : WPF .NET 10, win-x64, self-contained
;  Générateur   : Inno Setup 6.3+  (https://jrsoftware.org/isdl.php)
;
;  ÉTAPES AVANT DE COMPILER :
;    1. Publier l'application (une seule fois) :
;         dotnet publish Coclico\Coclico.csproj ^
;           -c Release -r win-x64 --self-contained false ^
;           --output Coclico\publish
;    2. Ouvrir ce fichier dans Inno Setup 6 → Ctrl+F9 (Compile)
;    3. L'installateur est dans :  Installer\Coclico_Setup.exe
; ================================================================

; ── Constantes globales ─────────────────────────────────────────
#define MyAppName         "Coclico"
#define MyAppVersion      "1.0.3"
#define MyAppPublisher    "Coclico"
#define MyAppURL          "https://coclico.app"
#define MyAppExeName      "Coclico.exe"
#define MyAppDescription  "Gestionnaire système Windows intelligent"
#define MyAppCopyright    "© 2025 Coclico"

; Chemin du dossier publié (.NET publish output)
#define MySourceDir       "Coclico\publish"

; Icônes & ressources
#define MyIcon            "Coclico\Resources\icone\plage.ico"
#define MyIconDir         "Coclico\Resources\icone"

; ================================================================
[Setup]

; ── Identité application ────────────────────────────────────────
; !! NE JAMAIS CHANGER l'AppId après la 1ère publication !!
AppId={{F3A2C1D0-8B4E-4F7A-9C6D-2E5B0A1D3F8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppComments={#MyAppDescription}
AppCopyright={#MyAppCopyright}

; ── Dossier d'installation ──────────────────────────────────────
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
DisableReadyMemo=no
DisableFinishedPage=no

; ── Sortie ──────────────────────────────────────────────────────
OutputDir=Installer
OutputBaseFilename=Coclico_Setup

; ── Compression maximale ────────────────────────────────────────
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=1048576
LZMANumFastBytes=273

; ── Interface Wizard ────────────────────────────────────────────
WizardStyle=modern
WizardResizable=no
; Pour ajouter vos propres images BMP :
; WizardImageFile={#MyIconDir}\setup_banner.bmp      (164x314 px)
; WizardSmallImageFile={#MyIconDir}\setup_small.bmp  (55x55 px)

; ── Métadonnées VersionInfo (affichées dans l'Explorateur) ──────
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Installation de {#MyAppName} {#MyAppVersion}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; ── Architecture 64 bits obligatoire ───────────────────────────
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; ── Icônes setup / désinstallation ─────────────────────────────
SetupIconFile={#MyIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

; ── Privilèges ─────────────────────────────────────────────────
; "lowest" → installation sans droits admin par défaut
; L'utilisateur peut choisir d'élever via la boîte de dialogue
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; ── Langue ─────────────────────────────────────────────────────
ShowLanguageDialog=yes

; ── Comportement ───────────────────────────────────────────────
SetupMutex={#MyAppName}SetupMutex
CloseApplications=yes
RestartApplications=yes
DirExistsWarning=no
AlwaysRestart=no

; ================================================================
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

; ================================================================
;  Messages personnalisés — 6 langues
; ================================================================
[CustomMessages]

; ── Groupe des options ──────────────────────────────────────────
en.OptionsGroup=Startup options
fr.OptionsGroup=Options de démarrage
de.OptionsGroup=Startoptionen
es.OptionsGroup=Opciones de inicio
it.OptionsGroup=Opzioni di avvio
pt.OptionsGroup=Opções de inicialização

; ── Lancement auto au démarrage Windows ────────────────────────
en.RunAtStartup=Launch {#MyAppName} automatically when Windows starts
fr.RunAtStartup=Lancer {#MyAppName} automatiquement au démarrage de Windows
de.RunAtStartup={#MyAppName} automatisch beim Windows-Start ausführen
es.RunAtStartup=Iniciar {#MyAppName} automáticamente al arrancar Windows
it.RunAtStartup=Avviare {#MyAppName} automaticamente all'avvio di Windows
pt.RunAtStartup=Iniciar {#MyAppName} automaticamente com o Windows

; ── Réduire dans le tray ────────────────────────────────────────
en.MinimizeToTray=Minimize {#MyAppName} to the system tray when closing
fr.MinimizeToTray=Réduire {#MyAppName} dans le tray système à la fermeture
de.MinimizeToTray={#MyAppName} beim Schließen in die Taskleiste minimieren
es.MinimizeToTray=Minimizar {#MyAppName} a la bandeja del sistema al cerrar
it.MinimizeToTray=Riduci {#MyAppName} nell'area di notifica alla chiusura
pt.MinimizeToTray=Minimizar {#MyAppName} para a bandeja ao fechar

; ── Raccourci bureau ────────────────────────────────────────────
en.DesktopIcon=Create a shortcut on the desktop
fr.DesktopIcon=Créer un raccourci sur le bureau
de.DesktopIcon=Verknüpfung auf dem Desktop erstellen
es.DesktopIcon=Crear un acceso directo en el escritorio
it.DesktopIcon=Crea collegamento sul desktop
pt.DesktopIcon=Criar atalho na área de trabalho

; ── Raccourci barre des tâches ──────────────────────────────────
en.TaskbarPin=Pin {#MyAppName} to the taskbar
fr.TaskbarPin=Épingler {#MyAppName} à la barre des tâches
de.TaskbarPin={#MyAppName} an die Taskleiste anheften
es.TaskbarPin=Anclar {#MyAppName} a la barra de tareas
it.TaskbarPin=Aggiungi {#MyAppName} alla barra delle applicazioni
pt.TaskbarPin=Fixar {#MyAppName} na barra de tarefas

; ── Lancer après installation ───────────────────────────────────
en.LaunchAfterInstall=Launch {#MyAppName} now
fr.LaunchAfterInstall=Lancer {#MyAppName} maintenant
de.LaunchAfterInstall={#MyAppName} jetzt starten
es.LaunchAfterInstall=Abrir {#MyAppName} ahora
it.LaunchAfterInstall=Avvia {#MyAppName} ora
pt.LaunchAfterInstall=Abrir {#MyAppName} agora

; ── Message de bienvenue ────────────────────────────────────────
en.WelcomeLabel2=This wizard will install [name/ver] on your computer.%n%nCoclico is an intelligent Windows system manager: application library, visual automation (Flow Chains), installer, deep cleaning, RAM optimizer and local AI assistant.%n%nClick Next to continue.
fr.WelcomeLabel2=Cet assistant va installer [name/ver] sur votre ordinateur.%n%nCoclico est un gestionnaire système Windows intelligent : bibliothèque d'applications, automatisation visuelle (Flow Chains), installateur, nettoyage profond, optimiseur RAM et assistant IA local.%n%nCliquez sur Suivant pour continuer.
de.WelcomeLabel2=Dieser Assistent installiert [name/ver] auf Ihrem Computer.%n%nCoclico ist ein intelligenter Windows-Systemmanager: App-Bibliothek, visuelle Automatisierung (Flow Chains), Installer, Tiefenreinigung, RAM-Optimierer und lokaler KI-Assistent.%n%nKlicken Sie auf Weiter, um fortzufahren.
es.WelcomeLabel2=Este asistente instalará [name/ver] en su equipo.%n%nCoclico es un gestor de sistema Windows inteligente: biblioteca de aplicaciones, automatización visual (Flow Chains), instalador, limpieza profunda, optimizador RAM y asistente IA local.%n%nHaga clic en Siguiente para continuar.
it.WelcomeLabel2=Questa procedura installerà [name/ver] nel computer.%n%nCoclico è un gestore di sistema Windows intelligente: libreria app, automazione visuale (Flow Chains), programma di installazione, pulizia avanzata, ottimizzatore RAM e assistente IA locale.%n%nFare clic su Avanti per continuare.
pt.WelcomeLabel2=Este assistente instalará [name/ver] no seu computador.%n%nO Coclico é um gerenciador inteligente do sistema Windows: biblioteca de apps, automação visual (Flow Chains), instalador, limpeza profunda, otimizador de RAM e assistente IA local.%n%nClique em Avançar para continuar.

; ── Memo de fin (résumé) ────────────────────────────────────────
en.ReadyMemoTasks=Selected tasks:
fr.ReadyMemoTasks=Tâches sélectionnées :
de.ReadyMemoTasks=Ausgewählte Aufgaben:
es.ReadyMemoTasks=Tareas seleccionadas:
it.ReadyMemoTasks=Operazioni selezionate:
pt.ReadyMemoTasks=Tarefas selecionadas:

; ── Titre de la page prérequis ──────────────────────────────────
en.PrereqTitle=Prerequisites
fr.PrereqTitle=Prérequis système
de.PrereqTitle=Systemvoraussetzungen
es.PrereqTitle=Requisitos del sistema
it.PrereqTitle=Requisiti di sistema
pt.PrereqTitle=Requisitos do sistema

; ── Contenu page prérequis ──────────────────────────────────────
en.PrereqDesc=Minimum requirements:
fr.PrereqDesc=Configuration minimale requise :
de.PrereqDesc=Mindestanforderungen:
es.PrereqDesc=Requisitos mínimos:
it.PrereqDesc=Requisiti minimi:
pt.PrereqDesc=Requisitos mínimos:

; ================================================================
[Tasks]

; Raccourci bureau (coché par défaut)
Name: "desktopicon"; \
  Description: "{cm:DesktopIcon}"; \
  Flags: checkedonce

; Lancement automatique au démarrage Windows (décoché par défaut)
Name: "startup"; \
  Description: "{cm:RunAtStartup}"; \
  GroupDescription: "{cm:OptionsGroup}"; \
  Flags: unchecked

; Réduire dans le tray à la fermeture (décoché par défaut)
Name: "minimizetray"; \
  Description: "{cm:MinimizeToTray}"; \
  GroupDescription: "{cm:OptionsGroup}"; \
  Flags: unchecked

; ================================================================
[Files]

; ── Tous les fichiers du dossier publish (récursif) ─────────────
; Inno Setup compresse tout avec LZMA2/ultra64
Source: "{#MySourceDir}\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; ── Icône copiée à la racine (pour raccourcis & barre des tâches)
Source: "{#MyIcon}"; \
  DestDir: "{app}"; \
  Flags: ignoreversion

; ================================================================
[Icons]

; Raccourci dans le menu Démarrer
Name: "{autoprograms}\{#MyAppName}"; \
  Filename: "{app}\{#MyAppExeName}"; \
  IconFilename: "{app}\plage.ico"; \
  Comment: "{#MyAppDescription}"; \
  WorkingDir: "{app}"

; Désinstallation dans le menu Démarrer
Name: "{autoprograms}\{#MyAppName}\{cm:UninstallProgram,{#MyAppName}}"; \
  Filename: "{uninstallexe}"

; Raccourci bureau (si tâche cochée)
Name: "{autodesktop}\{#MyAppName}"; \
  Filename: "{app}\{#MyAppExeName}"; \
  IconFilename: "{app}\plage.ico"; \
  Comment: "{#MyAppDescription}"; \
  WorkingDir: "{app}"; \
  Tasks: desktopicon

; ================================================================
[Registry]

; ── Lancement automatique au démarrage Windows ──────────────────
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Tasks: startup; \
  Flags: uninsdeletevalue

; ── Entrée dans "Programmes installés" (HKCU pour PrivilegesRequired=lowest)
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

; ── Association optionnelle pour les fichiers .coclico (Flow Chains)
Root: HKCU; \
  Subkey: "Software\Classes\.coclico"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "CoclicoFlowChain"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\CoclicoFlowChain"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "Coclico Flow Chain"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\CoclicoFlowChain\DefaultIcon"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: "{app}\{#MyAppExeName},0"; \
  Flags: uninsdeletekey

Root: HKCU; \
  Subkey: "Software\Classes\CoclicoFlowChain\shell\open\command"; \
  ValueType: string; \
  ValueName: ""; \
  ValueData: """{app}\{#MyAppExeName}"" ""%1"""; \
  Flags: uninsdeletekey

; ================================================================
[Run]

; Lancer l'application à la fin de l'installation
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchAfterInstall}"; \
  WorkingDir: "{app}"; \
  Flags: nowait postinstall skipifsilent

; ================================================================
[UninstallRun]

; Fermer proprement l'application avant de désinstaller
Filename: "{cmd}"; \
  Parameters: "/C taskkill /IM ""{#MyAppExeName}"" /F 2>nul"; \
  Flags: runhidden; \
  RunOnceId: "KillCoclico"

; ================================================================
[UninstallDelete]

; Supprimer les données utilisateur
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

; ================================================================
[Code]
// ================================================================
//  SECTION CODE — Pascal Script Inno Setup
//
//  Fonctionnalités :
//   · InitializeSetup()           Vérifie Windows 10 minimum
//   · InitializeWizard()          Page "Prérequis" personnalisée
//   · WriteSettingsJson()         Génère %AppData%\Coclico\settings.json
//   · ApplyStartupRegistry()      Gère la clé Run si tâche cochée
//   · CurStepChanged()            Orchestrateur post-install
//   · CurUninstallStepChanged()   Nettoyage complet à la désinstallation
//   · ShouldSkipPage()            Cache les pages inutiles
// ================================================================

var
  PrereqPage : TWizardPage;
  PrereqMemo : TNewMemo;

// ── Vérification Windows 10 minimum ──────────────────────────────
function InitializeSetup(): Boolean;
var
  WinVer : TWindowsVersion;
begin
  GetWindowsVersionEx(WinVer);
  Result := True;

  if (WinVer.Major < 10) then
  begin
    MsgBox(
      '{#MyAppName} requires Windows 10 or later.' + #13#10 +
      '{#MyAppName} nécessite Windows 10 ou supérieur.',
      mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  // Windows 10 version 2004 (build 19041) minimum pour .NET 10 WPF Mica
  if (WinVer.Major = 10) and (WinVer.Build < 19041) then
  begin
    MsgBox(
      '{#MyAppName} requires Windows 10 version 2004 (build 19041) or later.' + #13#10 +
      'Your current build: ' + IntToStr(WinVer.Build),
      mbError, MB_OK);
    Result := False;
  end;
end;

// ── Page "Prérequis" personnalisée insérée après Bienvenue ────────
procedure InitializeWizard();
var
  PrereqLabel : TNewStaticText;
  ReqText     : String;
begin
  // Créer une page wizard personnalisée
  PrereqPage := CreateCustomPage(
    wpWelcome,
    ExpandConstant('{cm:PrereqTitle}'),
    ExpandConstant('{cm:PrereqDesc}'));

  // Texte des prérequis
  ReqText :=
    '  · Windows 10 (build 19041+) or Windows 11' + #13#10 +
    '  · x64 processor (64-bit)' + #13#10 +
    '  · 200 MB disk space minimum' + #13#10 +
    '  · 4 GB RAM recommended' + #13#10 +
    '' + #13#10 +
    '  · Windows 10 (build 19041+) ou Windows 11' + #13#10 +
    '  · Processeur x64 (64 bits)' + #13#10 +
    '  · 200 Mo d''espace disque minimum' + #13#10 +
    '  · 4 Go RAM recommandés' + #13#10 +
    '' + #13#10 +
    '  Optional / Optionnel :' + #13#10 +
    '  · winget (App Installer) for the built-in installer module' + #13#10 +
    '  · winget pour le module installateur intégré';

  PrereqMemo := TNewMemo.Create(PrereqPage);
  PrereqMemo.Parent     := PrereqPage.Surface;
  PrereqMemo.Left       := 0;
  PrereqMemo.Top        := 0;
  PrereqMemo.Width      := PrereqPage.SurfaceWidth;
  PrereqMemo.Height     := PrereqPage.SurfaceHeight;
  PrereqMemo.ScrollBars := ssVertical;
  PrereqMemo.ReadOnly   := True;
  PrereqMemo.Text       := ReqText;
  PrereqMemo.Font.Size  := 9;
end;

// ── Générer le settings.json initial dans %AppData%\Coclico ──────
procedure WriteSettingsJson();
var
  SettingsDir     : String;
  SettingsFile    : String;
  MinimizeToTray  : Boolean;
  LaunchAtStartup : Boolean;
  Json            : String;
  JsonBuf         : AnsiString;
begin
  SettingsDir     := ExpandConstant('{userappdata}\{#MyAppName}');
  SettingsFile    := SettingsDir + '\settings.json';
  MinimizeToTray  := WizardIsTaskSelected('minimizetray');
  LaunchAtStartup := WizardIsTaskSelected('startup');

  // Créer le dossier si nécessaire
  if not DirExists(SettingsDir) then
    ForceDirectories(SettingsDir);

  // Si settings.json existe déjà → mettre à jour seulement les 2 clés
  if FileExists(SettingsFile) then
  begin
    if LoadStringFromFile(SettingsFile, JsonBuf) then
    begin
      Json := String(JsonBuf);

      // minimizeToTray
      if MinimizeToTray then
      begin
        StringChange(Json, '"minimizeToTray": false', '"minimizeToTray": true');
        if Pos('"minimizeToTray"', Json) = 0 then
          Json := Copy(Json, 1, Length(Json)-1) + ',' + #13#10 + '  "minimizeToTray": true' + #13#10 + '}';
      end;

      // launchAtStartup
      if LaunchAtStartup then
      begin
        StringChange(Json, '"launchAtStartup": false', '"launchAtStartup": true');
        if Pos('"launchAtStartup"', Json) = 0 then
          Json := Copy(Json, 1, Length(Json)-1) + ',' + #13#10 + '  "launchAtStartup": true' + #13#10 + '}';
      end;

      JsonBuf := AnsiString(Json);
      SaveStringToFile(SettingsFile, JsonBuf, False);
      Exit;
    end;
  end;

  // Sinon : créer un settings.json complet depuis zéro
  if MinimizeToTray then
    Json := 'true'
  else
    Json := 'false';

  if LaunchAtStartup then
    Json := Json + #0 + 'true'    // séparateur temporaire
  else
    Json := Json + #0 + 'false';

  Json :=
    '{' + #13#10 +
    '  "language": "fr",' + #13#10 +
    '  "accentColor": "#6366F1",' + #13#10 +
    '  "themePreset": "Indigo",' + #13#10 +
    '  "backgroundMode": "UltraDark",' + #13#10 +
    '  "cardOpacity": 0.07,' + #13#10 +
    '  "fontSize": 13.0,' + #13#10 +
    '  "sidebarWidth": 220,' + #13#10 +
    '  "compactMode": false,' + #13#10 +
    '  "wingetScope": "machine",' + #13#10 +
    '  "launchAtStartup": ' + Copy(Json, Pos(#0, Json)+1, 255) + ',' + #13#10 +
    '  "minimizeToTray": ' + Copy(Json, 1, Pos(#0, Json)-1) + ',' + #13#10 +
    '  "firstRun": true,' + #13#10 +
    '  "launchMode": "Normal"' + #13#10 +
    '}';

  JsonBuf := AnsiString(Json);
  SaveStringToFile(SettingsFile, JsonBuf, False);
end;

// ── Résumé personnalisé dans la page "Prêt à installer" ──────────
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

// ── Orchestrateur après installation ─────────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteSettingsJson();
end;

// ── Nettoyage complet à la désinstallation ────────────────────────
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Supprimer la clé de démarrage automatique
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#MyAppName}');

    // Supprimer l'association de fichiers .coclico
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.coclico');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\CoclicoFlowChain');

    // Supprimer la clé registre de l'éditeur
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\{#MyAppPublisher}\{#MyAppName}');
  end;
end;

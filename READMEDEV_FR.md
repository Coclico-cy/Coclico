<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Icône de Coclico" width="72" />

# Coclico : Documentation développeur

### Architecture, patterns, pipeline de build et CI, expliqués module par module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4?logo=windows11&logoColor=white)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[User guide (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · [Developer (EN)](READMEDEV.md) · **Développeur (FR)**

</div>

---

## Sommaire

1. [Stack technique et projets](#1-stack-technique-et-projets)
2. [Démarrage de l'application](#2-démarrage-de-lapplication)
3. [Organisation du code](#3-organisation-du-code)
4. [Les modules, un par un](#4-les-modules-un-par-un)
5. [L'assistant IA en détail](#5-lassistant-ia-en-détail)
6. [Navigation et menu](#6-navigation-et-menu)
7. [Ajouter un module, pas à pas](#7-ajouter-un-module-pas-à-pas)
8. [Patterns de services](#8-patterns-de-services)
9. [Localisation et thème](#9-localisation-et-thème)
10. [Réglages et données](#10-réglages-et-données)
11. [Tests](#11-tests)
12. [Build, format, CI et release](#12-build-format-ci-et-release)
13. [Le pipeline de l'installeur](#13-le-pipeline-de-linstalleur)
14. [Pratiques de sécurité](#14-pratiques-de-sécurité)
15. [Publier une version](#15-publier-une-version)

---

## 1. Stack technique et projets

| Élément | Détail |
| --- | --- |
| Cible | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Langage | C# (`LangVersion latest`), `Nullable` et `ImplicitUsings` activés |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (contrôles Fluent : `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4.0 (utilisé pour certains ViewModels ; beaucoup de vues restent en code-behind) |
| DI / hébergement | Microsoft.Extensions.DependencyInjection 10 + la façade statique maison `ServiceContainer` |
| Logs | Serilog (fichier quotidien dans `%APPDATA%\Coclico\logs`, rétention 14 jours) |
| IA locale | LLamaSharp 0.26 : backend CPU embarqué ; backend CUDA 12 (`LLamaSharp.Backend.Cuda12` + paquets runtime NVIDIA) uniquement avec `-p:IncludeCuda=true` |
| Divers | System.Management (WMI), System.ServiceProcess.ServiceController, System.Reactive, System.Text.Json, Microsoft.Extensions.Http / Caching.Memory |

Réglages de build notables dans `Coclico/Coclico.csproj` :

- PGO par niveaux + compilation par niveaux, `OptimizationPreference=Speed`, GC concurrent (GC serveur désactivé), pas de trimming.
- PDB portables en Release, conservés volontairement : ils alimentent les rapports d'erreurs de l'application.
- `EnforceCodeStyleInBuild` + `AnalysisLevel=latest` : une violation de style fait échouer le build.
- `SatelliteResourceLanguages=fr;en` : seules les langues satellites français et anglais sont livrées.
- `EnableWindowsTargeting` : la CI peut compiler la cible Windows depuis un agent non-Windows.
- `InternalsVisibleTo("Coclico.Tests")` : le projet de tests peut atteindre les membres `internal`.
- L'icône de l'application est `Resources/icone/plage.ico` ; la version vit dans `version.txt` à la racine du dépôt et remonte jusqu'au build (cf. §13 et §15).

La solution (`Coclico.slnx`, également reflétée en `Coclico.sln`) contient **deux projets** :

```text
Coclico/         application WPF (l'app principale)
Coclico.Tests/   tests xunit (référence Coclico via InternalsVisibleTo)
```

`Coclico.Installer/` ne fait **pas** partie de la solution : c'est un projet WPF autonome compilé uniquement par `installer/build-installer.ps1` (cf. §13).

## 2. Démarrage de l'application

`App.xaml.cs` (classe `Coclico.App`) :

1. **Gestion globale des erreurs** : branchements sur `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` et `DispatcherUnhandledException` → `LoggingService` + `IAppErrorManager`.
2. `ServiceContainer.Build(services => ...)` : construit le `IServiceProvider` (paresseux, `ValidateOnBuild`). Serilog y est configuré, avec les services de base (`IAuditLog`, `ISecurityPolicy`, `IProcessExecutionService`, cache mémoire, `HttpClient`), puis les services de chaque module.
3. L'application démarre **en utilisateur standard** (pas d'invite UAC au lancement) ; l'élévation se fait à la demande via `App.RestartAsAdmin()`. Vérifie l'état avec `App.IsRunningAsAdministrator()`.
4. Les réglages sont chargés depuis `%APPDATA%\Coclico\settings.json`, puis `LocalizationService.SetLanguage`, `ThemeService.ApplyCurrentSettings`, puis `SplashWindow` → `MainWindow`.

```text
App.OnStartup
  ├── Hooks d'exceptions globaux (AppDomain / TaskScheduler / Dispatcher)
  ├── ServiceContainer.Build(...)          ← Serilog + base + services des modules
  ├── RemoveLegacyAiCredentialVault()
  ├── SettingsService : charge %APPDATA%\Coclico\settings.json
  ├── LocalizationService.SetLanguage(...)
  ├── ThemeService.ApplyCurrentSettings()
  └── SplashWindow → MainWindow
```

`ServiceContainer` est une façade statique : `ServiceContainer.GetRequired<T>()`, `GetOptional<T>()`. Les vues (code-behind) résolvent leurs services ainsi.

## 3. Organisation du code

```text
Coclico/
  Common/
    Core/          SettingsService, CacheService, StartupService, ServiceContainer,
                   UpdateCheckService, UpdateManager, LaunchMode, SimpleStemmer
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService
    Execution/     IProcessExecutionService (exécution de processus filtrée par ISecurityPolicy),
                   ProcessExecutionService, ProcessWatcherService, ResourceAllocatorService,
                   ResourceGuardService
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   TrayService.Win32, KeyboardShortcutsService
  Converters/      FirstCharConverter, InverseBoolToVisibilityConverter, HexToBrushConverter,
                   EqualityToBoolConverter, FileIconConverter
  Modules/         Un dossier par module (cf. §4)
  Resources/
    Lang/          fr.xaml + en.xaml (dictionnaires de chaînes)
    icone/         icônes de l'application
  App.xaml         Styles globaux (brosses, CornerCard/CornerBtn, styles de boutons)
  MainWindow.*     Fenêtre principale : barre supérieure, tiroir de navigation, panneaux
```

Anatomie d'un module (`Modules/<Nom>/`) :

- `Services/` : la logique métier, gardée **pure et testable** autant que possible (sans dépendance WPF).
- `Views/` : `XxxView.xaml` + code-behind. Certaines vues utilisent un dossier `ViewModels/` + CommunityToolkit à la place.
- `Windows/`, `Models/` : au besoin (fenêtres modales, modèles de données).

Racine du dépôt :

| Chemin | Rôle |
| --- | --- |
| `version.txt` | Le numéro de version unique, lu par le build et le script d'installeur |
| `Directory.Build.props` | Propriétés de build partagées par tous les projets |
| `LICENSE.txt` | Licence MIT |
| `SECURITY.md` | Comment signaler un problème de sécurité |
| `CONTRIBUTORS.md` | Crédits |
| `README.md` / `README_FR.md` | Guides utilisateur (EN / FR) |
| `READMEDEV.md` / `READMEDEV_FR.md` | Cette documentation développeur (EN / FR) |
| `docs/` | Captures d'écran utilisées par les guides utilisateur |
| `installer/build-installer.ps1` | Le script de build de l'installeur (§13) |
| `.github/workflows/` | CI, release et automatisation du dépôt (§12) |

## 4. Les modules, un par un

`Modules/` contient un dossier par fonctionnalité. Tous suivent la forme décrite en §3 : la logique dans les services, l'UI dans les vues. La visite complète :

### Tableau de bord (`Modules/Dashboard`)

La page d'accueil personnalisable. `HomeCustomizationService` + `HomeCustomizationModel` retiennent quelles tuiles sont visibles ; `CustomShortcutsService` ajoute tes propres raccourcis ; `HomeCustomizerWindow` est la fenêtre de personnalisation. Les tuiles passent par le même chemin public de navigation que le menu (§6), et le premier lancement affiche le message de bienvenue.

### Assistant IA (`Modules/AI`)

L'assistant intégré, entièrement local — le panneau de discussion ouvert avec `Ctrl+Space`. Détail en §5.

### Nettoyage (`Modules/Cleaning`)

`CleaningService` (+ ViewModel et vue) liste toutes les cibles — fichiers temporaires, caches… — et montre ce qui sera supprimé **avant** que quoi que ce soit n'arrive. Les suppressions vont à la **Corbeille**, et le résultat indique l'espace libéré et les éléments ignorés. Rien ne s'exécute sans une confirmation explicite.

### Santé & Défense (`Modules/HealthDefense`)

- `ISecurityPolicy` / `SecurityPolicyService` : la politique de commandes centrale. `IProcessExecutionService` la consulte avant d'exécuter quoi que ce soit (§8), ainsi les commandes interdites sont bloquées en un seul endroit, couvert par `SecurityPolicyTests`.
- `SystemHealthService` : contrôles de l'état du système.
- `StartupHealthService` : examine ce qui se lance au démarrage de Windows.

### Analyseur d'espace disque (`Modules/DiskAnalyzer`)

- `DiskAnalyzerService` : scanne un disque, construit l'arborescence, calcule la répartition par zones et les plus gros fichiers.
- `DuplicateFinderService` : le scan de doublons en trois phases (index par taille → groupes candidats → affinage par hash) est entièrement statique et testé unitairement ; les liens physiques NTFS sont dédoublonnés par identité de fichier, pour qu'un fichier lié ne soit jamais signalé deux fois. `DuplicateScanOptions` contrôle les seuils et le nombre de groupes affichés.
- `RecycleBinHelper` : suppression sûre via `SHFileOperation` avec `FOF_ALLOWUNDO`.
- La vue propose une barre de modes segmentée (analyse / doublons), des cartes de disques avec pourcentage d'utilisation et sélection, un panneau de zones, et une liste de doublons avec « ouvrir l'emplacement » et suppression vers la Corbeille fichier par fichier.

### Nettoyage RAM (`Modules/RamCleaner`)

- `MemoryCleanerService` : nettoyage du working set à la demande, globalement ou par processus.
- `SmartMemoryDaemonService` (+ `ISmartMemoryDaemonService`) : le démon en arrière-plan et ses profils (dont Deep) — état protégé par un lock, boucle dans un `Task.Run` avec jeton d'annulation, événements `StatusChanged`, réglages persistés dans `AppSettings`, arrêt propre au déchargement de la vue.
- La vue affiche en direct la RAM/le CPU et les processus les plus gourmands.

### Réseau (`Modules/Network`)

Le plus gros module. Interfaces (`INetworkBenchmarkService`, `INetworkDiagnosticsService`, `INetworkDiscoveryService`, `INetworkExperimentEngine`, `INetworkMemoryService`, `INetworkOptimizerService`, `INetworkSafetyService`, `INetworkScoringService`, `INetworkSnapshotService`, `INetworkWatchdogService`), modèles (`NetworkModels`, `NetworkEngineModels`) et leurs implémentations : `NetworkBenchmarkService`, `NetworkDiagnosticsService`, `NetworkDiscoveryService`, `NetworkExperimentEngine`, `NetworkMemoryService`, `NetworkMonitorService`, `NetworkOptimizerService`, `NetworkSafetyService`, `NetworkScoringService`, `NetworkSnapshotService`, `NetworkWatchdogService`, plus `NetworkOptimizerViewModel` / `NetworkOptimizerView` et la fenêtre `NetworkResultsWindow`.

Conception sécurité : un instantané (snapshot) est pris **avant** toute modification et un retour arrière en un clic le restaure ; `INetworkSafetyService` valide chaque ajustement ; des préréglages appliquent des configurations éprouvées ; le watchdog surveille la connexion. Aucune écriture directe dans le registre.

### CPU & Alimentation (`Modules/Power`)

- `PowerPlanCatalog` : définitions des profils et arguments `powercfg` correspondants (testé unitairement).
- `CpuPowerService` : enrobeur de `powercfg` réservé aux administrateurs. Chaque réglage est validé par `powercfg /q` avant d'être appliqué, les réglages non supportés sont ignorés et signalés ; les profils créés par Coclico sont toujours supprimables, et l'application revient d'abord au profil Équilibré. Aucune écriture directe dans le registre.
- `CpuPowerBenchmark` : benchmark de charge CPU.
- `AdaptivePowerService` : le démon adaptatif — il change de profil d'alimentation selon la charge CPU, avec seuils, hystérésis et période de repos. La règle vit dans `AdaptivePowerRules`, pur et testé sans le démon.

### Programmes (`Modules/Programs`)

`InstalledProgramsService` énumère les programmes installés (par utilisateur et par machine) ; `UninstallCommandParser` transforme les chaînes de désinstallation brutes en exécutable + arguments sûrs (testé unitairement) ; la vue permet d'ouvrir, localiser ou désinstaller chaque entrée.

### Installeur — mises à jour intégrées (`Modules/Installer`)

- `UpdateCheckService` (singleton) interroge la dernière release GitHub au démarrage, puis environ toutes les 6 heures avec un petit décalage aléatoire. L'événement `UpdateAvailable` est consommé par `MainWindow`, qui met le bouton de synchronisation en évidence et ouvre `UpdateWindow` (versions, notes, progression). « Ignorer cette version » est persisté dans `AppSettings.SkippedUpdateVersion` ; la comparaison de versions (`IsNewerVersion`) est numérique et gère les pré-versions.
- `UpdateManager` : `DownloadReleaseAsync` (HTTPS et domaines GitHub uniquement, SHA-256 obligatoire, progression en Mo) puis `LaunchInstaller(path, expectedTag)`, qui revérifie le hash **et** la version du fichier (`FileVersionInfo`, sans l'exécuter) avant l'élévation `runas`.
- `InstallerService` câble le module intégré. Il est distinct du projet autonome `Coclico.Installer` (§13).

### Réglages (`Modules/Settings`)

`SettingsView` (+ ViewModel), `ProfileService` (profils utilisateur), `UserAccountService` (comptes et avatars personnalisés), et les fenêtres associées : `SplashWindow`, `ProfileWindow` (avec `AvatarCropWindow` pour recadrer un avatar personnalisé) et `ConfirmationDialog`.

### Aide (`Modules/Help`)

`HelpView` : la page d'aide intégrée.

## 5. L'assistant IA en détail

Tout tourne **en local**. Aucun appel cloud et aucune clé d'API nulle part dans le code ; `AiLocalOnlyTests` le garantit (aucun fournisseur cloud ne peut apparaître, et Ollama n'est jamais contacté ailleurs que sur loopback).

- **Fournisseurs.** `MultiProviderClient` (avec `AiProviderContracts`) dialogue avec les backends de modèles : fichiers GGUF locaux via LLamaSharp, ou serveur Ollama local. `AiHardwareService` détecte le GPU/la VRAM et décide du déchargement des couches ; quand le backend CUDA n'est pas disponible, LLamaSharp bascule automatiquement sur le backend CPU embarqué.
- **Discussion.** `AiChatService` construit le prompt système compact, garde la mémoire de conversation avec élagage, gère les tokens d'arrêt, et répond de façon déterministe aux questions « à quoi sert l'application ? ». L'assistant sait toujours où tu es : la navigation met à jour `IAiService.CurrentStatusContext` (§6). L'UI vit dans `AiChatView` (+ la partielle `AiChatView.Actions.cs`).
- **Connaissance (RAG).** `RagService` fait de la recherche BM25/TF-IDF sur la base de connaissance embarquée (`resource/docs/coclico.md`), qui décrit chaque module, action et règle de sécurité ; `SimpleStemmer` gère le français et l'anglais (stop words, suffixes, pluriels).
- **Actions.** `AiActionParser` analyse la sortie structurée du modèle en actions (naviguer vers un module, nettoyer la RAM, optimiser le réseau…). `AiToolExecutionService` les exécute avec une sanitisation stricte — cibles de navigation sur liste autorisée, identifiants de paquets validés, bornes numériques — et exige une confirmation explicite de l'utilisateur avant tout ce qui modifie le système.
- **Vision.** `AiVisionService` gère la compréhension d'images avec les mêmes modèles locaux.

## 6. Navigation et menu

- Le menu principal est le **tiroir hamburger** (`ArchitecturalDropdownOverlay` dans `MainWindow.xaml`). C'est un accordéon groupé : Tableau de bord, puis les groupes repliables **MAINTENANCE** (Nettoyage, Santé & Défense, Disque), **PERFORMANCE** (RAM, Réseau, CPU & Alimentation) et **APPLICATIONS** (Programmes, Installeur), puis Réglages et Aide, toujours visibles. L'état déplié/replié est persisté dans `AppSettings.MenuExpandedGroups`.
- Chaque carte porte un `Tag` (l'identifiant du module). Un clic passe par `MainWindow.NavigateTo(tag)` → `NavigateToTagAsync(tag)` qui :
  1. résout ou crée la vue (mise en cache via un champ `_xxxView ??= new XxxView()`) ;
  2. met à jour `IAiService.CurrentStatusContext` (le contexte de l'assistant IA) ;
  3. joue la transition et met à jour l'indicateur actif.
- Les tuiles du tableau de bord et les actions IA (`open_cleaning`, `open_power`…) passent par ce même chemin public ; ne déclenche jamais manuellement un événement `Click` sur un `RadioButton`.
- Raccourcis globaux : `KeyboardShortcutsService` (IA, nettoyage RAM…), plus `F11` (plein écran) et `Ctrl+Space` (panneau IA) gérés dans `MainWindow`.

## 7. Ajouter un module, pas à pas

Exemple concret : le module CPU & Alimentation (`Modules/Power`). Étapes :

1. **Créer le module** : `Modules/<Nom>/Services/*.cs` (logique) et `Modules/<Nom>/Views/<Nom>View.xaml(.cs)`.
2. **Enregistrer les services** dans `App.xaml.cs` (`services.AddSingleton<...>()`, ou `AddTransient` pour les services à durée de vie courte).
3. **Câbler la navigation** dans `MainWindow.xaml.cs` :
   - un champ privé `_xxxView` ;
   - un cas `"Tag" => _xxxView ??= ...` dans le switch de `NavigateToTagAsync` ;
   - la phrase de contexte IA correspondante ;
   - (optionnel) un cas `open_xxx` dans `AiChatPanel_ActionRequested`.
4. **Ajouter la carte** au tiroir (`MainWindow.xaml`) : un `Border` avec `Tag="<Tag>"` et `MouseDown="DropdownItem_MouseDown"`, dans le bon groupe. Icônes : uniquement des symboles `Wpf.Ui.Controls.SymbolRegular` existants.
5. **Clés de langue** : ajouter chaque clé dans `Resources/Lang/fr.xaml` **et** `en.xaml` (les deux dictionnaires doivent rester synchrones). Les vues de modules peuvent aussi embarquer des chaînes françaises en dur (comme DiskAnalyzer) ; reste cohérent par module.
6. **Tests** : la logique pure dans `Coclico.Tests` ; pour une vue simple, un test d'instanciation STA évite une `XamlParseException` au premier clic (cf. `DiskAnalyzerTests.DiskAnalyzerView_InstantiatesWithoutXamlErrors`). Note : un test d'instanciation exige une vue sans dépendance DI obligatoire dans son constructeur.
7. **Vérifier** : `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` (le nouveau code doit être propre).

## 8. Patterns de services

**Service pur et testable.** Sépare l'algorithme de tout ce qui touche au système. Exemple `DuplicateFinderService` : les étapes de groupage (`IndexBySize`, `CandidateGroups`, `RefineByHash`, `ToDuplicateGroups`) sont statiques et testables ; le scan complet n'est qu'un `Task.Run` autour. Même approche dans `PowerPlanCatalog` (données + construction des arguments powercfg) et `AdaptivePowerRules.Evaluate` (règle pure, testée sans le démon).

**Exécution de processus.**

- Chemin standard : `IProcessExecutionService.ExecuteAsync`, filtré par `ISecurityPolicy.IsCommandBlocked`, timeout, annulation, encodages configurés. `requiresElevation: true` met `Verb = "runas"` → **une invite UAC par appel**.
- Pour un outil réservé admin exécuté en série (ex. `powercfg` dans `CpuPowerService`), n'utilise **pas** l'élévation par commande : appelle `powercfg.exe` directement (le pattern `ProcessStartInfo` de `NetworkOptimizerService`) et **conditionne l'UI** à `App.IsRunningAsAdministrator()`. L'utilisateur élève l'application une seule fois ; pas de tempête d'UAC.

**Interop natif.** Convention du dépôt : `DllImport` classique (pas `LibraryImport`, qui exigerait `AllowUnsafeBlocks`). Exemples : `GetSystemPowerStatus` (batterie/secteur, `CpuPowerService`), `GetSystemTimes` (charge CPU, `AdaptivePowerService`), `SHFileOperation` (Corbeille, `RecycleBinHelper`). Toujours envelopper dans une petite classe interne avec une méthode publique tolérante aux échecs.

**Démons.** Deux références : `SmartMemoryDaemonService` (nettoyage mémoire) et `AdaptivePowerService` (changement de profil selon la charge CPU). Points communs : état protégé par un `lock`, boucle dans un `Task.Run` avec `CancellationTokenSource`, événements type `StatusChanged`, persistance dans `AppSettings`, arrêt propre au `Unloaded` des vues. Les événements levés sur un thread d'arrière-plan doivent être marshalés avec `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**Mesures système.** Préférer les API Win32 (`GetSystemTimes`) aux compteurs de performance : pas de dépendance, pas de souci de localisation, calcul de delta trivial.

## 9. Localisation et thème

- Chaînes UI : `Resources/Lang/fr.xaml` et `en.xaml`, consommées en `{DynamicResource MyKey}`. `LocalizationService` remplace le dictionnaire fusionné au changement de langue ; `LocalizationService.Get(key)` est le repli du code-behind.
- **Règle absolue : chaque clé ajoutée doit exister dans les deux fichiers.**
- Ressources XAML : `XamlResourceValidationTests` vérifie statiquement que chaque `{StaticResource}` (ex. `CornerCard`, `CornerBtn`) et chaque dictionnaire fusionné se résout, sinon le build de tests échoue. Les clés `{DynamicResource}` manquantes échouent silencieusement au chargement, d'où l'importance de vérifier les clés.
- Brosses globales dans `App.xaml` : `BgCardBrush`, `BgSurfaceBrush`, `BgElevatedBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `ArchitecturalYellowBrush`, `Text*Brush`… `ThemeService` applique accent/thème depuis les réglages.
- `FindResource` **lève une exception** si absent ; en code-behind, préfère `TryFindResource` ou un cast `as Brush` avec repli.

## 10. Réglages et données

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (JSON camelCase via `[JsonPropertyName]`, valeurs par défaut sur les propriétés). Ajouter une propriété = la déclarer avec un défaut ; tout le reste (chargement, sauvegarde, migration douce des champs manquants) est géré par `SettingsService`.
- Autres données : logs Serilog (`logs/`), piste d'audit (`IAuditLog`, rétention configurable), instantanés réseau (module Réseau), profils utilisateur (`ProfileService`).

## 11. Tests

`Coclico.Tests` (xunit) : tout doit passer avant toute fusion. Couverture actuelle :

| Fichier | Ce qu'il couvre |
| --- | --- |
| `Common/Core/UpdateVersionTests.cs` | Comparaison de versions numérique, pré-versions incluses (`IsNewerVersion`) |
| `Modules/Ai/AiLocalOnlyTests.cs` | Garde-fous « IA locale uniquement » (aucune clé cloud, Ollama en loopback) |
| `Modules/Ai/RagServiceTests.cs` | Recherche sur la base de connaissance |
| `Modules/DiskAnalyzer/DiskAnalyzerTests.cs` | Scan d'arborescence, `FormatSize`, instanciation STA de la vue (attrape les `XamlParseException`) |
| `Modules/DiskAnalyzer/DuplicateFinderTests.cs` | Doublons en 3 phases, seuil de taille, top-N plus gros fichiers, groupage et tri |
| `Modules/HealthDefense/SecurityPolicyTests.cs` | Politique de commandes : les commandes interdites restent bloquées |
| `Modules/Power/PowerPlanCatalogTests.cs` | Noms des profils, arguments powercfg, bornes des valeurs |
| `Modules/Power/AdaptivePowerRulesTests.cs` | Règle adaptative (seuils, hystérésis, repos), fenêtre glissante |
| `Modules/Programs/UninstallCommandParserTests.cs` | Analyse des chaînes de désinstallation (guillemets, arguments, cas limites) |
| `Ui/Xaml/XamlResourceValidationTests.cs` | Résolution des `StaticResource`, dictionnaires fusionnés et chemins d'images dans Coclico et Coclico.Installer |

`InternalsVisibleTo("Coclico.Tests")` permet aux tests d'atteindre les méthodes `internal` des services.

## 12. Build, format, CI et release

Commandes locales (identiques à la CI) :

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Règle de format : tout nouveau code doit passer `dotnet format --verify-no-changes`. Le dépôt est propre au format ; si une vérification locale échoue, lance `dotnet format Coclico.slnx` (sans `--verify`) et commite le résultat dans une PR dédiée pour garder un diff relisible.

Workflows dans `.github/workflows/` :

- **`ci.yml`** — restore → vérification de format → build Release → tests (artefact TRX) → sur les pushes vers `main`, un publish win-x64 est téléversé en artefact.
- **`release.yml`** — déclenché par un tag `vX.Y.Z` : vérifie que le tag correspond à la version (`version.txt`), format + tests, publish, puis `./installer/build-installer.ps1 -SkipPublish`, ZIP portable + `SHA256SUMS.txt` (setup + zip), et la release GitHub avec les trois fichiers.
- **`codeql.yml`** — analyse de sécurité CodeQL.
- **`dependency-review.yml`** — revue des dépendances des changements de paquets.
- **`scorecard.yml`** — OpenSSF Scorecard.
- **`labeler.yml`, `greetings.yml`, `stale.yml`** — automatisation du dépôt (labels d'issues, message de bienvenue, gestion des tickets oubliés).

## 13. Le pipeline de l'installeur

Ce que fait `installer/build-installer.ps1`, étape par étape (console interactive, FR/EN) :

1. **Version** : lue depuis `version.txt` à la racine du dépôt (ou passée via `-Version`).
2. **Tests d'abord** : `dotnet test` s'exécute avant tout packaging (ignorer avec `-SkipTests`) — l'installeur n'est jamais construit sur une suite rouge.
3. **Nettoyage** : `publish/` et `artifacts/installer_build` sont nettoyés (ignorer avec `-SkipClean`).
4. **Publication de Coclico** : `dotnet publish` win-x64, **dépendant du framework** (`--self-contained false`), pas de fichier unique, pas de PDB → `publish/`. Le runtime .NET 10 Desktop est géré par l'installeur au moment de l'installation (ci-dessous).
5. **Payload** : `publish/` est zippé en `payload.zip` dans `Coclico.Installer/` (compression optimale). Sont exclus : les `.pdb` et, sauf `-IncludeCuda`, les fichiers runtime CUDA (`ggml-cuda.dll`, `cudart64_12.dll`, `cublas…`). Sans les DLL CUDA, LLamaSharp utilise proprement le backend CPU embarqué ; les utilisateurs NVIDIA récupèrent le runtime CUDA en téléchargement lors de l'installation — c'est ainsi que l'installeur reste léger.
6. **Installeur final** : `Coclico.Installer` est publié en SingleFile autonome compressé avec `payload.zip` embarqué → `artifacts/installer/Coclico-<version>.exe` (variante `-Full-CUDA` : `Coclico-<version>-Full-CUDA.exe`), avec son SHA-256 affiché.
7. `-SkipPublish` réutilise le dossier `publish/` existant (c'est ce que fait la CI de release après son propre publish).

Au moment de l'installation, le projet autonome `Coclico.Installer` prend le relais (`Services/`) :

- `SystemRequirementService` — configuration requise et disque cible déduits du chemin choisi.
- `GpuDetector` — détection de GPU NVIDIA (ne lève jamais).
- `DotnetRuntimeService` — vérifie le runtime .NET 10 Desktop ; s'il manque, télécharge l'installeur Microsoft épinglé et l'exécute en silence (`/install /passive /norestart /log`, avec reprises si Windows Installer est occupé).
- `CudaRuntimeDownloader` + `AiModelDownloader` — téléchargement optionnel du runtime CUDA et d'un modèle d'IA local (catalogue dans `ai-models.json`, tenant compte de la taille, reprenable).
- `InstallService` — installe pour le compte utilisateur courant (défaut `%LOCALAPPDATA%\Programs\Coclico`), crée les raccourcis (`ShortcutHelper`), enregistre l'entrée de désinstallation (via l'installeur lui-même, pas d'écritures registre dispersées), déploie le désinstalleur dans `Uninstall\Coclico.Installer.exe`, ferme d'abord un Coclico en cours d'exécution, et conserve les réglages et données à la désinstallation.
- `InstallerLocalization` — chaînes FR/EN de l'installeur ; `UninstallWindow` propose de conserver les données utilisateur lors de la désinstallation.

## 14. Pratiques de sécurité

- **Confirmations** avant toute action destructive ; **Corbeille** (`SHFileOperation` + `FOF_ALLOWUNDO`) plutôt que suppression définitive pour les fichiers de l'utilisateur.
- **Instantanés réseau** avant toute modification + retour arrière en un clic ; un service de sécurité valide chaque ajustement.
- **Filtrage de commandes** : passer par `IProcessExecutionService` autant que possible (`ISecurityPolicy` bloque les commandes interdites, couvert par `SecurityPolicyTests`).
- **Module Alimentation** : uniquement `powercfg` (aucune écriture registre directe), chaque réglage validé par `powercfg /q` avant application (les réglages non supportés sont ignorés et signalés), profils Coclico supprimables à tout moment, retour au profil Équilibré d'abord.
- **IA locale uniquement** : aucun coffre de clés cloud ; Ollama restreint au loopback ; garde-fous testés par `AiLocalOnlyTests`. Les actions IA sont sanitizées et exigent la confirmation de l'utilisateur.
- **Mises à jour** : HTTPS et domaines GitHub uniquement, SHA-256 obligatoire vérifié au téléchargement **et** de nouveau avant le lancement, version du fichier contrôlée sans l'exécuter ; les releases embarquent `SHA256SUMS.txt`.
- **Élévation à la demande** : l'application démarre en utilisateur standard ; les modules concernés affichent leur état et renvoient vers le bouton d'élévation existant.
- **Logs** : jamais de données personnelles ; `LoggingService.LogException` dans chaque `catch`.

## 15. Publier une version

1. Mettre à jour `version.txt` à la racine du dépôt — l'unique source de la version, remontant jusqu'au build et au script d'installeur.
2. Mettre à jour la version affichée dans la barre d'état de `MainWindow.xaml` si besoin.
3. Fusionner vers `main` (la CI exécute : format, build, tests).
4. Créer le tag : `git tag vX.Y.Z && git push origin vX.Y.Z`. La release (setup + ZIP portable + SHA256SUMS.txt) est générée automatiquement, à condition que le tag corresponde à la version.
5. Vérifier la release GitHub : les trois fichiers attendus, et le SHA-256 affiché par le script d'installeur local correspond à `SHA256SUMS.txt`.

---

<p align="center"><i>Retour : <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="README.md">user guide (EN)</a> · <a href="READMEDEV.md">developer documentation (EN)</a></i></p>

<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Icône de Coclico" width="72" />

# Coclico : Documentation développeur

### Architecture, patterns, pipeline de build et CI, expliqués module par module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Plateforme](https://img.shields.io/badge/plateforme-Windows%2011-0078D4?logo=windows11&logoColor=white)](#1-stack-technique-et-projets)
[![Licence : MIT](https://img.shields.io/badge/licence-MIT-green.svg)](LICENSE.txt)

[Guide utilisateur (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · [Developer (EN)](READMEDEV.md) · **Développeur (FR)**

</div>

---

## Sommaire

1. [Stack technique et projets](#1-stack-technique-et-projets)
2. [Démarrage de l'application](#2-démarrage-de-lapplication)
3. [La CentralUnit](#3-la-centralunit)
4. [Organisation du code](#4-organisation-du-code)
5. [Les modules, un par un](#5-les-modules-un-par-un)
6. [Galerie d'écrans](#6-galerie-décrans)
7. [L'assistant IA en détail](#7-lassistant-ia-en-détail)
8. [Navigation et menu](#8-navigation-et-menu)
9. [Ajouter un module, pas à pas](#9-ajouter-un-module-pas-à-pas)
10. [Patterns de services](#10-patterns-de-services)
11. [Localisation et thème](#11-localisation-et-thème)
12. [Réglages et données](#12-réglages-et-données)
13. [Tests](#13-tests)
14. [Build, format, CI et release](#14-build-format-ci-et-release)
15. [Le pipeline de l'installeur](#15-le-pipeline-de-linstalleur)
16. [Pratiques de sécurité](#16-pratiques-de-sécurité)
17. [Publier une version](#17-publier-une-version)

---

## 1. Stack technique et projets

| Élément | Détail |
| --- | --- |
| Cible | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Langage | C# (`LangVersion latest`), `Nullable` et `ImplicitUsings` activés |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (contrôles Fluent : `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4.0 (certains ViewModels ; beaucoup de vues restent en code-behind) |
| DI | Microsoft.Extensions.DependencyInjection 10 + façade statique maison `ServiceContainer` |
| Logs | Serilog (fichier quotidien dans `%APPDATA%\Coclico\logs`, rétention 14 jours) |
| IA locale | LLamaSharp 0.26 : backend CPU embarqué ; backend CUDA 12 uniquement avec `-p:IncludeCuda=true` |
| Divers | System.Management (WMI), System.Reactive, System.Text.Json, Microsoft.Extensions.Http / Caching.Memory |

Réglages de build notables dans `Coclico/Coclico.csproj` :

- PGO par niveaux + compilation par niveaux, `OptimizationPreference=Speed`, GC concurrent, pas de trimming.
- PDB portables en Release, conservés volontairement : ils alimentent les rapports d'erreurs.
- `EnforceCodeStyleInBuild` + `AnalysisLevel=latest` : une violation de style fait échouer le build.
- `SatelliteResourceLanguages=fr;en` : seules les langues satellites français et anglais sont livrées.
- `EnableWindowsTargeting` : la CI peut compiler la cible Windows depuis un agent non-Windows.
- `InternalsVisibleTo("Coclico.Tests")` : les tests atteignent les membres `internal`.

La solution (`Coclico.slnx`, aussi mirroir en `Coclico.sln`) contient **deux projets** :

```text
Coclico/         Application WPF (l'application principale)
Coclico.Tests/   Tests xunit (référence Coclico via InternalsVisibleTo)
```

`Coclico.Installer/` **n'est pas** dans la solution : c'est un projet WPF autonome, compilé uniquement par `installer/build-installer.ps1` (cf. §15).

## 2. Démarrage de l'application

`App.xaml.cs` (classe `Coclico.App`), dans l'ordre :

1. **Gestion globale des erreurs** : hooks sur `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` et `DispatcherUnhandledException` → `LoggingService` + `IAppErrorManager`.
2. `ServiceContainer.Build(services => ...)` : construit le `IServiceProvider` (lazy, `ValidateOnBuild`). Serilog, les services de base (`IAuditLog`, `ISecurityPolicy`, `IProcessExecutionService`, cache, `HttpClient`), puis les services de chaque module.
3. L'application démarre **en utilisateur standard** (pas d'UAC au lancement). L'élévation se fait à la demande. Vérifie avec `App.IsRunningAsAdministrator()`.
4. Chargement des réglages depuis `%APPDATA%\Coclico\settings.json`, puis `LocalizationService.SetLanguage`, `ThemeService.ApplyCurrentSettings`, puis `SplashWindow` → `MainWindow`.

```text
App.OnStartup
  ├── Hooks d'exceptions globaux (AppDomain / TaskScheduler / Dispatcher)
  ├── ServiceContainer.Build(...)          ← Serilog + base + modules
  ├── SettingsService : lecture settings.json
  ├── LocalizationService.SetLanguage(...)
  ├── ThemeService.ApplyCurrentSettings()
  ├── CentralUnitService.Current.Initialize()
  └── SplashWindow → MainWindow
```

`ServiceContainer` est une façade statique : `ServiceContainer.GetRequired<T>()`, `GetOptional<T>()`. Les vues résolvent leurs services ainsi.

## 3. La CentralUnit

`Common/Core/CentralUnit/` est le noyau maison de l'application. Un singleton, `CentralUnitService.Current`, orchestre la stabilité de l'app. Neuf briques :

| Brique | Rôle |
| --- | --- |
| `CentralUnitService` | Le chef d'orchestre. Boucle de fond, politique CPU/RAM, boost |
| `CentralUnitActivity` | Niveau d'activité global (démarrage, idle, actif…) |
| `CentralUnitFlagStore` | Planificateur de drapeaux internes (`CentralFlags`, réveils programmés) |
| `CentralUnitVariables` | Bus de variables partagées (publish/subscribe) |
| `CentralUnitBus` | Bus d'événements inter-composants |
| `CentralUnitStigmergy` | Scores d'usage par module (statistiques persistées) |
| `CentralCircuitBreaker` | Disjoncteur : un sous-système qui échoue 3 fois se coupe 30 s |
| `CentralErrorTriage` | Tri et regroupement des erreurs récurrentes |
| `CentralUnitWatchdog` | Chien de garde de la boucle de fond |

Le cœur : la **boucle de pression**. Elle échantillonne CPU et RAM toutes les 20 s (45 s sous pression). Si le CPU dépasse 85 % ou la RAM 92 % pendant 3 échantillons, l'app passe en mode « pression externe » :

- priorité mémoire et power throttling ajustés sur ses propres processus ;
- déchargement du modèle IA après 30 s (`CentralFlags.AiModelUnload`) ;
- la sortie de pression exige 2 échantillons sous les seuils (hystérésis : entrée 85 % / sortie 60 %).

Tout est protégé par un `lock` d'état et annulable par `CancellationTokenSource`. Chaque brique a ses tests unitaires (cf. §13).

## 4. Organisation du code

```text
Coclico/
  Common/
    Core/          SettingsService, CacheService, StartupService, ServiceContainer,
                   UpdateCheckService, UpdateManager, SimpleStemmer,
                   CentralUnit/ (9 fichiers, cf. §3)
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService,
                   AsyncEventSafe (exécution d'événements asynchrones sans crash)
    Execution/     IProcessExecutionService (exécution filtrée par ISecurityPolicy),
                   ProcessExecutionService, ProcessWatcherService,
                   ResourceAllocatorService, ResourceGuardService
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   NotificationService, KeyboardShortcutsService, TrayService.Win32
  Modules/         Un dossier par module (cf. §5)
  Resources/
    Lang/          fr.xaml + en.xaml (dictionnaires de chaînes)
    docs/          coclico.md (base de connaissances du RAG de l'IA)
    icone/         icônes de l'application
  App.xaml         Styles globaux (brushes, CornerCard/CornerBtn, styles de boutons)
  MainWindow.*      Fenêtre principale : barre haute, tiroir de navigation, panneaux
```

Anatomie d'un module (`Modules/<Name>/`) :

- `Services/` : la logique métier, pure et testable autant que possible (sans dépendance WPF).
- `Views/` : `XxxView.xaml` + code-behind. Beaucoup de modules ajoutent un `ViewModels/`.
- `Windows/`, `Models/` : au besoin (fenêtres modales, modèles de données).

Racine du dépôt :

| Chemin | Rôle |
| --- | --- |
| `version.txt` | Le numéro de version unique, lu par le build et le script d'installeur |
| `Directory.Build.props` | Propriétés de build partagées |
| `LICENSE.txt` / `SECURITY.md` / `CONTRIBUTORS.md` | Licence, signalement sécurité, crédits |
| `README.md` / `README_FR.md` | Guides utilisateur (EN / FR) |
| `READMEDEV.md` / `READMEDEV_FR.md` | Cette documentation (EN / FR) |
| `docs/` | Captures d'écran des README |
| `installer/build-installer.ps1` | Le script d'installeur (§15) |
| `.github/workflows/` | CI, release, automatisation (§14) |

## 5. Les modules, un par un

`Modules/` contient onze dossiers, un par fonctionnalité. Tous suivent la forme du §4 : la logique dans les services, l'UI dans les vues.

### Accueil (`Modules/Dashboard`)

La page d'accueil personnalisable. `HomeCustomizationService` + `HomeCustomizationModel` retiennent les tuiles visibles ; `CustomShortcutsService` ajoute tes propres raccourcis ; `HomeCustomizerWindow` est la fenêtre de personnalisation. Les tuiles passent par le même chemin public de navigation que le menu (§8). Trois modes en un clic (Zen, Gamer, Travail) combinent nettoyage mémoire et fichiers temporaires.

### Assistant IA (`Modules/AI`)

L'assistant intégré, entièrement local — le panneau ouvert par `Ctrl + Espace`. Détail au §7.

### Poubelle PC (`Modules/Cleaning`)

`CleaningService` (+ ViewModel + vue) liste chaque cible — fichiers temporaires, caches… — et montre ce qui sera supprimé **avant** toute action. Les suppressions partent à la **Corbeille**. Le résultat indique l'espace libéré et les éléments ignorés. Rien ne s'exécute sans confirmation explicite.

### Réparation & Diagnostic (`Modules/HealthDefense`)

- `ISecurityPolicy` / `SecurityPolicyService` : la politique centrale de commandes. `IProcessExecutionService` la consulte avant tout lancement (§10). Les commandes interdites sont bloquées en un seul point, couvert par `SecurityPolicyTests`.
- `SystemHealthService` : vérifications de l'état système.
- `StartupHealthService` : examine ce qui tourne au démarrage de Windows.

### Analyse Disque (`Modules/DiskAnalyzer`)

- `DiskAnalyzerService` : scanne un lecteur, construit l'arborescence, calcule les zones et les plus gros fichiers.
- `DuplicateFinderService` : le scan de doublons en trois phases (index par taille → groupes candidats → affinage par hash) est entièrement statique et testé ; les liens physiques NTFS sont dédupliqués par identité de fichier.
- `RecycleBinHelper` : suppression sûre via `SHFileOperation` avec `FOF_ALLOWUNDO`.
- La vue : barre de modes segmentée, cartes de lecteurs avec pourcentage rempli, panneau des zones, liste de doublons avec ouverture d'emplacement et mise à la corbeille par fichier.

### Management RAM (`Modules/RamCleaner`)

- `MemoryCleanerService` : nettoyage de working set, global ou par processus.
- `SmartMemoryDaemonService` (+ `ISmartMemoryDaemonService`) : le démon de fond et ses profils. État protégé par `lock`, boucle dans un `Task.Run` avec jeton d'annulation, événements `StatusChanged`, réglages persistés dans `AppSettings`, arrêt propre au déchargement de la vue.
- La vue affiche RAM/CPU en direct et les processus les plus gourmands.

### Réglage Réseau (`Modules/Network`)

- `NetworkMonitorService` : cartes réseau, ping et débit en direct.
- `NetworkBenchmarkService` (`INetworkBenchmarkService`) : test de performance avant/après.
- `NetworkOptimizerService` (`INetworkOptimizerService`) : applique les réglages. **Zéro écriture registre** : `netsh` pour la pile TCP, PowerShell officiel (`Set-NetAdapterAdvancedProperty`, `Set-NetAdapterPowerManagement`) pour la carte. Un paramètre absent est ignoré et journalisé. `RestoreWindowsDefaultsAsync` remet les vrais défauts Windows.
- `NetworkTuningCatalog` : le catalogue des 9 paramètres documentés Microsoft (5 TCP + 4 carte), testé unitairement.
- `NetworkSettingsGuard` : validation statique de toutes les valeurs avant application (niveaux d'autotuning, états on/off, caractères interdits dans les noms de carte…).

### Management Alimentation PC (`Modules/Power`)

- `PowerPlanCatalog` : définitions des plans et arguments `powercfg` correspondants (testé unitairement).
- `CpuPowerService` : enrobage admin de `powercfg`. Chaque paramètre est validé par `powercfg /q` avant application ; les non supportés sont ignorés et comptés. Les plans Coclico sont supprimables à tout moment, retour au plan Équilibré d'abord. Aucune écriture registre.
- `CpuPowerBenchmark` : charge CPU comparative sur les trois plans (5 s chacun), restauration du plan de départ par GUID réel.
- `AdaptivePowerService` : le démon adaptatif — bascule de plans selon la charge CPU, avec seuils, hystérésis, délai de repos, et re-synchronisation périodique avec le plan réellement actif. La règle vit dans `AdaptivePowerRules`, pure, testée sans le démon.

### Applications (`Modules/Programs`)

`InstalledProgramsService` énumère les programmes installés (par utilisateur et par machine) ; `UninstallCommandParser` transforme les chaînes de désinstallation brutes en exécutable + arguments sûrs (testé) ; la vue permet d'ouvrir, localiser ou désinstaller chaque entrée.

### Installeur Winget (`Modules/Apps`)

- `AppCatalogService` : lit `Assets/apps.json` (70+ logiciels) et `Assets/apppacks.json` (17 packs). Cohérence validée par `AppsCatalogTests`.
- `WingetEngineService` : exécution Winget, installation automatique de l'appbundle Winget si absent.
- `GitHubToolService` : télécharge depuis GitHub les logiciels absents de Winget (DS4Windows, NVIDIA Profile Inspector…), dézippe et localise l'exécutable.
- `AppShortcutsService` : raccourcis de tes logiciels sur l'accueil.

### Paramètres et Aide (`Modules/Settings`, `Modules/Help`)

`SettingsView` (+ ViewModel), `ProfileService` (profils utilisateur), `UserAccountService` (comptes et avatars personnalisés), et les fenêtres support : `SplashWindow`, `ProfileWindow` (avec `AvatarCropWindow`), `ConfirmationDialog`, `HelpView`.

## 6. Galerie d'écrans

Toutes les captures sont dans `docs/`. Elles illustrent aussi les guides utilisateur.

<p align="center">
  <img src="docs/accueil.png" alt="Accueil" width="400">
  <img src="docs/Menu.png" alt="Menu" width="400">
  <br>
  <em>Accueil (gauche) et tiroir de navigation (droite) — trois groupes dépliables.</em>
</p>

<p align="center">
  <img src="docs/Nettoyage-pc.png" alt="Poubelle PC" width="400">
  <img src="docs/santer-defence.png" alt="Réparation & Diagnostic" width="400">
  <br>
  <em>Poubelle PC (gauche) et Réparation & Diagnostic (droite).</em>
</p>

<p align="center">
  <img src="docs/espace-disque.png" alt="Analyse Disque" width="400">
  <img src="docs/ram-clean.png" alt="Management RAM" width="400">
  <br>
  <em>Analyse Disque (gauche) et Management RAM (droite).</em>
</p>

<p align="center">
  <img src="docs/Réglage réseau.png" alt="Réglage Réseau" width="400">
  <img src="docs/Mangement-power.png" alt="Management Alimentation PC" width="400">
  <br>
  <em>Réglage Réseau (gauche) et Management Alimentation PC (droite) — 9 paramètres réseau documentés, 3 plans honnêtes.</em>
</p>

<p align="center">
  <img src="docs/appplication.png" alt="Applications" width="400">
  <img src="docs/Installeur.png" alt="Installeur Winget" width="400">
  <br>
  <em>Applications (gauche) et catalogue de l'installeur Winget (droite).</em>
</p>

<p align="center">
  <img src="docs/Pack.png" alt="Packs" width="400">
  <img src="docs/update.png" alt="Mises à jour" width="400">
  <br>
  <em>Packs d'applications (gauche) et mises à jour des logiciels (droite).</em>
</p>

<p align="center">
  <img src="docs/IA.png" alt="Assistant IA" width="400">
  <img src="docs/Notification.png" alt="Notifications" width="400">
  <br>
  <em>Assistant IA local (gauche) et panneau de notifications (droite).</em>
</p>

## 7. L'assistant IA en détail

Tout tourne **localement**. Aucun appel cloud, aucune clé API dans la base de code. `AiLocalOnlyTests` le garantit (aucun fournisseur cloud, Ollama uniquement en boucle locale).

- **Providers.** `MultiProviderClient` (avec `AiProviderContracts`) parle aux moteurs : fichiers GGUF via LLamaSharp, ou serveur Ollama local. `AiHardwareService` détecte le GPU/VRAM et décide de l'offload des couches. Sans backend CUDA, LLamaSharp retombe sur le CPU automatiquement.
- **Chat.** `AiChatService` construit le prompt système compact, garde la mémoire de conversation avec élagage, gère les stop tokens. L'assistant sait toujours où tu es : la navigation met à jour `IAiService.CurrentStatusContext` (§8).
- **Connaissance (RAG).** `RagService` fait du retrieval BM25/TF-IDF sur la base embarquée `Resources/docs/coclico.md`, qui décrit chaque module, action et règle de sécurité. `SimpleStemmer` gère français et anglais.
- **Actions.** `AiActionParser` transforme la sortie structurée du modèle en actions (naviguer vers un module, nettoyer la RAM, optimiser le réseau…). `AiToolExecutionService` les exécute avec assainissement strict — cibles de navigation autorisées, identifiants de paquets validés, bornes numériques. Rien ne modifie le système sans confirmation explicite. Les tests `AiToolExecutionServiceSanitizerTests` couvrent l'assainissement.
- **Vision.** `AiVisionService` comprend les images joints avec les mêmes modèles locaux.

## 8. Navigation et menu

- Le menu principal est le **tiroir hamburger** dans `MainWindow.xaml`. Accueil, puis les groupes dépliables **MAINTENANCE**, **RÉGLAGE PC** et **APPLICATIONS**, puis Paramètres et Aide, toujours visibles. L'état déplié est persisté dans `AppSettings.MenuExpandedGroups`.
- Chaque carte porte un `Tag` (l'identifiant du module). Le registre des modules vit dans `MainWindow.Navigation.cs` :

```csharp
private static readonly (string Tag, Func<UIElement> Factory)[] ModuleRegistry =
[
    ("Home", static () => ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView()),
    ("Programs", static () => new ProgramsView()),
    // ... 11 entrées au total
];
```

- Un clic passe par `NavigateToTagAsync(tag)` : résolution ou création de la vue (cache `_viewCache`), mise à jour du contexte IA, transition, indicateur actif.
- `TagForAiAction` mappe les actions IA (`open_dashboard`, `open_power`…) vers ces mêmes tags. Tuiles d'accueil et actions IA passent par ce chemin public unique. Ne lève jamais un `Click` de `RadioButton` manuellement.
- Les gestionnaires d'événements asynchrones passent par `AsyncEventSafe.Run(...)` (Common/Diagnostics) : une exception dans un `async void` est capturée et journalisée au lieu de faire crasher l'app.
- Raccourcis globaux : `KeyboardShortcutsService`, plus `F11` (plein écran) et `Ctrl+Espace` (panneau IA) gérés dans `MainWindow`.

## 9. Ajouter un module, pas à pas

Exemple concret : le module Management Alimentation PC (`Modules/Power`). Les étapes :

1. **Créer le module** : `Modules/<Name>/Services/*.cs` (logique) et `Modules/<Name>/Views/<Name>View.xaml(.cs)`.
2. **Enregistrer les services** dans `App.xaml.cs` (`services.AddSingleton<...>()`, ou `AddTransient` pour les services à état courte durée).
3. **Câbler la navigation** dans `MainWindow.Navigation.cs` : une entrée dans `ModuleRegistry`, puis la carte correspondante dans `MainWindow.xaml` (un élément avec `Tag="<Tag>"`, dans le bon groupe). Icônes : uniquement des symboles `Wpf.Ui.Controls.SymbolRegular` existants.
4. **Action IA** : un cas dans `TagForAiAction` si le module doit être ouvrable par l'assistant.
5. **Clés de langue** : chaque clé dans `Resources/Lang/fr.xaml` **et** `en.xaml` (les deux dictionnaires restent synchronisés).
6. **Tests** : la logique pure dans `Coclico.Tests`. Pour une vue simple, un test d'instanciation STA évite une `XamlParseException` au premier clic.
7. **Vérifier** : `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` (le nouveau code doit être propre).

## 10. Patterns de services

**Service pur et testable.** Sépare l'algorithme de tout ce qui touche le système. Exemple `DuplicateFinderService` : les étapes de regroupement (`IndexBySize`, `CandidateGroups`, `RefineByHash`) sont statiques et testées ; le scan complet n'est qu'un `Task.Run` autour. Même approche dans `PowerPlanCatalog` (données + construction d'arguments `powercfg`) et `AdaptivePowerRules.Evaluate` (règle pure, testée sans le démon).

**Exécution de processus.**

- Chemin standard : `IProcessExecutionService.ExecuteAsync`, filtré par `ISecurityPolicy.IsCommandBlocked`, timeout, annulation, encodages configurés. `requiresElevation: true` met `Verb = "runas"` → **une invite UAC par appel**.
- Pour un outil admin exécuté en série (ex. `powercfg` dans `CpuPowerService`), ne pas demander l'élévation à chaque commande : appeler l'exécutable directement (le pattern `ProcessStartInfo` de `NetworkOptimizerService`) et **contrôler l'UI** sur `App.IsRunningAsAdministrator()`. L'utilisateur élève l'app une fois ; pas de tempête d'UAC.

**Interop natif.** Convention du dépôt : `DllImport` classique (pas `LibraryImport`, qui exigerait `AllowUnsafeBlocks`). Exemples : `GetSystemPowerStatus` (batterie/secteur, `CpuPowerService`), `GetSystemTimes` (charge CPU, `AdaptivePowerService`), `SHFileOperation` (Corbeille, `RecycleBinHelper`). Toujours enrober dans une petite classe interne avec une méthode publique tolérante aux échecs.

**Démons.** Deux références : `SmartMemoryDaemonService` (nettoyage mémoire) et `AdaptivePowerService` (bascule de plans). Points communs : état protégé par `lock`, boucle dans un `Task.Run` avec `CancellationTokenSource`, événements de type `StatusChanged`, persistance dans `AppSettings`, arrêt propre au `Unloaded` des vues. Les événements levés sur un thread de fond doivent être marshalés avec `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**Mesures système.** Préférer les API Win32 (`GetSystemTimes`) aux compteurs de performance : pas de dépendance, pas de souci de localisation, calcul de delta trivial.

## 11. Localisation et thème

- Chaînes UI : `Resources/Lang/fr.xaml` et `en.xaml`, consommées en `{DynamicResource MyKey}`. `LocalizationService` remplace le dictionnaire fusionné au changement de langue ; `LocalizationService.Get(key)` est le repli du code-behind.
- **Règle absolue : toute clé ajoutée doit exister dans les deux fichiers.**
- Ressources XAML : `XamlResourceValidationTests` vérifie statiquement que chaque `{StaticResource}` (ex. `CornerCard`, `CornerBtn`) et chaque dictionnaire fusionné se résout, sinon le build de tests échoue. Une clé `{DynamicResource}` manquante échoue silencieusement au chargement, d'où l'importance de la vérification.
- Brushes globales dans `App.xaml` : `BgCardBrush`, `BgSurfaceBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `Text*Brush`… `ThemeService` applique accent et thème depuis les réglages.
- `FindResource` **lève** une exception si absent ; en code-behind, préférer `TryFindResource` ou un cast `as Brush` avec repli.

## 12. Réglages et données

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (JSON camelCase via `[JsonPropertyName]`, valeurs par défaut sur les propriétés). Ajouter une propriété = la déclarer avec sa valeur par défaut ; tout le reste (chargement, sauvegarde, migration douce des champs manquants) est géré par `SettingsService`.
- Autres données : logs Serilog (`logs/`), piste d'audit (`IAuditLog`, rétention configurable), historique de notifications (`NotificationService`, 50 entrées), profils utilisateur (`ProfileService`).
- La CentralUnit persiste ses statistiques d'usage via `CentralUnitStigmergy`.

## 13. Tests

`Coclico.Tests` (xunit) : tout doit être vert avant toute fusion. `InternalsVisibleTo("Coclico.Tests")` donne accès aux membres `internal`. Couverture actuelle :

| Fichier | Ce qu'il couvre |
| --- | --- |
| `Common/Core/CentralCircuitBreakerTests.cs` | Disjoncteur : seuils, états, cooldown |
| `Common/Core/CentralErrorTriageTests.cs` | Tri des erreurs récurrentes |
| `Common/Core/CentralUnitActivityTests.cs` | Niveaux d'activité |
| `Common/Core/CentralUnitBusTests.cs` | Bus d'événements |
| `Common/Core/CentralUnitFlagStoreTests.cs` | Planificateur de drapeaux |
| `Common/Core/CentralUnitServiceTests.cs` | Boucle de pression CPU/RAM, boost |
| `Common/Core/CentralUnitStigmergyTests.cs` | Scores d'usage par module |
| `Common/Core/CentralUnitVariablesTests.cs` | Variables partagées |
| `Common/Core/CentralUnitWatchdogTests.cs` | Chien de garde |
| `Common/Core/SettingsServiceTests.cs` | Chargement / sauvegarde / migration des réglages |
| `Common/Core/UpdateVersionTests.cs` | Comparaison de versions numériques, préreleases |
| `Common/Execution/ProcessExecutionServiceTests.cs` | Exécution de processus filtrée |
| `Modules/Ai/AiLocalOnlyTests.cs` | Garde-fous « IA locale uniquement » |
| `Modules/Ai/AiToolExecutionServiceSanitizerTests.cs` | Assainissement des actions IA |
| `Modules/Ai/RagServiceTests.cs` | Retrieval sur la base de connaissances |
| `Modules/Apps/AppsCatalogTests.cs` | Catalogues apps.json / apppacks.json |
| `Modules/Cleaning/CleaningServiceTests.cs` | Catégories de nettoyage |
| `Modules/DiskAnalyzer/DiskAnalyzerTests.cs` | Scan d'arborescence, `FormatSize`, instanciation STA |
| `Modules/DiskAnalyzer/DuplicateFinderTests.cs` | Doublons en 3 phases, seuils, top-N |
| `Modules/HealthDefense/SecurityPolicyTests.cs` | Commandes interdites |
| `Modules/Network/NetworkSettingsGuardTests.cs` | Validation des valeurs réseau |
| `Modules/Network/NetworkTuningCatalogTests.cs` | Catalogue des 9 paramètres |
| `Modules/Power/AdaptivePowerRulesTests.cs` | Règle adaptative : seuils, hystérésis, cooldown |
| `Modules/Power/PowerPlanCatalogTests.cs` | Noms de plans, arguments powercfg, bornes |
| `Modules/Programs/UninstallCommandParserTests.cs` | Analyse des chaînes de désinstallation |
| `Modules/RamCleaner/MemoryCleanerServiceTests.cs` | Profils de purge mémoire |
| `Modules/Settings/ProfileServiceTests.cs` | Profils utilisateur |
| `Ui/Xaml/XamlResourceValidationTests.cs` | Résolution `StaticResource`, dictionnaires, chemins d'images |

## 14. Build, format, CI et release

Commandes locales (identiques à la CI) :

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Règle de format : tout nouveau code doit passer `dotnet format --verify-no-changes`. Si une vérification locale échoue, lance `dotnet format Coclico.slnx` (sans `--verify`) et commite le résultat.

Workflows dans `.github/workflows/` :

- **`ci.yml`** — sur chaque push/PR vers `main` : restore → vérification du format → build Release → tests → publication d'un artifact win-x64 sur les pushes vers `main`.
- **`release.yml`** — déclenché par un tag `vX.Y.Z` : cohérence tag ↔ `version.txt`, format + tests, publish, installeur via `./installer/build-installer.ps1 -SkipPublish`, ZIP portable + `SHA256SUMS.txt`, release GitHub.
- **`codeql.yml`** — analyse de sécurité CodeQL.
- **`dependency-review.yml`** — revue des dépendances.
- **`scorecard.yml`** — OpenSSF Scorecard.
- **`labeler.yml`, `greetings.yml`, `stale.yml`** — automatisation du dépôt.

## 15. Le pipeline de l'installeur

Ce que fait `installer/build-installer.ps1`, pas à pas (console interactive FR/EN) :

1. **Version** : lue depuis `version.txt` (ou passée avec `-Version`).
2. **Tests d'abord** : `dotnet test` avant tout empaquetage (`-SkipTests` pour sauter). L'installeur n'est jamais construit sur une suite rouge.
3. **Nettoyage** : `publish/` et `artifacts/installer_build` sont vidés (`-SkipClean`).
4. **Publish de Coclico** : `dotnet publish` win-x64, **dépendant du framework** (`--self-contained false`), sans single file, sans PDB → `publish/`.
5. **Payload** : `publish/` est zippé en `payload.zip` dans `Coclico.Installer/`. Exclus : `.pdb` et, sauf `-IncludeCuda`, les fichiers du runtime CUDA. Sans les DLL CUDA, LLamaSharp utilise proprement le backend CPU ; les utilisateurs NVIDIA téléchargent le runtime CUDA à l'installation — c'est ainsi que l'installeur reste léger.
6. **Installeur final** : `Coclico.Installer` est publié en SingleFile autonome compressé avec `payload.zip` embarqué → `artifacts/installer/Coclico-<version>.exe` (variante `-Full-CUDA`), SHA-256 affiché.
7. `-SkipPublish` réutilise le dossier `publish/` existant (c'est ce que fait la CI de release).

À l'installation, le projet autonome `Coclico.Installer` prend le relais (`Services/`) :

- `SystemRequirementService` — exigences système et lecteur cible selon le chemin choisi.
- `GpuDetector` — détection NVIDIA GPU (ne lève jamais).
- `DotnetRuntimeService` — vérifie le runtime .NET 10 Desktop ; au besoin le télécharge et l'installe silencieusement (`/install /passive /norestart /log`, avec reprises).
- `CudaRuntimeDownloader` + `AiModelDownloader` — téléchargements optionnels du runtime CUDA et d'un modèle IA local (catalogue `ai-models.json`, reprise possible).
- `InstallService` — installe pour l'utilisateur courant (défaut `%LOCALAPPDATA%\Programs\Coclico`), crée les raccourcis (`ShortcutHelper`), enregistre l'entrée de désinstallation, déploie le désinstalleur, ferme un Coclico en cours, et préserve réglages et données à la désinstallation.
- `InstallerLocalization` — chaînes FR/EN de l'installeur.

## 16. Pratiques de sécurité

- **Confirmations** avant toute action destructive ; **Corbeille** (`SHFileOperation` + `FOF_ALLOWUNDO`) au lieu d'une suppression définitive pour les fichiers utilisateur.
- **Réseau** : aucune écriture registre — `netsh` et PowerShell uniquement ; chaque valeur validée par `NetworkSettingsGuard` ; restauration des défauts Windows en un clic.
- **Alimentation** : uniquement `powercfg`, chaque paramètre validé par `powercfg /q`, plans Coclico supprimables à tout moment, retour au plan Équilibré.
- **Filtrage de commandes** : passer par `IProcessExecutionService` autant que possible (`ISecurityPolicy` bloque les commandes interdites, testé).
- **IA locale uniquement** : pas de coffre de clés ; Ollama restreint à la boucle locale ; garde-fous testés ; actions assainies et confirmées.
- **Mises à jour** : HTTPS et domaines GitHub uniquement, SHA-256 obligatoire vérifié au téléchargement **et** avant le lancement, version de fichier vérifiée sans exécution ; les releases embarquent `SHA256SUMS.txt`.
- **Élévation à la demande** : l'app démarre en utilisateur standard ; les modules concernés affichent leur état.
- **Logs** : jamais de données personnelles ; `LoggingService.LogException` dans chaque `catch` ; erreurs async routées par `AsyncEventSafe` et triées par la CentralUnit.

## 17. Publier une version

1. Mets à jour `version.txt` à la racine — la source unique de la version, remontée au build et au script d'installeur.
2. Ajuste la version affichée dans la barre d'état de `MainWindow.xaml` si besoin.
3. Fusionne vers `main` (la CI lance : format, build, tests).
4. Crée le tag : `git tag vX.Y.Z && git push origin vX.Y.Z`. La release (installeur + ZIP portable + SHA256SUMS.txt) est générée automatiquement, si le tag correspond à la version.
5. Vérifie la release GitHub : les trois fichiers attendus, et le SHA-256 affiché par le script local correspond à `SHA256SUMS.txt`.

---

<p align="center"><i>Retour : <a href="README.md">guide utilisateur (EN)</a> · <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="READMEDEV.md">documentation développeur (EN)</a></i></p>

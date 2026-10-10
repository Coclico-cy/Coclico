<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Icône de Coclico" width="72" />

# Coclico : Documentation développeur

### Architecture, patterns, pipeline de build et CI, expliqués module par module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Licence : MIT](https://img.shields.io/badge/licence-MIT-green.svg)](LICENSE.txt)

[User guide (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · [Developer (EN)](READMEDEV.md) · **Développeur (FR)**

</div>

---

## Sommaire

1. [Stack technique et projets](#1-stack-technique-et-projets)
2. [Démarrage de l'application](#2-démarrage-de-lapplication)
3. [Organisation du code](#3-organisation-du-code)
4. [Navigation et menu](#4-navigation-et-menu)
5. [Ajouter un module, pas à pas](#5-ajouter-un-module-pas-à-pas)
6. [Patterns de services](#6-patterns-de-services)
7. [Localisation et thème](#7-localisation-et-thème)
8. [Réglages et données](#8-réglages-et-données)
9. [Tests](#9-tests)
10. [Build, format, CI et release](#10-build-format-ci-et-release)
11. [Le pipeline de l'installeur](#11-le-pipeline-de-linstalleur)
12. [Pratiques de sécurité](#12-pratiques-de-sécurité)
13. [Publier une version](#13-publier-une-version)

---

## 1. Stack technique et projets

| Élément | Détail |
| --- | --- |
| Cible | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Langage | C# (`LangVersion latest`), `Nullable` et `ImplicitUsings` activés |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (contrôles Fluent : `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4 (certains ViewModels ; beaucoup de vues restent en code-behind) |
| DI / hébergement | Microsoft.Extensions.DependencyInjection 10 + la façade statique maison `ServiceContainer` |
| Logs | Serilog (fichier quotidien dans `%APPDATA%\Coclico\logs`, rétention 14 jours) |
| IA locale | LLamaSharp 0.26 + backend CUDA 12 (repli CPU automatique) |
| Divers | System.Management (WMI), System.Reactive, System.Text.Json |

La solution `Coclico.slnx` contient **deux projets** :

```text
Coclico/         Application WPF (application principale)
Coclico.Tests/   Tests xunit (référence Coclico via InternalsVisibleTo)
```

`Coclico.Installer/` n'est **pas** dans la solution : c'est un projet WPF autonome compilé uniquement par `installer/build-installer.ps1` (voir §11).

## 2. Démarrage de l'application

`App.xaml.cs` (classe `Coclico.App`) :

1. **Gestion d'erreurs globale** : hooks sur `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` et `DispatcherUnhandledException` → `LoggingService` + `IAppErrorManager`.
2. `ServiceContainer.Build(services => ...)` : construit le `IServiceProvider` (paresseux, `ValidateOnBuild`). Serilog est configuré ici, plus les services de base (`IAuditLog`, `ISecurityPolicy`, `IProcessExecutionService`, cache mémoire, `HttpClient`), puis les services de tous les modules.
3. L'application démarre **en utilisateur standard** (aucune invite UAC au lancement) ; l'élévation se fait à la demande via `App.RestartAsAdmin()`. L'état se lit avec `App.IsRunningAsAdministrator()`.
4. Réglages chargés depuis `%APPDATA%\Coclico\settings.json`, puis `LocalizationService.SetLanguage`, `ThemeService.ApplyCurrentSettings`, puis `SplashWindow` → `MainWindow`.

```text
App.OnStartup
  ├── Hooks d'exceptions globales (AppDomain / TaskScheduler / Dispatcher)
  ├── ServiceContainer.Build(...)          ← Serilog + base + services des modules
  ├── RemoveLegacyAiCredentialVault()
  ├── SettingsService : chargement de %APPDATA%\Coclico\settings.json
  ├── LocalizationService.SetLanguage(...)
  ├── ThemeService.ApplyCurrentSettings()
  └── SplashWindow → MainWindow
```

`ServiceContainer` est une façade statique : `ServiceContainer.GetRequired<T>()`, `GetOptional<T>()`. Les vues (code-behind) résolvent leurs services ainsi.

## 3. Organisation du code

```text
Coclico/
  Common/
    Core/          SettingsService, CacheService, StartupService, UpdateCheckService...
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService
    Execution/     IProcessExecutionService (exécution filtrée par ISecurityPolicy)
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   TrayService, KeyboardShortcutsService
  Converters/      Convertisseurs de valeurs XAML
  Modules/         Un dossier par module (voir ci-dessous)
  Resources/
    Lang/          fr.xaml + en.xaml (dictionnaires de chaînes)
    icone/         icônes de l'application
  App.xaml         Styles globaux (pinceaux, CornerCard/CornerBtn, styles de boutons)
  MainWindow.*     Fenêtre principale : barre supérieure, tiroir de navigation, panneaux
```

Anatomie d'un module (`Modules/<Nom>/`) :

- `Services/` : la logique métier, le plus possible **pure et testable** (sans dépendance à WPF).
- `Views/` : `XxxView.xaml` + code-behind. Certaines vues utilisent un `ViewModels/` + CommunityToolkit.
- `Windows/`, `Models/` : selon les besoins (fenêtres modales, modèles de données).

## 4. Navigation et menu

- Le menu principal est le **tiroir du bouton hamburger** (`ArchitecturalDropdownOverlay` dans `MainWindow.xaml`). C'est un accordéon groupé : Tableau de bord, puis les groupes dépliables **MAINTENANCE** (Nettoyage, Santé & Défense, Espace disque), **PERFORMANCE** (RAM, Réseau, CPU & Alimentation) et **APPLICATIONS** (Programmes, Installeur), puis Paramètres et Aide toujours visibles. L'état déplié/replié est persisté dans `AppSettings.MenuExpandedGroups`.
- Chaque carte porte un `Tag` (identifiant du module). Le clic passe par `MainWindow.NavigateTo(tag)` → `NavigateToTagAsync(tag)` qui :
  1. résout ou crée la vue (cache par champ `_xxxView ??= new XxxView()`) ;
  2. met à jour `IAiService.CurrentStatusContext` (le contexte de l'assistant IA) ;
  3. joue la transition et met à jour l'indicateur actif.
- Les tuiles du Dashboard et les actions de l'IA (`open_cleaning`, `open_power`…) passent par ce même chemin public ; ne jamais lever l'événement `Click` d'un `RadioButton` à la main.
- Raccourcis globaux : `KeyboardShortcutsService` (IA, nettoyage RAM…), plus `F11` (plein écran) et `Ctrl+Espace` (panneau IA) gérés dans `MainWindow`.

## 5. Ajouter un module, pas à pas

Exemple concret : le module CPU & Alimentation (`Modules/Power`). Étapes :

1. **Créer le module** : `Modules/<Nom>/Services/*.cs` (logique) et `Modules/<Nom>/Views/<Nom>View.xaml(.cs)`.
2. **Enregistrer les services** dans `App.xaml.cs` (`services.AddSingleton<...>()`, ou `AddTransient` pour les services à état court).
3. **Brancher la navigation** dans `MainWindow.xaml.cs` :
   - un champ privé `_xxxView` ;
   - une case `"Tag" => _xxxView ??= ...` dans le `switch` de `NavigateToTagAsync` ;
   - la phrase de contexte IA correspondante ;
   - (optionnel) une case `open_xxx` dans `AiChatPanel_ActionRequested`.
4. **Ajouter la carte** dans le tiroir (`MainWindow.xaml`) : un `Border` avec `Tag="<Tag>"` et `MouseDown="DropdownItem_MouseDown"`, dans le groupe approprié. Icônes : uniquement des symboles `Wpf.Ui.Controls.SymbolRegular` existants.
5. **Clés de langue** : ajouter chaque clé dans `Resources/Lang/fr.xaml` **et** `en.xaml` (les deux dictionnaires doivent rester synchrones). Les vues de modules peuvent aussi embarquer leurs textes en français en dur (comme DiskAnalyzer) ; garder la cohérence par module.
6. **Tests** : logique pure dans `Coclico.Tests` ; pour une vue simple, un test d'instanciation STA évite les `XamlParseException` au premier clic (voir `DiskAnalyzerTests.DiskAnalyzerView_InstantiatesWithoutXamlErrors`). Attention : un test d'instanciation exige une vue sans dépendance DI obligatoire dans le constructeur.
7. **Vérifier** : `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` (le nouveau code doit être propre).

## 6. Patterns de services

**Service pur et testable.** Séparer l'algorithme de tout ce qui touche le système. Exemple `DuplicateFinderService` : les étapes de regroupement (`IndexBySize`, `CandidateGroups`, `RefineByHash`, `ToDuplicateGroups`) sont statiques et testables ; le scan complet n'est qu'un `Task.Run` autour. Même approche pour `PowerPlanCatalog` (données + construction d'arguments powercfg) et `AdaptivePowerRules.Evaluate` (règle pure, testée sans le daemon).

**Exécution de processus.**

- Voie standard : `IProcessExecutionService.ExecuteAsync`, filtré par `ISecurityPolicy.IsCommandBlocked`, timeout, annulation, encodages configurés. `requiresElevation: true` met `Verb = "runas"` → **une invite UAC par appel**.
- Pour un outil d'administration lancé en série (ex. `powercfg` dans `CpuPowerService`), on n'utilise **pas** l'élévation par commande : on appelle `powercfg.exe` directement (pattern `ProcessStartInfo` de `NetworkOptimizerService`) et on **conditionne l'UI** sur `App.IsRunningAsAdministrator()`. L'utilisateur relance l'application en admin une fois ; aucune pluie d'UAC.

**Interopérabilité native.** Convention du dépôt : `DllImport` classique (pas `LibraryImport`, qui exigerait `AllowUnsafeBlocks`). Exemples : `GetSystemPowerStatus` (batterie/secteur, `CpuPowerService`), `GetSystemTimes` (charge CPU, `AdaptivePowerService`), `SHFileOperation` (Corbeille, `RecycleBinHelper`). Toujours encapsuler dans une petite classe interne avec une méthode publique tolérante aux échecs.

**Daemons.** Deux références : `SmartMemoryDaemonService` (nettoyage mémoire) et `AdaptivePowerService` (bascule des plans selon la charge CPU). Points communs : état protégé par un `lock`, boucle dans un `Task.Run` avec `CancellationTokenSource`, événements type `StatusChanged`, persistance dans `AppSettings`, arrêt propre sur le `Unloaded` des vues. Les événements arrivant sur un thread de fond doivent être rebasculés via `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**Mesures système.** Préférer les API Win32 (`GetSystemTimes`) aux compteurs de performance : pas de dépendance, pas de problème de localisation, delta facile à calculer.

**Système de mise à jour.** `UpdateCheckService` (singleton) interroge la dernière release GitHub au démarrage puis toutes les ~6 heures avec un léger décalage aléatoire ; l'événement `UpdateAvailable` est consommé par `MainWindow`, qui marque le bouton de synchronisation en jaune et propose le dialogue `Modules/Installer/Windows/UpdateWindow` (versions, notes de release, progression). Le flux d'installation : `UpdateManager.DownloadReleaseAsync` (HTTPS + domaine GitHub uniquement, SHA-256 obligatoire, progression en Mo) → `LaunchInstaller(chemin, tagAttendu)` qui re-vérifie le hash **et** la version du fichier (`FileVersionInfo`, sans exécuter) avant l'élévation `runas`. « Ignorer cette version » persiste dans `AppSettings.SkippedUpdateVersion` ; la comparaison de versions (`IsNewerVersion`) est numérique et gère les préreleases.

## 7. Localisation et thème

- Chaînes UI : `Resources/Lang/fr.xaml` et `en.xaml`, consommées en `{DynamicResource MaCle}`. `LocalizationService` remplace le dictionnaire fusionné au changement de langue ; `LocalizationService.Get(cle)` sert de repli côté code.
- **Règle absolue : toute clé ajoutée doit exister dans les deux fichiers.**
- Ressources XAML : `XamlResourceValidationTests` vérifie statiquement que chaque `{StaticResource}` (ex. `CornerCard`, `CornerBtn`) et chaque dictionnaire fusionné se résolvent, sinon les tests échouent. Les `{DynamicResource}` manquants n'échouent pas au chargement (silencieux), d'où l'importance de vérifier les clés.
- Pinceaux globaux dans `App.xaml` : `BgCardBrush`, `BgSurfaceBrush`, `BgElevatedBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `ArchitecturalYellowBrush`, `Text*Brush`… `ThemeService` applique accent/thème depuis les réglages.
- `FindResource` **lève** une exception si absent ; en code-behind, préférer `TryFindResource` ou un cast `as Brush` avec repli.

## 8. Réglages et données

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (JSON camelCase via `[JsonPropertyName]`, défauts dans les propriétés). Ajouter une propriété = la déclarer avec une valeur par défaut ; tout le reste (chargement, sauvegarde, migration douce des champs manquants) est géré par `SettingsService`.
- Autres données : logs Serilog (`logs/`), piste d'audit (`IAuditLog`, rétention configurable), instantanés réseau (module Network), profils utilisateur (`ProfileService`).

## 9. Tests

`Coclico.Tests` (xunit) : tout doit passer avant toute fusion :

| Fichier | Ce qu'il couvre |
| --- | --- |
| `DiskAnalyzerTests.cs` | Scan d'arborescence, `FormatSize`, instanciation STA de la vue (attrape les `XamlParseException`) |
| `XamlResourceValidationTests.cs` | Résolution des `StaticResource`, dictionnaires fusionnés et chemins d'images dans Coclico et Coclico.Installer |
| `AiLocalOnlyTests.cs` | Garde-fous « IA locale uniquement » (pas de clés cloud, boucle locale Ollama) |
| `DuplicateFinderTests.cs` | Doublons en 3 phases, seuil de taille, top-N des gros fichiers, regroupement et tri |
| `PowerPlanCatalogTests.cs` | Noms des plans, arguments powercfg, bornes des valeurs |
| `AdaptivePowerRulesTests.cs` | Règle adaptative (seuils, hystérésis, cooldown), fenêtre glissante |

`InternalsVisibleTo("Coclico.Tests")` permet aux tests d'atteindre les méthodes `internal` des services.

## 10. Build, format, CI et release

Commandes locales (identiques à la CI) :

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Règle de format : tout nouveau code doit passer `dotnet format --verify-no-changes`. Le dépôt est format-clean ; si une vérification échoue localement, lance `dotnet format Coclico.slnx` (sans `--verify`) et committe le résultat dans une PR dédiée pour garder le diff lisible.

**CI (`.github/workflows/ci.yml`)** : restore → vérification du format → build Release → tests (artefact TRX) → sur les push `main`, publication win-x64 autonome en artefact.

**Release (`.github/workflows/release.yml`)** : déclenchée par un tag `vX.Y.Z` :

1. vérifie que le tag correspond à la `Version` de `Coclico/Coclico.csproj` (sinon échec) ;
2. format + tests ;
3. `dotnet publish` autonome win-x64 ;
4. `./installer/build-installer.ps1 -Version X.Y.Z -SkipPublish` ;
5. ZIP portable + `SHA256SUMS.txt` (setup + zip) ;
6. release GitHub avec les trois fichiers.

## 11. Le pipeline de l'installeur

Ce que fait `installer/build-installer.ps1`, étape par étape :

1. **Version** : lue depuis `Coclico.csproj` (ou passée via `-Version`).
2. **Publication** de Coclico : `dotnet publish` win-x64, autonome, sans symboles → `publish/`.
3. **Désinstalleur léger** : `Coclico.Installer` est compilé en SingleFile **sans** payload (`-p:EmbedPayload=false`) → `uninstaller.bin` (environ 45 Mo) ; c'est le binaire qui désinstallera depuis les Paramètres Windows.
4. **Payload** : `publish/` est zippé en `payload.zip` (compression optimale). Par défaut, le dossier `cuda12` est **exclu** (installeur léger ~50 Mo) : sans `ggml-cuda.dll`, LLamaSharp chargerait le backend CUDA et planterait ; sans le dossier, il bascule sur la variante CPU incluse. `-IncludeCuda` rétablit le runtime complet (~1,1 Go).
5. **Installeur final** : `Coclico.Installer` est recompilé en SingleFile **avec** `payload.zip` et `uninstaller.bin` embarqués → `artifacts/installer/Coclico-<version>.exe` (variante `-Full-CUDA` : `Coclico-<version>-Full-CUDA.exe`), SHA-256 affiché.
6. `-SkipPublish` réutilise le dossier `publish/` existant (c'est ce que fait la CI de release après son propre publish) ; `-BuildUninstaller` compile en plus l'ancien désinstalleur autonome s'il est présent.

L'installeur s'installe pour le compte utilisateur courant, crée l'entrée de désinstallation dans le registre (via l'installeur lui-même, pas par des écritures registre dispersées) et conserve réglages et données à la désinstallation.

## 12. Pratiques de sécurité

- **Confirmations** avant toute action destructive ; **Corbeille** (`SHFileOperation` + `FOF_ALLOWUNDO`) plutôt que suppression définitive pour les fichiers utilisateur.
- **Instantanés réseau** avant modification + restauration en un clic.
- **Filtrage des commandes** : passer par `IProcessExecutionService` quand c'est possible (`ISecurityPolicy` bloque les commandes interdites).
- **Module alimentation** : uniquement `powercfg` (aucune écriture registre directe), chaque paramètre validé par `powercfg /q` avant application (les paramètres non supportés sont ignorés et comptés), plans Coclico supprimables à tout moment, retour au plan Équilibré avant suppression.
- **IA locale uniquement** : aucun coffre de clés cloud ; Ollama limité à la boucle locale ; garde-fous testés par `AiLocalOnlyTests`.
- **Élévation à la demande** : l'application démarre en utilisateur standard ; les modules concernés affichent leur état et orientent vers le bouton d'élévation existant.
- **Logs** : jamais de données personnelles ; `LoggingService.LogException` dans chaque `catch`.

## 13. Publier une version

1. Mettre à jour `<Version>` dans `Coclico/Coclico.csproj`.
2. Mettre à jour le numéro affiché dans la barre de statut de `MainWindow.xaml` si nécessaire.
3. Fusionner sur `main` (la CI tourne : format, build, tests).
4. Créer le tag : `git tag vX.Y.Z && git push origin vX.Y.Z`. La release (setup + ZIP + SHA256SUMS) est générée automatiquement, à condition que le tag soit identique à la version du `.csproj`.
5. Vérifier la release GitHub : les trois fichiers attendus, et le SHA-256 affiché par le script d'installation locale correspond à `SHA256SUMS.txt`.

---

<p align="center"><i>Retour à : <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="README.md">user guide (EN)</a> · <a href="READMEDEV.md">developer documentation (EN)</a></i></p>

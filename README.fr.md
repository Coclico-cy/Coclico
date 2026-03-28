# Coclico -- Plateforme d'administration systeme Windows

[![Version](https://img.shields.io/badge/Version-1.0.4-blue)](https://github.com/Coclico-cy/Coclico/releases/latest)
[![Licence](https://img.shields.io/badge/Licence-MIT-green)](LICENSE)
[![Plateforme](https://img.shields.io/badge/Plateforme-Windows%2010%2F11-0078d4)](https://github.com/Coclico-cy/Coclico)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/)
[![Site Web](https://img.shields.io/badge/Site%20Web-coclico--cy.github.io-ff7043)](https://coclico-cy.github.io/Coclico/)

[English](README.md) | **Francais**

Coclico est une application de bureau Windows native dediee a l'administration systeme, la supervision de processus, la maintenance automatisee et l'analyse assistee par intelligence artificielle. L'application fonctionne integralement hors ligne, sans aucune dependance cloud. Un modele de langage local embarque (LLamaSharp 0.26) fournit des capacites d'analyse et d'assistance pilotees par IA, sans qu'aucune donnee ne soit transmise vers l'exterieur.

---

## Table des matieres

1. [Presentation technique](#presentation-technique)
2. [Architecture](#architecture)
3. [Modules](#modules)
4. [Noyau autonome](#noyau-autonome)
5. [Telemetrie et supervision](#telemetrie-et-supervision)
6. [Modele de securite](#modele-de-securite)
7. [Prerequis systeme](#prerequis-systeme)
8. [Installation et compilation](#installation-et-compilation)
9. [Arborescence du projet](#arborescence-du-projet)
10. [Site web et documentation](#site-web-et-documentation)
11. [Contribuer](#contribuer)
12. [Licence](#licence)

---

## Presentation technique

Coclico est une application WPF construite sur .NET 10 (C# preview) selon le patron MVVM. Elle integre huit modules couvrant la supervision systeme en temps reel, la gestion de programmes, l'automatisation par chaines de flux, l'installation de logiciels, le nettoyage de disque, l'audit d'applications, l'optimisation de la memoire vive et l'analyse systeme assistee par IA.

### Pile technologique

| Composant | Technologie | Version |
|-----------|-------------|---------|
| Runtime | .NET 10.0, cible `net10.0-windows10.0.22621.0` | 10.0 |
| Langage | C# (LangVersion: preview) | -- |
| Framework UI | WPF avec WPF-UI (controles Fluent Design) | 4.2 |
| IA locale | LLamaSharp avec double contexte d'execution isole | 0.26.0 |
| Analyse statique | Roslyn (Microsoft.CodeAnalysis.CSharp) | 4.12.0 |
| Injection de dependances | Microsoft.Extensions.DependencyInjection | 10.0.0 |
| Extensions reactives | System.Reactive | 6.0.1 |
| Journalisation | Serilog avec fichiers rotatifs quotidiens (retention 14 jours) | 4.2.0 |
| Toolkit MVVM | CommunityToolkit.Mvvm | 8.4.0 |
| Tests | xUnit + Moq | -- |

### Optimisations de performance

- **TieredPGO** active pour la compilation a chaud adaptive
- **GC concurrent** pour minimiser les pauses de collecte
- **OptimizationPreference: Speed** pour un profil d'execution oriente vitesse
- **Compilation hierachisee** (TieredCompilation) active

L'application impose une elevation administrateur au demarrage (invite UAC). Toute inference IA, tout traitement de donnees et toute modification systeme restent strictement locaux.

---

## Architecture

### Cycle de vie de l'application

1. **Elevation UAC** -- `App.OnStartup` verifie que le processus s'execute avec les privileges administrateur. Si ce n'est pas le cas, une invite UAC est presentee.
2. **Construction du conteneur DI** -- `ServiceContainer.Build` enregistre l'ensemble des singletons et transients avec `ValidateOnBuild = true`, garantissant que toute dependance manquante provoque une erreur immediate au demarrage.
3. **Ecran de demarrage** -- `SplashWindow` s'affiche pendant l'initialisation des services et le chargement des ressources.
4. **Fenetre principale** -- `MainWindow` s'ouvre avec une barre laterale de navigation donnant acces a tous les modules.
5. **Premier lancement** -- Lors de la premiere execution, `LauncherWindow` permet de configurer le mode de demarrage prefere.

### Injection de dependances

L'architecture repose integralement sur l'injection par constructeur. Aucun singleton statique n'est utilise. Les services sont enregistres en singletons ou transients selon leurs exigences de cycle de vie.

#### Services lies par interface (contrats imposes)

| Interface | Implementation | Responsabilite |
|-----------|---------------|----------------|
| `ICacheService` | `CacheService` | Cache memoire avec politiques d'expiration |
| `IDynamicTracer` | `DynamicTracerService` | Telemetrie runtime, spans d'operations, estimation P99 |
| `IResourceAllocator` | `ResourceAllocatorService` | Priorite des processus, affinite et gestion du working set |
| `IRollbackService` | `RollbackService` | Snapshots de fichiers avec chiffrement DPAPI |
| `ISourceAnalyzer` | `SourceAnalyzerService` | Analyse AST via Roslyn (complexite cyclomatique, metriques Halstead, indice de maintenabilite) |
| `IStateValidator` | `StateValidatorService` | Simulation de patches et validation du delta de complexite |
| `ICodePatcher` | `CodePatcherService` | Patching de code automatise avec workflow d'approbation |
| `IOptimizationEngine` | `OptimizationEngineService` | Cycle d'optimisation toutes les 30 secondes, pipeline 3 etapes |
| `IAuditLog` | `AuditLogService` | Journal d'audit NDJSON en ajout seul avec contexte de decision IA |
| `ISecurityPolicy` | `SecurityPolicyService` | Regles de securite configurables avec defauts non-suppressibles |
| `IAiService` | `AiChatService` | Interface unifiee d'acces au LLM local |

#### Singletons concrets

`SettingsService`, `ThemeService`, `LocalizationService`, `ProfileService`, `ProcessWatcherService`, `NetworkMonitorService`, `ResourceGuardService`, `KeyboardShortcutsService`, `StartupService`, `FeatureExecutionEngine`.

#### Services transients

`CleaningService`, `InstallerService`, `WorkflowService`, `FlowChainService`, `FlowChainExecutionService`, `StartupHealthService`, `UserAccountService`.

### Double executeur LLM

`AiChatService` maintient deux paires isolees `LLamaContext` + `InteractiveExecutor` :

- **`_chatCtx` / `_chatSem`** -- contexte dedie au chat IA utilisateur, lie a la vue `AiChatView`
- **`_engineCtx` / `_engineSem`** -- contexte dedie aux taches du moteur d'optimisation en arriere-plan

Les contextes utilisent un patron d'echange immutable (`Interlocked.Exchange`) permettant la reinitialisation sans blocage des consommateurs. Aucun semaphore n'est jamais acquis depuis le thread UI, ce qui garantit la reactivite de l'interface.

**Dechargement automatique** -- apres une periode d'inactivite configurable (`aiIdleTimeoutMinutes`, defaut : 5 minutes), le modele est automatiquement decharge de la memoire, liberant environ 2,5 Go de RAM. Le rechargement est transparent a la prochaine sollicitation.

### Structure MVVM

Les vues resident dans `Views/`, les ViewModels dans `ViewModels/`, avec injection des services par constructeur. Les ViewModels s'appuient sur `CommunityToolkit.Mvvm` pour la notification de changement de propriete et le binding de commandes. Toutes les mises a jour de l'interface passent par `Dispatcher.InvokeAsync` -- jamais de blocage du thread UI en attente d'une reponse LLM ou WMI.

### Moteur d'execution des fonctionnalites

`FeatureExecutionEngine` encapsule l'execution de toutes les fonctionnalites avec un circuit-breaker et de la telemetrie. Toute action longue passe par `RunFeatureAsync`. `FlowChainExecutionService` execute les chaines d'automatisation definies par l'utilisateur ; toute operation modifiant le systeme (registre, WMI, fichiers) transite obligatoirement par ce service.

### Algorithmes integres

Le repertoire `Services/Algorithms/` contient quatre composants specialises :

- **IsolationForestDetector** -- detection d'anomalies dans les flux de telemetrie
- **MethodReplacer** -- remplacement de methodes dans les arbres syntaxiques
- **SimpleStemmer** -- racinisation de termes pour le moteur RAG
- **TDigest** -- estimation de quantiles (P50, P95, P99) pour les metriques de performance

---

## Modules

### 1. Tableau de bord

Supervision systeme en temps reel avec rafraichissement toutes les 3 secondes. Affiche le pourcentage CPU, la RAM utilisee et totale, l'espace disque libre, l'uptime Windows, le nombre de processus actifs et le nombre d'applications installees. Un score de sante global synthetise l'etat du systeme. Trois profils preconfigures (Zen, Gamer, Travail) permettent d'adapter les seuils et actions rapides au contexte d'utilisation.

### 2. Applications

Bibliotheque unifiee des logiciels et jeux installes, alimentee par 8 sources de detection : registre Windows (HKLM et HKCU), Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, Rockstar Games Launcher et Microsoft Store (MSIX). Chaque application peut etre renommee, classee par categorie, lancee ou desinstallee directement. L'ajout manuel est supporte pour les programmes non detectes. Les resultats sont mis en cache pendant 6 heures pour des temps de chargement instantanes.

### 3. Flow Chains (chaines de flux)

Editeur visuel d'automatisation en glisser-deposer pour creer des chaines d'operations systeme multi-etapes. 28 types de noeuds disponibles : lancement d'application, terminaison de processus, commandes shell, scripts PowerShell, operations sur fichiers, requetes HTTP, modifications du registre, verifications systeme, branchement conditionnel, boucles, delais, notifications, et plus encore. 10 operateurs de condition et 3 strategies de gestion d'erreur par noeud (continuer au suivant, arreter la chaine, sauter a la fin). Les chaines sont persistees au format JSON et peuvent etre declenchees par raccourci clavier.

### 4. Installeur rapide

Interface graphique pour l'installation de logiciels via Winget. 6 categories de logiciels preprogrammees. Portee d'installation configurable (machine ou utilisateur). Installation en un clic avec suivi de la progression.

### 5. Nettoyage

Moteur de nettoyage couvrant 10 categories : fichiers temporaires Windows, caches de navigateurs, journaux systeme, caches de miniatures, residus Windows Update, corbeille, et autres. Un mode nettoyage profond (deep clean) etend l'analyse. La pre-estimation de l'espace recuperable est affichee avant toute suppression, offrant un apercu complet sans action destructrice.

### 6. Scanner

Audit des applications installees sur le systeme. Les resultats sont triables et filtrables selon de multiples criteres. Ce module fournit une vue consolidee de l'ensemble du parc logiciel, utile pour l'inventaire et la conformite.

### 7. RAM Cleaner (nettoyage memoire)

Optimisation de la memoire vive par appels P/Invoke natifs. Trois profils de nettoyage (leger, standard, agressif) adaptent l'intensite des operations : collecte du ramasse-miettes, reduction du working set, vidage de la standby list et gestion memoire par processus. Un mode automatique peut etre active pour un nettoyage periodique sans intervention.

### 8. Coclico AI (assistant IA)

Interface de chat avec un modele de langage local, entierement hors ligne. Le modele au format GGUF est embarque dans l'application. Un systeme RAG (Retrieval-Augmented Generation) indexe la documentation embarquee pour enrichir les reponses avec du contexte specifique a Coclico. Le double executeur isole garantit que le chat utilisateur et les taches d'arriere-plan n'interferent jamais. L'acceleration GPU via Vulkan est optionnelle ; le CPU est utilise par defaut avec repli automatique en cas d'echec GPU.

---

## Noyau autonome

Le noyau autonome constitue le cerveau analytique de Coclico. Il fonctionne en arriere-plan et orchestre un cycle continu d'observation, d'analyse et d'action.

### OptimizationEngineService

Pipeline en 3 etapes execute toutes les 30 secondes :

1. **Collecter** -- rassemble les metriques de telemetrie du systeme
2. **Decider** -- analyse les donnees et determine les optimisations necessaires via le contexte LLM dedie (`_engineSem`)
3. **Executer** -- applique les actions d'optimisation identifiees

Les allocations memoire sont minimisees dans la boucle principale (`ArrayPool` pour les buffers de telemetrie).

### SourceAnalyzerService

Analyse statique du code source C# via Roslyn. Calcule pour chaque methode :

- **Complexite cyclomatique** (CC) -- mesure de la complexite structurelle
- **Metriques de Halstead** -- Volume (V), Difficulte (D), Effort (E)
- **Indice de maintenabilite** -- score composite de la facilite de maintenance

### StateValidatorService (Digital Twin)

Valide que les modifications de code proposees par l'IA ne degradent pas la qualite. Regle cardinale : la complexite cyclomatique ne doit jamais augmenter apres application d'un patch. Ce service agit comme un jumeau numerique qui simule l'impact de chaque modification avant sa mise en oeuvre.

### CodePatcherService

Gere le cycle de vie complet des patches automatises :

- `GetPendingProposals()` -- liste les patches en attente d'approbation
- `ApproveAndApplyAsync()` -- applique un patch valide apres approbation humaine
- `RejectProposalAsync()` -- rejette une proposition de patch

Le mode audit (actif par defaut en configuration entreprise) journalise les propositions sans les appliquer. Avant toute ecriture sur le disque, un snapshot de rollback est obligatoirement cree.

---

## Telemetrie et supervision

### Journalisation structuree

Tous les services journalisent via `LoggingService`, appuye par Serilog avec des fichiers rotatifs quotidiens stockes dans `%APPDATA%/Coclico/logs/`. Chaque bloc `catch` inclut une journalisation d'exception structuree avec le nom de la classe et de la methode d'origine. Les methodes critiques (patching, analyse AST, changement de priorite de processus, operations memoire, execution de chaines de flux) incluent des traces d'entree et de sortie.

### Journal d'audit

`AuditLogService` ecrit des entrees NDJSON en ajout seul dans `%APPDATA%/Coclico/audit/`. Chaque entree contient :

- Horodatage, type d'action, acteur (utilisateur ou moteur d'optimisation)
- `AiDecisionContext` pour les decisions pilotees par le LLM (hash du prompt, resume de la reponse, score de confiance)
- Elagage automatique des entrees anciennes selon `AuditRetentionDays` (configurable)

### Telemetrie runtime

`DynamicTracerService` fournit :

- Suivi de spans d'operations (debut, fin, duree, succes ou echec)
- Collecte de metriques avec estimation de quantiles P2 (P50, P95, P99) via l'algorithme TDigest
- Detection d'anomalies via EWMA (Exponentially Weighted Moving Average) et Isolation Forest

### Supervision des ressources

`ResourceGuardService` surveille en continu le processus de l'application lui-meme :

- Suivi du CPU, du working set et de la memoire privee
- Classification du niveau de pression en 4 paliers : Normal, Elevated, High, Critical
- Declenchement automatique du ramasse-miettes et reduction du working set lorsque la pression augmente

---

## Modele de securite

Coclico applique un modele de securite multicouche concu pour un fonctionnement entierement local et transparent.

- **Zero Cloud** -- tout traitement et stockage de donnees est local. Aucune telemetrie, aucune analytique, aucune donnee n'est transmise a l'exterieur. L'application n'effectue aucun appel reseau sortant lie a son fonctionnement.

- **Securite de rollback** -- toute modification de fichier declenchee par le moteur IA est precedee d'un snapshot chiffre via DPAPI par l'intermediaire de `IRollbackService`. La restauration est possible a tout moment.

- **Porte du jumeau numerique** -- les patches de code proposes par l'IA doivent passer la validation de `IStateValidator`. La complexite cyclomatique ne doit jamais augmenter. Tout patch echouant a cette verification est automatiquement rejete.

- **Mode audit seul** -- le parametre `CodePatcherAuditOnly` (active par defaut) journalise les propositions de patches IA sans les appliquer. Un operateur humain doit explicitement approuver chaque modification avant son application.

- **Politique de securite** -- `ISecurityPolicy` charge et applique des regles configurables depuis `%APPDATA%/Coclico/security-policy.json`. Ces regles sont fusionnees avec des valeurs par defaut codees en dur et non-suppressibles, garantissant un socle de securite minimal en toutes circonstances.

- **Journal d'audit** -- piste d'audit NDJSON en ajout seul, incluant le contexte de chaque decision IA, pour une tracabilite complete et inalterable.

---

## Prerequis systeme

| Exigence | Minimum | Recommande |
|----------|---------|------------|
| Systeme d'exploitation | Windows 10 version 22H2 | Windows 11 |
| SDK .NET | 10.0 ou superieur | 10.0 ou superieur |
| RAM | 4 Go | 8 Go (pour l'IA locale) |
| Espace disque | 200 Mo (plus le fichier modele LLM) | 500 Mo |
| Privileges | Administrateur (elevation UAC imposee au demarrage) | -- |
| GPU (optionnel) | Compatible Vulkan | GPU dedie avec pilotes Vulkan a jour |

---

## Installation et compilation

### Telechargement direct

La derniere version est disponible sur la page des releases :
**[Telecharger Coclico v1.0.4](https://github.com/Coclico-cy/Coclico/releases/latest)**

### Compiler depuis les sources

```bash
git clone https://github.com/Coclico-cy/Coclico.git
cd Coclico
dotnet restore
dotnet build Coclico/Coclico.csproj
dotnet run --project Coclico/Coclico.csproj
```

L'application exige des privileges administrateur. Si le processus n'est pas eleve, une invite UAC sera presentee automatiquement au demarrage.

### Executer les tests

```bash
dotnet test
```

Pour executer un sous-ensemble de tests specifique :

```bash
dotnet test --filter "FullyQualifiedName~ServiceCore"
```

La suite de tests couvre plus de 25 fichiers de tests, englobant les services principaux, les algorithmes, les ViewModels, les convertisseurs, les modeles, l'execution des chaines de flux et les tests d'integration.

### Configuration du backend IA

Par defaut, le backend CPU est utilise. Pour activer l'acceleration GPU via Vulkan, remplacer la reference NuGet dans le fichier projet :

```xml
<!-- Remplacer -->
<PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.26.0" />
<!-- Par -->
<PackageReference Include="LLamaSharp.Backend.Vulkan" Version="0.26.0" />
```

L'application detecte automatiquement la disponibilite du GPU et retombe sur le CPU en cas d'echec.

---

## Arborescence du projet

```
Coclico/
    App.xaml.cs                  Point d'entree, enregistrement DI, elevation UAC
    MainWindow.xaml.cs           Fenetre de navigation principale
    Converters/                  Convertisseurs de valeurs WPF (6 fichiers)
        BooleanToVisibilityInvertedConverter.cs
        EqualityToBooleanConverter.cs
        FileIconToImageSourceConverter.cs
        HexToBrushConverter.cs
        ObjectEqualsConverter.cs
        StringFirstCharConverter.cs
    Models/                      Modeles de domaine
        LaunchMode.cs            Modes de demarrage de l'application
        WorkflowPipeline.cs      Definition des chaines de flux
    Services/                    Logique metier (52+ fichiers)
        I*.cs                    Contrats de services (12 interfaces)
        *Service.cs              Implementations de services
        Algorithms/              Algorithmes specialises
            IsolationForestDetector.cs   Detection d'anomalies
            MethodReplacer.cs            Remplacement AST
            SimpleStemmer.cs             Racinisation pour RAG
            TDigest.cs                   Estimation de quantiles
        SecurityHelpers.cs       Validation et assainissement des entrees
        SeverityClassifier.cs    Classification de severite des menaces
        ServiceContainer.cs      Wrapper DI (lazy, thread-safe)
    ViewModels/                  ViewModels MVVM (5 fichiers)
        CleaningViewModel.cs
        DashboardViewModel.cs
        ScannerViewModel.cs
        SettingsViewModel.cs
        WorkflowPipelinesViewModel.cs
    Views/                       Vues WPF et code-behind (17 fichiers)
        DashboardView.xaml       Tableau de bord
        ProgramsView.xaml        Gestionnaire d'applications
        WorkflowPipelinesView.xaml  Editeur de chaines de flux
        InstallerView.xaml       Installeur rapide
        CleaningView.xaml        Nettoyage
        ScannerView.xaml         Scanner
        RamCleanerView.xaml      Nettoyage memoire
        AiChatView.xaml          Assistant IA
        SettingsView.xaml        Parametres
        HelpView.xaml            Aide
        SplashWindow.xaml        Ecran de demarrage
        LauncherWindow.xaml      Configuration initiale
        ProfileWindow.xaml       Profil utilisateur
        ...
    Resources/
        Lang/                    Dictionnaires de localisation
            de.xaml              Allemand
            en.xaml              Anglais
            es.xaml              Espagnol
            fr.xaml              Francais
        icone/                   Icones de l'application
    resource/
        docs/                    Documentation d'aide embarquee (11 fichiers)
        model/                   Fichier modele LLM (format GGUF)
Coclico.Tests/                   Suite de tests xUnit + Moq (26 fichiers de tests)
website/                         Source du site web (Astro + Tailwind CSS)
docs/                            Site construit (GitHub Pages)
```

---

## Site web et documentation

Le site officiel de Coclico est construit avec Astro et Tailwind CSS, et heberge via GitHub Pages :

**[https://coclico-cy.github.io/Coclico/](https://coclico-cy.github.io/Coclico/)**

Le site est disponible en quatre langues (francais, anglais, allemand, espagnol) et comprend les sections suivantes :

- **Accueil** -- presentation du projet et telechargement
- **Documentation** -- reference technique detaillee
- **Guide** -- tutoriels pas a pas pour chaque module
- **Wiki** -- articles approfondis sur l'architecture et les concepts
- **FAQ** -- questions frequentes
- **Nouveautes** -- journal des modifications et notes de version

La documentation d'aide embarquee dans l'application (accessible via le module Aide) couvre 11 rubriques : tableau de bord, applications, chaines de flux, installeur, nettoyage, scanner, RAM cleaner, parametres, et documentation generale.

---

## Contribuer

Les contributions sont les bienvenues. Pour contribuer :

1. Forker le depot sur [GitHub](https://github.com/Coclico-cy/Coclico)
2. Creer une branche a partir de `main`
3. Apporter vos modifications en respectant les conventions du projet
4. S'assurer que les tests passent (`dotnet test`)
5. Soumettre une pull request avec une description claire des changements

### Conventions de code

- Classes et interfaces en `PascalCase` ; champs prives en `_camelCase`
- Methodes asynchrones suffixees par `Async`
- Injection par constructeur obligatoire, jamais de service-location dans un constructeur
- Services d'arriere-plan : toujours utiliser `ConfigureAwait(false)`
- Journaliser via `LoggingService.LogInfo` et `LoggingService.LogException`
- Ne jamais avaler une exception sans la journaliser
- Utiliser des objets `Result` pour les operations systeme plutot que des exceptions nues
- Aucun token, chemin d'acces ou secret code en dur

---

## Licence

Ce projet est distribue sous licence MIT. Voir le fichier [LICENSE](LICENSE) pour les details complets.

© 2026 Coclico-cy — Open Source.

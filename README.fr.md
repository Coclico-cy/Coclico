# 🏖️ Coclico — Plateforme Enterprise de Gestion Windows

![Version](https://img.shields.io/badge/Version-1.0.3%20Beta-blue)
![Licence](https://img.shields.io/badge/Licence-Propriétaire-red)
![Plateforme](https://img.shields.io/badge/Plateforme-Windows%2010%2B%20%26%20Windows%2011-0078d4)
![Construit avec](https://img.shields.io/badge/Construit%20avec-.NET%2010%20%26%20WPF-512bd4)

> **Supervision. Automatisation. Nettoyage. Confidentialité.**
> 
> Suite unifiée de gestion système Windows avec 8 modules puissants, IA locale, automatisations sécurisées et zéro dépendance cloud.

---

## 🎯 Démarrage Rapide

### Prérequis Système
- **OS**: Windows 10 (v2004+) ou Windows 11
- **.NET**: 10.0 ou supérieur
- **RAM**: 512 Mo minimum
- **Droits Admin**: Requis pour les opérations système (élévation UAC au lancement)

### Installation
1. Téléchargez la dernière version depuis [Releases](https://github.com/YOUR_GITHUB_USERNAME/Coclico/releases)
2. Extrayez `Coclico-setup.exe`
3. Exécutez l'installateur (admin requis)
4. Lancez Coclico depuis le menu Démarrer

### Compiler depuis la Source
```bash
# Cloner le dépôt
git clone https://github.com/YOUR_GITHUB_USERNAME/Coclico.git
cd Coclico

# Compiler
dotnet build Coclico.slnx -c Release

# Exécuter (admin requis)
dotnet run --project Coclico/Coclico.csproj

# Exécuter les tests
dotnet test Coclico.Tests/Coclico.Tests.csproj

# Publier (crée un .exe autonome)
dotnet publish Coclico/Coclico.csproj -c Release -r win-x64 --self-contained
```

---

## 📦 8 Modules Principaux

### 1. **Tableau de Bord** 📊
Supervision système en temps réel (mise à jour toutes les 3 secondes) :
- Utilisation CPU, RAM (utilisée/totale), espace C:
- Uptime Windows, processus actifs, nombre d'apps installées
- Vue d'ensemble rapide de la santé du système

### 2. **Applications** 🎮
Bibliothèque logiciels complète provenant de 8 sources :
- Registre Windows (HKLM + HKCU)
- Steam, Epic Games, GOG, Ubisoft, EA App, Rockstar, Microsoft Store
- Lancer, renommer, catégoriser, ajouter des fichiers .exe personnalisés
- Catégories et groupes personnalisés persistants

### 3. **Flow Chains** ⚙️
Éditeur visuel d'automatisation avec 30+ types de nœuds :
- Constructeur de flux drag-and-drop
- Nœuds : Start/End, OpenApp, CloseApp, RunCommand, KillProcess, Delay, Condition, Loop, Parallel, Notification, HttpRequest, FileOperation, etc.
- Raccourcis clavier globaux (Ctrl+Touche, Ctrl+Alt+Touche)
- Retry & timeout par nœud
- Stockage JSON dans `%AppData%\Coclico\`

### 4. **Installeur** 📥
Interface Winget avec catalogue logiciels curé :
- Catégories : Internet, Runtimes, Développement, Gaming, Création, Système
- Installation, mise à jour, recherche, réparation automatique des sources Winget
- 100+ applications pré-configurées

### 5. **Nettoyeur Système** 🧹
Moteur de nettoyage professionnel avec 10 catégories :
- Fichiers temp Windows, caches navigateurs (Chrome/Firefox/Edge)
- Logs système, corbeille, temp utilisateur, miniatures
- Rapports d'erreur Windows, anciens installeurs, cache DNS, prefetch
- Pré-scan d'estimation, sélection par catégorie, rapport final avec espace libéré

### 6. **Scanner** 🔍
Audit complet des applications installées :
- Détecte toutes les apps de toutes les sources
- Affichage : version, éditeur, taille, chemin d'installation, source
- Format prêt pour export

### 7. **Nettoyeur RAM** 💾
Gestion et surveillance de la mémoire :
- Suivi en temps réel RAM physique, RAM virtuelle, fichier d'échange
- 9 profils de nettoyage + extras
- Nettoyage manuel ou automatique (par intervalle ou seuil %)

### 8. **Paramètres** ⚙️️
Suite complète de personnalisation :
- Sélecteur couleur d'accent (#RRGGBB), thème, opacité cartes, taille police
- Largeur sidebar, mode compact, langue (FR/EN/DE/ES/IT/JA/KO/PT/RU/ZH)
- Intégration démarrage Windows, minimiser dans la barre système
- Configuration raccourcis clavier globaux
- Sauvegardé dans `%AppData%\Coclico\settings.json`

---

## 🤖 Assistant IA Coclico

IA conversationnelle intégrée (100% local, zéro cloud) :
- Modèle : Inférence locale GGUF via LLamaSharp
- Fonctionnalités : Q&A, tutoriels, conseils étape-par-étape, commandes d'action
- Confidentialité : Pas d'internet, pas de télémétrie, tout traitement local
- Contexte : Documentation intégrée avec RAG (Retrieval-Augmented Generation)

**Fichier modèle** (non inclus dans le repo, ~5GB) :
```
resource/model/IA-support-chat.gguf
```
Téléchargez depuis votre fournisseur de modèles et placez dans `resource/model/` pour activer l'IA.

---

## 🏗️ Architecture

### Stack Technologique
- **Framework UI** : WPF + WPF-UI 4.2.0 (Fluent Design)
- **Langage** : C# avec .NET 10, LangVersion=preview
- **MVVM** : CommunityToolkit.Mvvm (ObservableObject, RelayCommand, générateurs sources)
- **IA** : LLamaSharp 0.26.0 avec backend CPU
- **Logging** : Serilog → `%AppData%\Coclico\logs\` (rotation quotidienne, rétention 14 jours)
- **Tests** : xUnit + Moq

### Structure du Projet
```
Coclico/
├── Coclico.csproj              # App WPF principale (.NET 10)
├── Coclico.Tests.csproj        # Suite test xUnit
├── Views/                        # XAML + code-behind
├── ViewModels/                   # ViewModels MVVM
├── Services/                     # Logique métier (singletons & transients)
├── Models/                       # Modèles données (FlowChain, LaunchMode, etc.)
├── Converters/                   # Convertisseurs valeurs XAML
├── Resources/
│   ├── Lang/                    # ResourceDictionaries WPF (fr.xaml, en.xaml, etc.)
│   ├── software_list.json       # Catalogue installeur
│   └── icone/                   # Icônes application
├── resource/
│   ├── docs/                    # Documentation Markdown
│   └── model/                   # Modèle IA GGUF (placer ici)
│
tests/
├── Coclico.Tests.csproj
├── ServiceCoreTests.cs
├── FeatureExecutionEngineTests.cs
├── RagServiceTests.cs
└── ... (15+ fichiers test)

docs/                           # Documentation web (HTML statique + i18n JSON)
├── index.html
├── docs.html
├── en.json, fr.json, de.json, ... (10 langues)
├── script.js
└── style.css
```

### Injection de Dépendances
Motif `ServiceContainer` personnalisé (enveloppe `Microsoft.Extensions.DependencyInjection`) :

**Singletons** (statique Instance + DI) :
- `SettingsService`, `ThemeService`, `LocalizationService`
- `AiChatService`, `NetworkMonitorService`, `ProcessWatcherService`
- `AppResourceGuardService`, `KeyboardShortcutsService`
- `FeatureExecutionEngine`, `FlowExecutionService`, `ProfileService`
- `InstalledProgramsService`

**Transients** (DI uniquement) :
- `CleaningService`, `DeepCleaningService`, `InstallerService`
- `FlowChainService`, `FlowChainExecutionService`
- `StartupHealthService`, `UserAccountService`

---

## 🔄 Système Auto-Update

Coclico inclut `UpdateManager.cs` pour l'intégration GitHub Releases :

```csharp
var updateMgr = new UpdateManager(logger, settingsService);

// Vérifier les mises à jour
var latest = await updateMgr.CheckForUpdatesAsync("1.0.3", cancellationToken);
if (latest != null)
{
    // Télécharger et installer
    await updateMgr.DownloadReleaseAsync(latest, "Coclico_Setup.exe", "./downloads/", ct);
    updateMgr.LaunchInstaller("./downloads/Coclico_Setup.exe");
}
```

**Configuration** (dans `UpdateManager.cs`) :
```csharp
private const string GitHubOwner = "YOUR_GITHUB_USERNAME";
private const string GitHubRepo = "Coclico";
```

---

## 🌐 Localisation

### Langues Supportées
- 🇫🇷 Français
- 🇬🇧 English
- 🇩🇪 Deutsch
- 🇪🇸 Español
- 🇮🇹 Italiano
- 🇯🇵 日本語
- 🇰🇷 한국어
- 🇵🇹 Português
- 🇷🇺 Русский
- 🇨🇳 中文

### Fichiers de Traduction
- **UI App** : `Coclico/Resources/Lang/{lang}.xaml` (ResourceDictionaries WPF)
- **Docs Web** : `docs/{lang}.json` (i18n JSON)

Changez la langue dans Paramètres → rafraîchissement UI instantané.

---

## 📝 Flow Chains — 30+ Types de Nœuds

Nœuds d'automatisation supportés :
```
Start, End, OpenApp, CloseApp, RunCommand, KillProcess, Delay, 
Condition, Loop, Parallel, Notification, HttpRequest, FileOperation, 
SystemCheck, RunPowerShell, OpenUrl, SetVolume, MuteAudio, 
SetProcessPriority, KillByMemory, CleanTemp, RamClean, ServiceStart, 
ServiceStop, RegistrySet, ClipboardSet, Screenshot, SendKeys, 
CompressFile, MonitorWait, LogMessage, SetEnvVar, CheckInternet, 
PlaySound, FocusWindow, RenameFile, EmptyRecycleBin, WakeOnLan
```

---

## 🔐 Sécurité & Confidentialité

✅ **Traitement 100% Local**
- Aucun service cloud, pas de télémétrie
- Toutes les données dans `%AppData%\Coclico\` (JSON)
- Élévation admin uniquement quand nécessaire (UAC)

✅ **Outils Sécurité Intégrés**
- Scan de secrets (détection basée regex)
- Audit système & vérification apps
- Gestion sécurisée des processus

✅ **Qualité Code**
- Suite test xUnit (15+ fichiers test)
- Logging complet Serilog
- Gestion d'erreurs avec dégradation gracieuse

---

## 📊 Performance

### Optimisations
- **.NET 10 PGO (Tiered)** : Optimisation guidée par profil
- **GC Concurrent** : Garbage collection parallèle
- **Task Parallel** : Opérations multi-thread (UI responsive)
- **Lazy Initialization** : Services chargés à la demande

### Empreinte Mémoire
- ~150-200 MB baseline (WPF + modèle IA au chargement)
- ~500 MB avec modèle IA complet en mémoire

---

## 🐛 Dépannage

### Problèmes Courants

**« Admin requis »**
→ Coclico nécessite les droits administrateur. Clic droit → Exécuter en tant qu'administrateur

**Chat IA ne fonctionne pas**
→ Téléchargez le modèle GGUF et placez dans `resource/model/IA-support-chat.gguf`

**Erreurs Winget**
→ Assurez-vous qu'« App Installer » est installé depuis le Microsoft Store (Windows 10 21H2+, auto sur Win11)

**Erreurs compilation (.NET 10)**
→ Installez .NET 10 SDK : https://dotnet.microsoft.com/download

---

## 🧪 Tests

```bash
# Exécuter tous les tests
dotnet test Coclico.Tests/Coclico.Tests.csproj

# Exécuter par catégorie
dotnet test Coclico.Tests/Coclico.Tests.csproj --filter "FullyQualifiedName~ServiceCoreTests"

# Exécuter un seul test
dotnet test Coclico.Tests/Coclico.Tests.csproj --filter "FullyQualifiedName~ServiceCoreTests.TestMethod"

# Avec couverture
dotnet test Coclico.Tests/ --collect:"XPlat Code Coverage"
```

---

## 📜 Licence

**Licence Propriétaire** — © Coclico 2024-2026  
Tous droits réservés. Reproduction non autorisée.

Pour les demandes de licence : [coordonnées]

---

## 🤝 Contribution

Ceci est un **projet commercial à source fermée**. Les contributions externes ne sont pas acceptées actuellement.

Pour les rapports de bugs ou demandes de fonctionnalités, veuillez contacter l'équipe de développement.

---

## 🚀 Notes de Publication

### v1.0.3 Beta
- ✅ Suite complète 8 modules
- ✅ Assistant IA local
- ✅ 30+ nœuds Flow Chains
- ✅ Système auto-update (GitHub)
- ✅ Support 10 langues
- ⚠️ Fichier modèle GGUF requis pour l'IA (téléchargement externe)

### Roadmap
- [ ] Sync cloud (optionnel, chiffré)
- [ ] SDK nœuds personnalisés
- [ ] Profileur performance
- [ ] Support natif ARM64
- [ ] Distribution Microsoft Store

---

## 📚 Documentation

- **Guide Utilisateur** : [Lancer → Aide → Guide](docs/index.html)
- **Wiki Développeur** : [docs/wiki.html](docs/wiki.html)
- **FAQ** : [docs/faq.html](docs/faq.html)
- **Docs API** : Voir [CLAUDE.md](CLAUDE.md) pour architecture & commandes build

---

## 💬 Support

- 🐛 **Rapports Bugs** : [GitHub Issues](https://github.com/YOUR_GITHUB_USERNAME/Coclico/issues)
- 📧 **Email** : support@coclico.dev (placeholder)
- 💬 **Discord** : [Serveur Communauté](https://discord.gg/your-invite)

---

**Fait avec ❤️ par l'équipe Coclico**

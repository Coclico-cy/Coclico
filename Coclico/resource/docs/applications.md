# Module Applications Coclico

Le module Applications est la bibliothèque centrale de tous les logiciels et jeux installés sur le PC. Il consolide automatiquement toutes les sources d'installation en une vue unifiée, interactive et filtrable.

## Sources détectées automatiquement

### Registre Windows (source principale)
- **HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall** : applications installées pour tous les utilisateurs (installation classique avec droits admin)
- **HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall** : applications installées pour l'utilisateur courant uniquement
- **HKLM\SOFTWARE\WOW6432Node\...** : applications 32 bits sur Windows 64 bits

### Plateformes de jeux
- **Steam** : bibliothèque complète de jeux détectée via les fichiers .acf dans steamapps\. Lit le nom du jeu (DisplayName), le chemin d'installation (installdir) et construit automatiquement le chemin de l'exécutable
- **Epic Games** : jeux détectés via les fichiers manifest JSON du Epic Games Store. Extrait le nom (DisplayName), le chemin d'installation (InstallLocation) et l'exécutable (LaunchExecutable)
- **GOG** : jeux GOG Galaxy détectés via le registre Windows
- **Microsoft Store** : applications du Windows Store

## Informations affichées par application
- **Nom** : nom d'affichage de l'application (DisplayName)
- **Éditeur** : publisher ou developer
- **Version** : numéro de version installée
- **Source** : Windows / Steam / Epic / GOG (origine de l'installation)
- **Chemin d'installation** : dossier racine où l'application est installée
- **Exécutable** : chemin complet vers le fichier .exe de lancement
- **Taille sur disque** : espace occupé en Mo ou Go
- **Icône** : extraite automatiquement depuis l'exécutable de l'application
- **Catégorie** : Logiciel, Jeu ou autre catégorie personnalisée

## Actions disponibles sur les applications

### Double-clic
Lance directement l'application depuis Coclico sans ouvrir l'explorateur de fichiers.

### Clic droit – Menu contextuel
- **Renommer** : modifier le nom d'affichage de l'entrée (sans toucher au fichier)
- **Changer la catégorie** : attribuer une catégorie personnalisée (Logiciel, Jeu, Travail, etc.)
- **Copier le chemin** : copier le chemin de l'exécutable dans le presse-papier Windows

## Filtrage et recherche
- **Filtre par catégorie** : afficher uniquement les Logiciels, les Jeux, ou toutes les catégories
- **Barre de recherche** : filtrage en temps réel par nom d'application
- Filtrages combinables : catégorie ET nom simultanément

## Filtrage intelligent automatique – Blacklist
Coclico applique un filtre intelligent pour ne pas afficher les entrées techniques non pertinentes qui encombrerait la liste. Sont automatiquement exclus :
- **Drivers et pilotes** : tous les drivers matériels
- **Redistributables** : VCRedist, DirectX, .NET Runtime, OpenAL, OpenAL Soft...
- **Frameworks et SDK** : Java JRE, Visual Studio Build Tools, Python launcher...
- **Outils système** : agents de mise à jour, bootstrappers, diagnostics, verifiers
- **Anti-cheat** : EasyAntiCheat, BattlEye, FACEIT AC (composants cachés)
- **Composants internes** : DLLs, plugins, composants de jeux (Steam Common Redist, etc.)

Résultat : seules les vraies applications et jeux utilisables sont affichés.

## Performance et cache
- La liste des applications est chargée au démarrage de Coclico en arrière-plan (async)
- Un cache mémoire (memory cache) accélère les accès ultérieurs
- Les icônes sont pré-chargées en parallèle au démarrage pour un affichage instantané
- La liste peut être rechargée/invalidée si nécessaire

## Différence entre Applications et Scanner
- **Applications** : vue conviviale pour le quotidien. Icônes, filtres, actions rapides, lancement direct. Interface optimisée pour l'utilisation courante.
- **Scanner** : audit technique exhaustif. Affiche tout sans filtre de convivialité, orienté inventaire et diagnostic.

# Module Scanner Coclico

Le Scanner est l'outil d'audit, de diagnostic et d'inventaire exhaustif des applications installées sur le PC. Il effectue une analyse complète de toutes les sources logicielles pour produire un inventaire technique détaillé.

## Comment utiliser le Scanner
1. Accéder au module **Scanner** depuis la barre latérale de Coclico
2. Cliquer sur le bouton **"Analyser"** pour démarrer le scan
3. Le statut affiche : "Analyse en cours…" pendant l'opération
4. Une fois terminé : "Analyse terminée — X application(s) trouvée(s)." avec le nombre exact
5. La liste complète des applications s'affiche avec toutes leurs informations

## Sources analysées par le Scanner

### Registre Windows – Source principale
- **HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall** : applications installées pour tous les utilisateurs (installation standard avec admin)
- **HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall** : applications installées pour l'utilisateur courant uniquement
- **HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall** : applications 32 bits sur système 64 bits
- Lit : DisplayName, DisplayVersion, Publisher, InstallLocation, UninstallString, EstimatedSize

### Steam
- Détection automatique de la bibliothèque Steam via les fichiers .acf dans steamapps\
- Utilise des expressions régulières pour extraire : nom du jeu (DisplayName), chemin d'installation (installdir)
- Construit automatiquement le chemin vers l'exécutable principal du jeu

### Epic Games Store
- Détection via les fichiers manifest JSON dans le dossier LauncherInstalled d'Epic
- Extrait : DisplayName (nom du jeu), InstallLocation (chemin), LaunchExecutable (exécutable)

### GOG Galaxy
- Détection des jeux GOG via le registre Windows et les données GOG

### Microsoft Store
- Applications installées depuis le Windows Store

## Informations affichées par application dans le Scanner
- **Nom** : nom d'affichage complet (DisplayName)
- **Version** : numéro de version installée (DisplayVersion)
- **Éditeur** : publisher / developer / studio
- **Chemin d'installation** : dossier racine de l'installation (InstallLocation)
- **Source** : Windows / Steam / Epic / GOG (d'où vient l'installation)
- **Taille sur disque** : espace occupé en Mo ou Go (EstimatedSize ou calculé)

## Filtrage et performances

### Filtre de blacklist intelligent
Le Scanner utilise un filtre de blacklist pour exclure les entrées non pertinentes :
- Drivers, pilotes matériels
- Redistributables (VCRedist, DirectX, OpenAL, .NET...)
- Frameworks, SDK, Build Tools
- Agents de mise à jour, bootstrappers
- Outils de diagnostic internes (Application Verifier, ISCI Initiator...)
- Composants anti-cheat et DLL helpers
Seules les vraies applications utilisables sont présentées.

### Détection des jeux connus
Coclico dispose d'une liste de jeux connus (Genshin Impact, Valorant, League of Legends, Overwatch, Minecraft, Roblox, Dota, Warcraft...) pour les catégoriser automatiquement comme "Jeu".

### Performance
- L'analyse est entièrement asynchrone (non-bloquante pour l'interface)
- Cache mémoire : les résultats sont mis en cache pour accélérer les accès ultérieurs
- InvalidateMemoryCache() permet de forcer une nouvelle analyse complète
- Les icônes des applications sont pré-chargées en parallèle pour un affichage plus rapide

## Différence Scanner vs module Applications

| Critère | Scanner | Applications |
|---------|---------|--------------|
| Objectif | Audit/inventaire technique | Usage quotidien |
| Affichage | Données brutes et techniques | Interface conviviale avec icônes |
| Filtrage | Minimal (audit complet) | Filtres avancés + blacklist |
| Actions | Consultation | Lancer, renommer, catégoriser |
| Idéal pour | Diagnostic, inventaire | Lancer ses applications |

## Cas d'usage typiques
- **Inventaire avant réinstallation** : lister tous les logiciels à réinstaller
- **Audit de versions** : vérifier quelle version précise est installée
- **Détection de logiciels indésirables** : trouver des applications inconnues ou non désirées
- **Audit espace disque** : identifier les applications les plus volumineuses
- **Diagnostic environnement** : fournir un inventaire à un technicien ou administrateur système
- **Doublons et obsolètes** : détecter des logiciels en double ou très anciens

## Résultat type d'un scan
Un scan typique sur un PC moyen détecte entre 50 et 200 entrées selon le nombre de logiciels et jeux installés. Le scanner est plus exhaustif que le module Applications car il inclut certaines entrées utiles pour l'audit qui seraient filtrées dans la vue Applications.

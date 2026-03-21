# Module Installeur Coclico

L'Installeur est une interface graphique moderne pour Winget (Windows Package Manager). Il permet d'installer, rechercher, mettre à jour et gérer des logiciels sans jamais ouvrir un terminal.

## Qu'est-ce que Winget ?
Winget est le gestionnaire de packages officiel de Microsoft, intégré nativement dans Windows 10 (version 2004+) et Windows 11. Il permet d'installer des milliers de logiciels en une seule commande. Coclico fournit une interface conviviale par-dessus Winget pour rendre son usage accessible à tous.

## Fonctionnalités de l'Installeur

### Catalogue de logiciels curé
Un catalogue organisé par catégories présente les logiciels les plus populaires. Chaque logiciel dispose d'un nom, d'une description et peut être installé en un seul clic.

### Recherche Winget
Rechercher n'importe quel logiciel disponible dans le registre Winget (plus de 6000 packages disponibles) par son nom ou son identifiant Winget.

### Installation en un clic
Sélectionner un logiciel dans le catalogue ou les résultats de recherche et cliquer sur "Installer". Coclico lance Winget en arrière-plan et affiche la progression.

### Mise à jour de logiciels
Mettre à jour les packages Winget installés vers leur dernière version disponible.

### Réparation automatique des sources Winget
Si Winget rencontre des erreurs (sources inaccessibles, index corrompu), Coclico peut les réinitialiser automatiquement avec la commande `winget source reset --force`. Utile quand Winget est cassé ou refuse de se connecter.

## Catégories du catalogue

### Internet
Navigateurs : Google Chrome, Mozilla Firefox, Brave Browser, Microsoft Edge
Outils réseau : PuTTY, WinSCP, FileZilla
VPN : OpenVPN, ProtonVPN
Clients mail : Thunderbird

### Runtimes (environnements d'exécution)
.NET Runtime et SDK (toutes versions)
Java JDK / JRE (OpenJDK, Oracle)
Visual C++ Redistributable (2015-2022)
DirectX End-User Runtime
Microsoft Visual J#, WebView2

### Développement
Éditeurs : Visual Studio Code, Visual Studio Community
Outils : Git, Windows Terminal, PowerShell 7
Langages : Node.js, Python, Go, Rust
Conteneurs : Docker Desktop
API : Postman, Insomnia
Bases de données : DBeaver, TablePlus

### Gaming
Plateformes : Steam, Epic Games Launcher, GOG Galaxy, Battle.net, Xbox App
Voix : Discord, TeamSpeak
Jeux : Minecraft Launcher, Roblox

### Création
3D : Blender
Image : GIMP, Inkscape, Krita, Paint.NET
Vidéo : OBS Studio, DaVinci Resolve, HandBrake, Kdenlive, VLC
Audio : Audacity, LMMS, Spotify
Streaming : Streamlabs OBS

### Système
Archiveurs : 7-Zip, WinRAR
Recherche : Everything (recherche instantanée de fichiers)
Monitoring : CPU-Z, HWiNFO64, GPU-Z, CrystalDiskInfo
Gestion processus : Process Hacker 2 / System Informer
Désinstallation : Revo Uninstaller
Espace disque : TreeSize Free, WinDirStat
Utilitaires : BleachBit, Rufus, Ventoy, Bulk Rename Utility

## Paramètre : Portée d'installation Winget
Accessible dans Paramètres → portée d'installation :
- **machine** (par défaut) : installe pour tous les utilisateurs du PC. Nécessite une élévation UAC (droits administrateur). L'application est disponible pour tous les profils Windows.
- **user** : installe uniquement pour le compte Windows courant. Pas besoin de droits admin. Installation dans %LocalAppData%.

## Format du catalogue (software_list.json)
Le catalogue est chargé depuis `Resources/software_list.json`. Format JSON structuré :
```json
[{ "Category": "Développement", "Items": [{ "Name": "VS Code", "WingetId": "Microsoft.VisualStudioCode", "Description": "Éditeur de code", "CustomCommand": "" }] }]
```
Le champ `CustomCommand` permet de définir une commande personnalisée pour les logiciels non disponibles via Winget (installation manuelle, script, etc.).

## Prérequis
Winget doit être disponible sur le système. Il est inclus nativement dans Windows 10 v2004+ et Windows 11. Si Winget est absent, l'utiliser depuis le Microsoft Store (App Installer) ou le télécharger sur GitHub (microsoft/winget-cli).

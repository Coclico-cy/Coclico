<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Icône de Coclico" width="88" />

# Coclico

### Une application Windows pour mieux suivre et entretenir son ordinateur

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Plateforme](https://img.shields.io/badge/plateforme-Windows%2011-0078D4?logo=windows11&logoColor=white)](#configuration-requise)
[![Licence : MIT](https://img.shields.io/badge/licence-MIT-green.svg)](LICENSE.txt)


[English](README.md) · [Français](README_FR.md) · [Dev (EN)](READMEDEV.md) · [Dev (FR)](READMEDEV_FR.md)

</div>

---

## Présentation

Coclico est une application de bureau Windows en WPF qui regroupe des informations système et des outils de maintenance dans une seule interface moderne. Le projet cherche à rendre les opérations courantes plus faciles à trouver et à comprendre, avec une confirmation **avant** les actions qui peuvent modifier l’ordinateur.

> **Un projet solo, vivant.** Coclico est développé par une seule personne, sur du temps libre. Le projet continue d’avancer avec le temps, mais les mises à jour peuvent être espacées selon le temps disponible : pas d’inquiétude, il est bien vivant et il continuera d’évoluer.

## Transparence sur l’usage de l’IA

J’ai créé et je maintiens Coclico seul ; l’orientation du produit et les décisions d’implémentation sont les miennes. J’utilise l’IA de façon transparente comme outil d’aide : pour corriger et bien structurer mes textes en français comme en anglais (je vis avec une dyslexie et une dysorthographie, qui rendent certaines fautes difficiles à repérer), pour rechercher des bugs et des erreurs persistantes, comprendre des problèmes que je ne maîtrise pas encore, et mieux cerner des sujets techniques comme .NET 10. Certaines suggestions de l’IA ont pu contribuer à des changements de code ; je relis et assume le travail intégré au projet. Je ne présente donc pas Coclico comme un projet réalisé sans assistance de l’IA, et je préfère l’assumer clairement.

Au premier lancement, un message de bienvenue présente l’application :

<p align="center">
  <img src="docs/messagede-bievenue.png" alt="Message de bienvenue au premier lancement" width="560">
</p>

L’application démarre en utilisateur standard : les outils qui ont besoin des droits administrateur le demandent au moment voulu, jamais au lancement.

Le menu principal est un accordéon groupé, inspiré des Paramètres Windows : les modules sont rangés en trois sections dépliables (**Maintenance**, **Performance** et **Applications**) qui se souviennent de ce que tu as déplié. Accueil, Paramètres et Aide restent toujours visibles.

| | |
| --- | --- |
| **Sécurité d’abord** | Chaque action destructive demande une confirmation, les fichiers supprimés partent à la Corbeille, et les changements réseau sont protégés par des instantanés restaurables en un clic. |
| **Matériel respecté** | Les plans d’alimentation sont créés uniquement via `powercfg`, chaque paramètre étant validé par rapport à ton PC avant d’être appliqué. |
| **IA locale** | Un modèle GGUF local ou un serveur Ollama local : aucune clé cloud, adresses de boucle locale uniquement. |

Le projet est encore en cours de finition. Certains outils dépendent des droits administrateur, du matériel ou de logiciels installés. Lis la description et la confirmation de chaque action avant de l’appliquer.

## Fonctionnalités détaillées

### Tableau de bord

<p align="center">
  <img src="docs/accueil.png" alt="Tableau de bord de Coclico" width="640">
</p>

L’écran d’accueil donne l’état du système en un coup d’œil (processeur, mémoire et disques), avec des tuiles de raccourcis vers les modules. Trois modes en un clic combinent nettoyage mémoire et nettoyage de fichiers temporaires :

- **Zen** : nettoyage mémoire rapide + fichiers temporaires, pour retrouver un PC léger.
- **Gamer** : purge mémoire profonde avant une session de jeu.
- **Travail** : nettoyage mémoire standard, sans toucher aux fichiers.

Les tuiles affichées sont personnalisables : tu choisis toi-même les raccourcis qui apparaissent sur ton accueil.

### Nettoyage système

<p align="center">
  <img src="docs/Nettoyage-pc.png" alt="Module de nettoyage système" width="640">
</p>

Le module analyse d’abord, supprime ensuite, et toujours avec ta confirmation. Les catégories couvertes :

- Fichiers temporaires de Windows ;
- Corbeille ;
- Cache des miniatures (thumbnails) ;
- Caches des navigateurs ;
- Journaux système ;
- Vidage du cache DNS.

Des préréglages permettent de sélectionner en un clic un jeu de catégories adapté (nettoyage rapide ou complet). Après chaque opération, un résumé indique l’espace libéré et le nombre de fichiers supprimés.

### Santé & Défense

<p align="center">
  <img src="docs/santer-defence.png" alt="Module Santé et Défense" width="640">
</p>

Un point central pour l’état de sécurité de Windows :

- État de Windows Defender et fraîcheur des signatures ;
- **Réparation en un clic** : une suite de vérifications et de réparations, étape par étape, avec possibilité d’annuler ;
- Scan rapide et scan complet Defender ;
- Mise à jour des signatures ;
- Accès à l’outil de suppression de logiciels malveillants (MSRT) et au scan hors ligne de Defender (WDO) ;
- Contrôle de la santé au démarrage de Windows.

### Espace disque

<p align="center">
  <img src="docs/espace-disque.png" alt="Carte de l'espace disque, doublons et gros fichiers" width="640">
</p>

Trois modes complémentaires pour comprendre où passe ton espace :

- **Carte de l’espace** : une carte radiale interactive de n’importe quel lecteur ou dossier. Clic sur une zone pour zoomer, fil d’Ariane pour revenir, liste des zones les plus lourdes, ouverture directe dans l’Explorateur.
- **Doublons** : la recherche se fait en trois phases (taille, puis empreinte des 64 premiers Ko, puis empreinte SHA-256 complète) : seuls les fichiers au contenu *réellement identique* sont listés. Seuil au choix (1, 10, 50 ou 100 Mo), groupes triés par espace récupérable, case à cocher par fichier, bouton « cocher les copies (garder le premier) », et suppression **vers la Corbeille** avec confirmation.
- **Gros fichiers** : le top 50, 100 ou 200 des fichiers les plus volumineux d’un dossier, avec ouverture de l’emplacement et mise à la corbeille fichier par fichier.

### RAM Turbo Cleaner

<p align="center">
  <img src="docs/ram-clean.png" alt="RAM Turbo Cleaner" width="640">
</p>

Moniteur et purge de la mémoire vive :

- Vue en direct de la mémoire physique, de la mémoire virtuelle et du fichier d’échange (libre / total) ;
- Purge manuelle avec plusieurs profils (rapide, normal, profond, intelligent) ;
- Historique et total libéré depuis le début de la session ;
- **Nettoyage automatique intelligent** : par intervalle, par seuil d’utilisation, ou hybride, avec protection de l’application au premier plan pour ne jamais purger ce que tu es en train d’utiliser.

### Optimiseur réseau

<p align="center">
  <img src="docs/optimisateur-reseau.png" alt="Optimiseur réseau" width="640">
</p>

- Liste des cartes réseau avec leur état, ping et débit en direct, et watchdog de connexion ;
- Optimiseur global : DNS, MTU, paramètres TCP (autotuning, RSS, RSC, timestamps, ECN, fournisseur de congestion) et QoS ;
- **Chaque modification est protégée par un instantané** : restauration en un clic si un réglage ne convient pas à ta carte ou à ton pilote ;
- Mesures de performance pour comparer avant/après.

### CPU & Alimentation

<p align="center">
  <img src="docs/optimisateur-pc.png" alt="CPU et plans d'alimentation Coclico" width="640">
</p>

Trois plans d’alimentation Coclico, créés et pilotés **uniquement via powercfg** (aucune écriture registre directe) :

- **Coclico économie** : autonomie maximale. Boost économe, composants en économie d’énergie.
- **Coclico optimal** : réactif sur secteur, économe sur batterie. Compatible avec le mode adaptatif.
- **Coclico performance** : réactivité maximale. Boost agressif, cœurs actifs, aucune mise en veille des composants.

Points clés du module :

- **Auto-configuration selon ton PC** : chaque paramètre (EPP, boost, seuils de fréquence, USB, PCIe, Wi-Fi, écran, disque, veille…) est validé par `powercfg /q` avant d’être appliqué ; ceux que ton matériel ne supporte pas sont ignorés et comptés.
- **Mode optimal adaptatif** : Coclico surveille la charge CPU et bascule tout seul vers « performance » quand la charge est soutenue, vers « économie » quand le PC redevient tranquille, avec seuils, confirmation dans la durée et délai de repos entre deux bascules.
- **Test comparatif** : mesure le débit de calcul du processeur sous chacun des trois plans (environ 15 secondes), affiche les résultats en barres, surligne le meilleur et restaure ton plan de départ.
- Les plans Coclico peuvent être supprimés à tout moment ; Coclico revient alors au plan Équilibré de Windows. Droits administrateur requis.

### Programmes

<p align="center">
  <img src="docs/appplication.png" alt="Liste des applications installées" width="640">
</p>

- Inventaire des applications installées : scan, recherche instantanée, filtres par catégorie, tri, vue grille ou liste ;
- Désinstallation propre via le désinstalleur officiel de chaque application, avec confirmation ;
- Ajout de programmes manquants à la liste.

### Installeur Winget

<p align="center">
  <img src="docs/installateur.png" alt="Installeur Winget" width="640">
</p>

- Catalogue d’applications populaires installables en un clic via **Winget** ;
- Détection automatique des paquets déjà présents sur ta machine (le bouton affiche « Déjà installé ») ;
- Vérification des mises à jour de Coclico depuis GitHub.

### Assistant IA local

<p align="center">
  <img src="docs/IA.png" alt="Assistant IA local" width="640">
</p>

Un assistant intégré, **100 % local**, accessible depuis n’importe quel module via le bouton flottant ou `Ctrl + Espace` :

- Deux moteurs au choix : un modèle **GGUF** exécuté par LLamaSharp (accélération GPU NVIDIA optionnelle, repli CPU automatique), ou un serveur **Ollama** local (adresses `localhost` uniquement, serveurs distants refusés) ;
- Possibilité de joindre des images (vision) ;
- L’assistant sait sur quel module tu te trouves et peut proposer des **actions Coclico** prises en charge : chaque action est présentée et validée avant exécution ;
- Aucune clé API cloud : les fournisseurs cloud et leur coffre de clés ont été supprimés.

### Paramètres et profils

- Langue de l’interface (français / anglais), thème et couleur d’accent, mode compact et taille de police ;
- Démarrage automatique avec Windows et réduction vers la zone de notification ;
- Réglages de l’IA : moteur, modèle, utilisation du GPU et nombre de couches ;
- Profil utilisateur avec avatar (recadrage intégré) ;
- Vérification et installation des mises à jour de Coclico.

Les fonctions disponibles varient selon la version de Windows, les autorisations, les pilotes et les composants installés.

## IA locale et confidentialité

Coclico ne demande, ne stocke et n’utilise aucune clé API d’un fournisseur d’IA tiers. Au premier démarrage de cette version, Coclico supprime également l’ancien coffre chiffré de clés API du profil Windows actuel, s’il existe.

Le téléchargement d’un modèle GGUF nécessite Internet une fois ; l’inférence s’effectue ensuite localement. Les questions et les images jointes sont envoyées au moteur d’inférence local choisi. Coclico accède aussi au réseau pour certaines fonctions, comme la recherche de mises à jour GitHub ou le téléchargement des fichiers de modèles.

Examine les propositions de l’assistant avant de l’autoriser à modifier les paramètres du système. Une réponse d’IA peut être inexacte : elle ne remplace ni ton jugement ni la documentation Windows.

## Configuration requise

- Windows 11, version 22H2 ou ultérieure (build 22621 ou plus récent).
- Aucun runtime .NET n’est à installer séparément pour les versions officielles : le runtime .NET 10 Desktop est inclus.
- Connexion Internet pour rechercher des mises à jour et télécharger un modèle. Un modèle local déjà téléchargé peut ensuite fonctionner hors ligne.
- Facultatif : Ollama installé et démarré localement.
- Facultatif : carte graphique NVIDIA compatible et runtime CUDA 12 pour l’accélération GPU. Le processeur peut être utilisé en solution de repli ; la vitesse dépend de la machine et du modèle.
- Mémoire vive et espace disque suffisants pour le modèle choisi. Les fichiers GGUF sont volumineux et ne sont pas inclus dans ce dépôt.
- Certains modules (plans d’alimentation, installeur, une partie du nettoyage et du réseau) demandent les droits administrateur.

Le dossier de publication Windows x64 actuel pèse environ 1,6 Go avant compression ZIP, car il inclut le runtime CUDA facultatif. Le modèle GGUF se télécharge séparément.

## Télécharger Coclico

Rends-toi sur la page [GitHub Releases](https://github.com/Coclico-cy/Coclico/releases) et télécharge `Coclico-<version>.exe` pour utiliser l’assistant d’installation, ou l’archive ZIP Windows x64 pour une installation portable. L’installateur s’installe pour le compte Windows actuel, propose un raccourci sur le bureau et conserve les réglages et données personnelles de Coclico après désinstallation. L’installation standard omet le volumineux runtime CUDA facultatif ; choisis ce composant uniquement si tu as une carte NVIDIA compatible. L’installateur n’est pas signé numériquement et Windows peut afficher un avertissement sur son éditeur.

L’installateur et l’archive portable incluent le runtime .NET 10 Desktop ; aucune installation séparée de .NET n’est nécessaire. Chaque release fournit `SHA256SUMS.txt` pour vérifier les deux téléchargements.

Pour créer l’installateur localement, lance simplement `./installer/build-installer.ps1`. Le script publie Coclico en version autonome Windows x64 si nécessaire, compresse le payload et génère l’installeur moderne autonome dans `artifacts/installer`.

## Raccourcis clavier et zone de notification

| Raccourci | Action |
| --- | --- |
| `F11` | Basculer entre plein écran et fenêtré |
| `Ctrl` + `Espace` | Ouvrir ou fermer le panneau de l’assistant IA |

Coclico peut aussi se réduire dans la zone de notification (active-le dans les Paramètres). Un clic droit sur l’icône restaure la fenêtre.

## Compiler le projet

Installe le SDK .NET 10 et utilise un environnement Windows avec le SDK Windows Desktop :

```powershell
dotnet restore Coclico.slnx
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
dotnet format Coclico.slnx --verify-no-changes --no-restore
```

Ouvre `Coclico.slnx` dans Visual Studio ou compile-le depuis PowerShell. Pour publier un dossier Windows x64 autonome :

```powershell
dotnet publish Coclico/Coclico.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output ./publish `
  -p:PublishSingleFile=false `
  -p:DebugSymbols=false `
  -p:DebugType=None
```

Les détails sur l’architecture, l’anatomie d’un module, le pipeline de l’installeur et la CI se trouvent dans la [documentation développeur](READMEDEV_FR.md).

## Précautions

- Certaines opérations peuvent supprimer des fichiers ou modifier Windows. Vérifie les catégories sélectionnées et lis le résumé après l’opération.
- L’optimisation réseau écrit des paramètres qui dépendent de la carte et du pilote. Utilise les instantanés et la restauration proposés, et garde un autre moyen de rétablir la connexion.
- Le module d’alimentation n’utilise que la ligne de commande `powercfg` (aucune écriture registre directe), valide chaque paramètre par rapport à ton matériel et peut supprimer les plans Coclico à tout moment.
- Les opérations qui ont besoin de droits élevés demandent une élévation ; si tu la refuses, certaines fonctions peuvent être indisponibles.
- Garde une sauvegarde de tes données importantes. Coclico ne remplace ni les outils de récupération Windows ni une solution de sauvegarde.
- Pour signaler un problème reproductible, indique la version de Windows, celle de Coclico et les étapes suivies. N’envoie jamais de mot de passe, de clé API, de fichier personnel ni de journal contenant des données non masquées.

## Organisation du dépôt

```text
Coclico/                 Application WPF
  Common/                Services applicatifs partagés (core, UI, diagnostics, exécution)
  Modules/               Tableau de bord, IA, nettoyage, réseau, alimentation, disque, etc.
  Resources/             Thèmes, langues et icônes
Coclico.Installer/       Installeur autonome WPF moderne (SingleFile, fluide et animé)
Coclico.Tests/           Tests unitaires et validation XAML
installer/               Scripts d'empaquetage et de publication de l'installeur
docs/                    Captures d'écran utilisées par les README
.github/workflows/       Compilation, tests et publication
README.md                Documentation en anglais
README_FR.md             Documentation en français
READMEDEV.md             Documentation développeur (anglais)
READMEDEV_FR.md          Documentation développeur (français)
```

## Licence

Le projet est distribué sous licence MIT. Tu peux utiliser, copier, modifier, fusionner, publier, distribuer, accorder une sous-licence et vendre des copies du projet, à condition d’inclure la mention de droit d’auteur et le texte de la licence. Consulte [LICENSE.txt](LICENSE.txt). Les dépendances et certains éléments tiers peuvent avoir leurs propres licences ; vérifie leurs mentions avant de les redistribuer.

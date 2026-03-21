# Module Nettoyage Coclico

Le Nettoyage est le moteur de nettoyage professionnel de Coclico. Il supprime en toute sécurité les fichiers inutiles accumulés sur le système pour libérer de l'espace disque, améliorer les performances et maintenir un PC propre.

## Les 10 catégories de nettoyage profond

### 🪟 Fichiers temporaires Windows
- **Chemins nettoyés** : C:\Windows\Temp\, C:\Windows\Prefetch\ (via catégorie séparée), dossiers temp système
- **Contenu** : fichiers créés par Windows et les installeurs lors des mises à jour, installations, et opérations système
- **Taille typique** : de quelques Mo à plusieurs Go sur les anciens systèmes
- **Sécurité** : 100% sûr, aucun fichier système actif n'est touché

### 🌐 Cache navigateurs
- **Navigateurs ciblés** : Google Chrome, Mozilla Firefox, Microsoft Edge, Opera, Brave, et autres basés sur Chromium
- **Chemins typiques** : %LocalAppData%\Google\Chrome\User Data\Default\Cache\, %AppData%\Mozilla\Firefox\Profiles\...\cache2\
- **Contenu** : images, scripts JavaScript, CSS, vidéos mises en cache localement par les sites web
- **Impact** : le nettoyage n'affecte PAS les mots de passe, favoris, historique ni les sessions connectées
- **Taille typique** : 500 Mo à 5 Go selon l'utilisation du navigateur

### 📋 Logs système
- **Chemins** : dossiers de journaux Windows (%WinDir%\Logs\, %WinDir%\Panther\), logs d'applications
- **Contenu** : journaux d'événements, traces de débogage, fichiers .log et .etl accumulés
- **Taille typique** : de quelques Mo à plusieurs centaines de Mo sur les systèmes actifs longtemps

### 🗑️ Corbeille
- **Opération** : vide définitivement la Corbeille de **tous les lecteurs** (C:\, D:\, E:\, etc.)
- **Avertissement** : les fichiers supprimés de la Corbeille ne sont PAS récupérables
- **Taille typique** : variable selon ce qui a été supprimé et non encore vidé

### 👤 Fichiers temporaires utilisateur
- **Chemin** : %TEMP% (généralement C:\Users\[nom]\AppData\Local\Temp\)
- **Contenu** : fichiers temporaires créés par les applications de l'utilisateur : fichiers d'installation, extractions, données de travail temporaires
- **Taille typique** : 200 Mo à 3 Go selon les applications utilisées

### 🖼️ Cache miniatures (Thumbnails)
- **Chemin** : %LocalAppData%\Microsoft\Windows\Explorer\ (fichiers thumbcache_*.db)
- **Contenu** : aperçus générés par Windows Explorer pour les images, vidéos et documents
- **Impact** : Windows recrée automatiquement les miniatures au prochain accès aux dossiers
- **Taille typique** : 50 Mo à 500 Mo

### ⚠️ Rapports d'erreur Windows (WER)
- **Chemin** : %LocalAppData%\Microsoft\Windows\WER\, %ProgramData%\Microsoft\Windows\WER\
- **Contenu** : dumps mémoire et rapports générés par Windows après chaque crash d'application
- **Taille typique** : 100 Mo à 2 Go sur les systèmes avec des crashs fréquents

### 📦 Fichiers d'installation obsolètes
- **Contenu** : résidus d'installers (.msi, .exe), fichiers de désinstallation incomplets, packages de mise à jour Windows déjà appliqués
- **Chemins** : C:\Windows\Installer\ (résidus), dossiers d'install abandonnés

### 🌍 Cache DNS
- **Opération** : exécute la commande `ipconfig /flushdns`
- **Contenu** : cache des résolutions DNS (correspondance nom de domaine ↔ adresse IP)
- **Utilité** : résoudre les problèmes d'accès à des sites web (erreurs DNS, sites qui ne se chargent plus)
- **Impact** : le cache DNS est automatiquement reconstruit lors des prochaines résolutions réseau

### ⚡ Prefetch
- **Chemin** : C:\Windows\Prefetch\ (nécessite droits admin)
- **Contenu** : fichiers de précache générés par Windows pour accélérer le démarrage des applications fréquemment utilisées
- **Impact** : Windows recrée ces fichiers automatiquement à l'usage, les premières lancements après nettoyage peuvent être légèrement plus lents

## Fonctionnalités du module Nettoyage

### Avant le nettoyage
- **Estimation de l'espace récupérable** : Coclico analyse les catégories sélectionnées et affiche l'espace total récupérable avant de commencer (en Go ou Mo)
- **Sélection des catégories** : chaque catégorie peut être cochée ou décochée individuellement selon les besoins
- **Confirmation obligatoire** : une boîte de dialogue demande une confirmation explicite avant toute suppression

### Pendant le nettoyage
- **Barre de progression** : pourcentage d'avancement global
- **Statut en texte** : catégorie en cours de nettoyage
- **Compteur en temps réel** : espace libéré en cours affiché dynamiquement
- **Annulation possible** : le nettoyage peut être interrompu à tout moment via CancellationToken

### Rapport final
- Nombre total de fichiers supprimés
- Nombre de dossiers nettoyés
- Espace total libéré (affiché en Go ou Mo)
- Durée totale du nettoyage
- Liste des erreurs éventuelles (fichiers verrouillés non supprimés)

### Nettoyage Windows intégré (classique)
En plus du nettoyage profond de Coclico, un bouton permet de lancer l'outil natif Windows Disk Cleanup (cleanmgr.exe) pour un nettoyage complémentaire incluant les fichiers système Windows.

## Sécurité et fiabilité
- Aucun fichier système critique n'est ciblé
- Tous les chemins sont validés avant suppression
- Les fichiers verrouillés (en cours d'utilisation) sont ignorés sans erreur fatale
- Les erreurs sont collectées et affichées dans le rapport sans interrompre le nettoyage

## Conseils d'utilisation
- Faire un nettoyage mensuel pour maintenir les performances
- Cache navigateurs et fichiers temporaires utilisateur sont les catégories les plus efficaces
- Ne pas nettoyer Prefetch trop fréquemment (ralentit les premiers lancements)
- Vider la Corbeille régulièrement (récupère souvent plusieurs Go)

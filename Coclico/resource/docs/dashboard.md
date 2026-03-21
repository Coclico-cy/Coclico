# Tableau de Bord Coclico

Le Tableau de Bord est la page d'accueil de Coclico. Il surveille les ressources système en temps réel avec un rafraîchissement automatique toutes les 3 secondes. C'est le hub central pour évaluer instantanément l'état de santé du PC.

## Indicateurs affichés en temps réel

### CPU – Processeur
- Taux d'utilisation globale du processeur en pourcentage (ex : 23%)
- Affiché avec une barre de progression colorée
- Source technique : Windows PerformanceCounter "Processor Information / % Processor Time / _Total"
- Initialisation en arrière-plan au démarrage (la première valeur retournée par Windows PerformanceCounter est toujours 0, elle est automatiquement ignorée)
- Rafraîchi toutes les 3 secondes
- Interprétation : 0-50% = normal, 50-80% = charge élevée, 80-100% = surcharge

### RAM – Mémoire vive
- Mémoire utilisée en gigaoctets et mémoire totale installée (ex : 5.2 GB / 16.0 GB)
- Pourcentage d'utilisation calculé en temps réel : (utilisée / totale) × 100
- Barre de progression visuelle
- Permet de détecter les applications gourmandes, les fuites mémoire ou le manque de RAM
- Interprétation : < 70% = normal, 70-85% = charge importante, > 85% = risque de ralentissement

### Disque – Espace de stockage
- Lecteur Windows détecté automatiquement (pas forcément C:\, dépend de l'installation Windows)
- Espace utilisé en GB, espace total en GB (ex : 120.5 GB / 500.0 GB)
- Espace libre calculé automatiquement : total - utilisé
- Pourcentage d'utilisation du disque
- Interprétation : < 80% = normal, 80-90% = à surveiller, > 90% = critique, utiliser le module Nettoyage

### Uptime – Temps depuis le dernier démarrage
- Durée exacte depuis le dernier allumage ou redémarrage de Windows
- Format affiché : XXh XXm XXs (ex : 08h 57m 48s)
- Basé sur Environment.TickCount64 ou l'API Windows GetTickCount
- Un uptime très long (plusieurs semaines/mois) peut indiquer des mises à jour Windows en attente

### Applications installées
- Nombre total d'applications et jeux détectés sur le PC
- Comptabilise toutes les sources détectées : Registre Windows + Steam + Epic Games + GOG
- Se met à jour lors du rafraîchissement

### Processus actifs
- Nombre total de processus Windows en cours d'exécution à l'instant du rafraîchissement
- Inclut tous les processus système et utilisateur
- Un nombre très élevé (> 200) peut indiquer des processus parasites

## Comportement technique du Tableau de Bord
- Rafraîchissement automatique via un DispatcherTimer WPF toutes les 3 secondes
- Chaque metric est mise à jour indépendamment via data binding (INotifyPropertyChanged)
- Toutes les opérations de lecture des métriques sont asynchrones pour ne pas bloquer l'interface
- Le compteur CPU est initialisé dans un thread séparé (Task.Run) au démarrage pour ne pas ralentir le lancement
- La détection du lecteur Windows utilise Environment.GetFolderPath(SpecialFolder.Windows) pour être 100% fiable

## Que faire quand les métriques sont anormales ?

### CPU constamment > 80%
1. Ouvrir le module Scanner ou Applications dans Coclico pour identifier les applications consommatrices
2. Utiliser le Gestionnaire des tâches Windows (Ctrl+Shift+Esc) pour voir quel processus utilise le plus de CPU
3. Flow Chains : créer une automatisation pour KillProcess sur les processus non désirés

### RAM > 85%
1. Fermer les applications non utilisées
2. Utiliser le module Nettoyage pour vider les caches qui consomment de la mémoire
3. Envisager d'ajouter de la RAM si le problème est récurrent

### Disque > 90%
1. Aller dans le module **Nettoyage** de Coclico pour libérer de l'espace
2. Les catégories les plus efficaces : Cache navigateurs, Fichiers temp, Corbeille
3. Utiliser le module Scanner pour identifier les applications volumineuses

### Uptime très long
- Redémarrer Windows pour appliquer les mises à jour et libérer la mémoire fragmentée
- Un redémarrage régulier (hebdomadaire) maintient les meilleures performances

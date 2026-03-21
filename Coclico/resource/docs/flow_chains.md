# Flow Chains – Automatisation visuelle Coclico

Flow Chains est l'éditeur d'automatisation visuelle de Coclico. Il permet de créer des séquences d'actions automatisées (appelées "chaînes") qui s'exécutent dans un ordre défini, entièrement sans ligne de commande. Interface par nœuds drag-and-drop sur un canevas.

## Concept fondamental
Une Flow Chain est un graphe orienté de nœuds interconnectés par des liens. Chaque nœud = une action. Les liens définissent l'ordre d'exécution. Les chaînes sont sauvegardées en fichiers JSON et peuvent être rechargées, modifiées, dupliquées et réexécutées à volonté. Aucune compétence en programmation n'est requise.

## Les 14 types de nœuds

### Nœuds de structure (obligatoires)
- **Start** : nœud d'entrée obligatoire. Toute chaîne doit commencer par ce nœud. C'est le point de départ de l'exécution.
- **End** : nœud de sortie. Marque la fin de l'exécution de la chaîne.

### Actions sur les applications
- **OpenApp** : ouvrir une application ou un fichier.
  - Paramètre : chemin complet vers l'exécutable (.exe) ou le fichier
  - Paramètre : arguments de lancement (optionnel)
  - Option : attendre que l'application se ferme avant de continuer vers le nœud suivant
- **CloseApp** : fermer une application en cours d'exécution.
  - Paramètre : nom du processus (ex : "notepad", "chrome", "code")
  - Fermeture propre (WM_CLOSE) avant de passer au suivant
- **KillProcess** : forcer l'arrêt immédiat d'un processus.
  - Paramètre : nom du processus
  - Équivaut à "Fin de tâche" dans le Gestionnaire des tâches
  - Utile quand CloseApp ne répond pas

### Actions système
- **RunCommand** : exécuter une commande en ligne de commande.
  - Paramètre : ligne de commande complète (compatible cmd.exe et PowerShell)
  - Exemples : "ipconfig /flushdns", "shutdown /r /t 60", scripts .bat, .ps1
- **Delay** : faire une pause dans l'exécution.
  - Paramètre : durée en secondes
  - Indispensable pour espacer les actions et laisser les applications se charger
- **Notification** : envoyer une notification toast Windows.
  - Paramètre : message à afficher dans la notification
  - La notification apparaît dans le centre de notifications Windows

### Logique conditionnelle
- **Condition** : branchement "si / sinon" selon une condition testée.
  - Si la condition est vraie → branche "Oui" (lien vert)
  - Si la condition est fausse → branche "Non" (lien rouge)
  - **Opérateurs disponibles** (10 au total) :
    - ProcessRunning / ProcessNotRunning : tester si un processus est actif
    - FileExists / FileNotExists : tester la présence d'un fichier ou dossier
    - TimeAfter / TimeBefore : comparer l'heure actuelle à une heure cible
    - CpuBelow / CpuAbove : comparer l'utilisation CPU à un seuil en %
    - RamBelow / RamAbove : comparer l'utilisation RAM à un seuil
  - Paramètre : valeur de comparaison (nom du processus, chemin, heure, seuil en %)

### Boucles et parallélisme
- **Loop** : répéter un groupe de nœuds N fois.
  - Paramètre : nombre d'itérations (LoopCount)
  - Paramètre : délai entre chaque itération en millisecondes (LoopDelayMs)
  - Utile pour les vérifications répétées ou les tentatives multiples
- **Parallel** : exécuter plusieurs branches de nœuds simultanément en parallèle.
  - Permet de lancer plusieurs actions en même temps
  - Les branches rejoignent un nœud de synchronisation une fois toutes terminées

### Intégrations réseau et fichiers
- **HttpRequest** : envoyer une requête HTTP vers une URL.
  - Paramètre : URL de destination
  - Paramètre : méthode HTTP (GET, POST, PUT, DELETE)
  - Utile pour déclencher des webhooks, des APIs REST, des notifications web
- **FileOperation** : effectuer des opérations sur des fichiers ou dossiers.
  - Paramètre : chemin source
  - Paramètre : chemin destination
  - Paramètre : type d'opération (copier, déplacer, supprimer)
- **SystemCheck** : effectuer des vérifications système avancées.

## Propriétés communes à chaque nœud
- **Nom** : label personnalisé affiché sur le nœud dans l'éditeur
- **Position** : X/Y sur le canevas, modifiable par drag-and-drop
- **Dimensions** : largeur et hauteur du nœud (160×70 par défaut)
- **Activé / Désactivé** : désactiver un nœud l'ignore sans le supprimer (pratique pour les tests)
- **RetryCount** : nombre de tentatives en cas d'échec (0 à 10 retries)
- **TimeoutSeconds** : délai maximum alloué à l'action avant abandon

## Sauvegarde et chargement
- Les chaînes sont sauvegardées au format JSON
- Chaque nœud est sérialisé avec toutes ses propriétés et sa position sur le canevas
- Les liens entre nœuds sont également sauvegardés

## Exemples concrets de Flow Chains

### Séquence de démarrage de travail
Start → OpenApp(VS Code) → Delay(3s) → OpenApp(Chrome, "https://github.com") → OpenApp(Spotify) → End

### Surveillance CPU
Start → Loop(∞) → Delay(60s) → Condition(CpuAbove, 80%) → [Oui] Notification("CPU à plus de 80% !") → [Non] End

### Nettoyage de nuit
Start → Condition(TimeAfter, 23:00) → [Oui] RunCommand("cleanmgr /sagerun:1") → Notification("Nettoyage terminé") → End

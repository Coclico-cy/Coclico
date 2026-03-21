# Module RAM Cleaner – Coclico

Le RAM Cleaner est le module de surveillance et de nettoyage complet de la mémoire vive de Coclico. Il permet de libérer la RAM physique, virtuelle et le fichier d'échange en temps réel, avec 9 types de nettoyage distincts et plus.

## Surveillance en temps réel

### Mémoire physique (RAM)
- Affichage de la RAM utilisée, disponible et totale (en Ko/Mo/Go adaptatif)
- Barre de progression avec dégradé violet→rose
- Pourcentage d'utilisation mis à jour selon le taux de rafraîchissement choisi

### Mémoire virtuelle
- Affichage de l'espace virtuel utilisé, disponible et total
- Barre de progression distincte (bleu→rose)
- Inclut la mémoire mappée, les DLLs partagées et l'espace virtuel réservé

### Fichier d'échange (Page File)
- Affichage de l'utilisation du fichier d'échange Windows (pagefile.sys)
- Taille utilisée, disponible et totale

### Taux de rafraîchissement
Configurable : 500 ms, 1 s, 2 s, 5 s, 10 s selon les besoins de performance.

## Les 9 types de nettoyage

### 1. Ensemble de travail (Working Sets)
- **Opération** : vide les pages actives de chaque processus en cours
- **API** : SetProcessWorkingSetSize(-1, -1) sur chaque processus
- **Effet** : force Windows à déplacer les pages inutilisées vers la liste Standby
- **Résultat** : libère immédiatement de la RAM physique pour d'autres usages

### 2. Liste d'attente – Standby
- **Opération** : purge la liste des pages en attente de réutilisation
- **API** : NtSetSystemInformation(80, MemoryPurgeStandbyList=4)
- **Effet** : libère les pages mises en cache en attente de réutilisation
- **Résultat** : augmente la RAM disponible immédiatement

### 3. Liste d'attente basse priorité
- **Opération** : purge la sous-liste Standby de faible priorité
- **API** : NtSetSystemInformation(80, MemoryPurgeLowPriorityStandbyList=5)
- **Effet** : cible spécifiquement les pages de moindre importance
- **Résultat** : libération supplémentaire sans affecter les pages critiques

### 4. Liste de pages modifiées
- **Opération** : force l'écriture sur disque des pages sales (dirty pages)
- **API** : NtSetSystemInformation(80, MemoryFlushModifiedList=3)
- **Effet** : écrit les pages modifiées mais non encore sauvegardées sur le disque/pagefile
- **Résultat** : les pages écrites passent en liste Standby et deviennent réutilisables

### 5. Liste de pages combinées
- **Opération** : exécute le flush modifié (cmd 3) puis la purge standby (cmd 4)
- **Effet** : nettoyage combiné plus complet — libère à la fois les pages sales ET les pages en attente
- **Résultat** : libération maximale en une seule opération

### 6. Cache de fichiers modifiés
- **Opération** : flush rapide des pages de fichiers modifiés via NtSetSystemInformation cmd 6
- **API** : MemoryFlushModifiedListFast=6 (fallback sur cmd 3 si non supporté)
- **Effet** : variante rapide du flush de pages modifiées, adaptée aux versions récentes de Windows

### 7. Cache de fichiers système
- **Opération** : libère le cache système de fichiers Windows
- **API** : SetSystemFileCacheSize(-1, -1, 0) — nécessite SeIncreaseQuotaPrivilege
- **Effet** : purge les pages mises en cache lors des lectures de fichiers
- **Résultat** : libère souvent plusieurs centaines de Mo après une session intensive de fichiers

### 8. Cache de registre
- **Opération** : écrit les ruches du registre Windows sur le disque
- **API** : RegFlushKey(HKEY_LOCAL_MACHINE) + RegFlushKey(HKEY_CURRENT_USER)
- **Effet** : force la synchronisation des données de registre en mémoire vers le disque
- **Usage** : utile pour assurer l'intégrité du registre

### 9. Cache DNS
- **Opération** : vide le cache du résolveur DNS
- **API** : DnsFlushResolverCache()
- **Effet** : supprime les correspondances nom de domaine ↔ adresse IP en cache
- **Usage** : résoudre les problèmes de connexion, DNS périmés, accès à de nouveaux domaines

### Extra : Garbage Collect .NET
- **Opération** : force la collection de toute la mémoire managée .NET
- **API** : GC.Collect(MaxGeneration, Aggressive, blocking, compacting)
- **Effet** : libère la mémoire allouée par les applications .NET (incluant Coclico lui-même)
- **Note** : désactivé par défaut (peut provoquer une brève latence)

## Nettoyage automatique

### Par intervalle
- Nettoie automatiquement toutes les N minutes (configurable)
- Le timer se réinitialise après chaque nettoyage

### Par seuil RAM
- Déclenche le nettoyage dès que la RAM physique dépasse le pourcentage configuré
- Vérifié à chaque tick du moniteur (selon le taux de rafraîchissement)
- Évite les nettoyages inutiles quand la RAM est suffisante

## Résultats

Après chaque nettoyage, un panneau de résultats affiche pour chaque type :
- L'espace libéré par chaque opération (Ko/Mo/Go adaptatif)
- Le total global libéré (mesuré avant/après)
- L'heure du dernier nettoyage

## Notifications
Un bouton permet d'activer/désactiver les notifications toast après chaque nettoyage. La notification indique la quantité totale de RAM libérée.

## Conseils d'utilisation
- Pour un nettoyage rapide : cocher Working Sets + Standby suffit souvent
- Pour un nettoyage complet : cocher tous les types
- Cache fichiers système : très efficace après de grosses opérations de copie/compilation
- Cache DNS : utile uniquement en cas de problèmes réseau
- Ne pas utiliser trop fréquemment en mode automatique par seuil : Windows gère bien sa mémoire seul

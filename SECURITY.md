# Security Policy / Politique de sécurité

## Supported versions / Versions prises en charge

| Version | Supported / Prise en charge |
| ------- | --------------------------- |
| 2.1.x   | Yes / Oui                   |

## Reporting a vulnerability / Signaler une vulnérabilité

If you discover a security flaw in Coclico:

1. **Do NOT open a public issue** describing the flaw
2. Use the "Report a vulnerability" tool in the repository's Security tab
3. Describe the issue and the steps to reproduce

You will receive a response within 7 days. Valid flaws will be fixed as a priority and credited in the related release. Private vulnerability reports can also be sent through https://github.com/kiwi-fr/teste/security/advisories. We follow a coordinated disclosure process: a confirmed vulnerability is fixed within 30 days of confirmation, and the fix is published no later than 90 days after the initial report.

Si vous découvrez une faille de sécurité dans Coclico :

1. **N'ouvrez pas d'issue publique** décrivant la faille
2. Utilisez l'outil « Report a vulnerability » de l'onglet Security du dépôt
3. Décrivez le problème et les étapes pour le reproduire

Vous recevrez une réponse sous 7 jours. Les failles valides seront corrigées en priorité et créditées dans la release concernée. Les signalements privés passent aussi par https://github.com/kiwi-fr/teste/security/advisories. Nous suivons un processus de divulgation coordonnée : une vulnérabilité confirmée est corrigée sous 30 jours après confirmation, et la correction est publiée au plus tard 90 jours après le signalement initial.

## Chaîne de confiance / Chain of trust

Coclico applique une chaîne de confiance complète, de la source jusqu'à l'exécution :

1. **Code source public.** Le dépôt GitHub est la référence. Le code visible ne prouve rien à lui seul : c'est la signature qui fait foi.
2. **Pipeline de build contrôlé.** Les releases officielles sont produites par le workflow GitHub Actions du dépôt, avec des actions épinglées par SHA de commit, des permissions minimales et une attestation de provenance générée pour chaque artefact.
3. **Manifeste signé.** Chaque release embarque un manifeste `integrity.json` qui décrit chaque fichier livré (chemin, taille, SHA-256). Le manifeste est signé en RSA-PSS avec SHA-256 par la clé privée du projet. La clé publique de vérification est compilée dans l'application (`Coclico/Security/trust-policy.json`).
4. **Vérification à l'installation et à l'exécution.** L'application vérifie le manifeste à chaque démarrage, toutes les 30 minutes, et à chaque événement du système de fichiers sur ses binaires. L'élévation de privilèges est refusée si l'intégrité est compromise.
5. **Mises à jour authentifiées.** `UpdateManager` refuse une release sans manifeste signé valide (lorsqu'une clé de confiance est configurée), applique une politique anti-rollback (version minimale autorisée, génération de sécurité minimale, retour arrière réservé à la dernière version saine connue), et vérifie le SHA-256 de chaque artefact téléchargé.
6. **Journal de sécurité local.** Chaque vérification est inscrite dans un journal chaîné par HMAC (`%AppData%\Coclico\security\journal.log`), clé protégée par DPAPI. Toute altération du journal casse la chaîne et devient détectable.
7. **Chargement des bibliothèques natives contrôlé.** Au démarrage, la recherche Windows de DLL est restreinte aux répertoires approuvés (`SetDefaultDllDirectories`), et chaque chargement natif (moteur IA, dépendances) est refusé hors du répertoire de l'application ou depuis un répertoire temporaire.
8. **Exécution de processus conditionnée.** Toute exécution de commande ou lancement d'application par Coclico passe par le verrou d'intégrité : si le statut est Altéré ou Récupération requise, l'exécution est refusée et journalisée.

## Statuts d'intégrité / Integrity statuses

| Statut | Signification | Comportement |
| ------ | ------------- | ------------ |
| Intègre (Valid) | Tous les contrôles ont réussi | Fonctionnement normal |
| Inconnu (Unknown) | Contrôle impossible ou incomplet | L'application fonctionne mais se présente comme non vérifiée |
| Altéré (Altered) | Fichier modifié, manquant ou ajouté, ou signature invalide | Les opérations sensibles (élévation, mises à jour) sont bloquées |
| Récupération requise (RecoveryRequired) | Version invalide ou installation endommagée | Réinstallation depuis une release authentifiée |

Une erreur d'accès à un fichier n'est jamais interprétée comme une attaque : elle produit le statut Inconnu avec une raison précise, pas Altéré.

## Gestion des clés / Key management

- La clé privée de signature ne quitte jamais la machine de publication. Elle est stockée chiffrée (PBKDF2-SHA256, 600 000 itérations, HKDF, AES-256-GCM) dans `installer/Key/key.keyfile`, fichier exclu du dépôt par `.gitignore`.
- La clé publique est publique : elle est intégrée au code source dans `Coclico/Security/trust-policy.json`.
- Chaque signature porte un `keyId`. La liste des clés de confiance accepte plusieurs clés, avec période d'activation et révocation, pour permettre une rotation sans bloquer les utilisateurs existants.
- La procédure de rotation et d'urgence est documentée dans `installer/Key/LISEZMOI.md`.

## Limites assumées / Known limits

- Le vérificateur s'exécute dans le même processus que l'application : un attaquant qui contrôle déjà Windows avec des droits administrateur peut patcher le vérificateur lui-même. Le système détecte les attaques de masse et les modifications de fichiers ; il ne peut pas arrêter un attaquant admin déterminé.
- Un fork peut être re-signé avec une autre clé : il s'affichera alors comme version non vérifiable, pas comme version officielle.
- Le journal local est chaîné par HMAC, mais si l'attaquant contrôle à la fois la clé et le journal, il peut tout réécrire : la chaîne détecte les altérations, elle ne les empêche pas. L'export signé (`ExportSigned`) fige un état vérifiable du journal, mais un attaquant qui contrôle la machine peut produire un export depuis un journal réécrit.
- `SetDefaultDllDirectories` durcit la recherche de DLL mais n'est pas une sandbox : il ne protège pas contre du code déjà exécuté dans le processus. `AssemblyLoadContext` et les permissions restent des limites documentées de Microsoft.
- Pas de télémétrie, pas d'envoi automatique de journaux : aucune donnée ne quitte la machine sans action explicite de l'utilisateur.
- Les composants téléchargés après l'installation (runtime CUDA dans `runtimes/win-x64/native/cuda12`, modèle IA dans `resource/model`, désinstalleur dans `Uninstall`) sont volontairement hors du manifeste signé : ils ne déclenchent donc pas d'alerte d'intégrité. Le répertoire d'installation est protégé par ACL (Administrateurs et SYSTEM en contrôle total, utilisateurs en lecture/exécution), mais ces fichiers ne bénéficient pas de la vérification cryptographique continue. Si un jour ils doivent être couverts, ils devront être intégrés au manifeste signé par la release.
- L'installeur refuse un payload dont le manifeste est présent mais invalide ; si le manifeste est absent, l'installation continue avec un avertissement (compatibilité dev et versions non signées), et la vérification locale de l'application affichera ensuite un statut inconnu, pas un bouclier vert.

## Incidents / Incident response

| Incident | Réaction |
| -------- | -------- |
| Manifeste absent ou invalide | Version considérée comme non officielle, statut Inconnu |
| Exécutable ou DLL modifié(e) | Statut Altéré, opérations sensibles bloquées, journalisation |
| Mise à jour refusée | La version saine installée reste en place |
| Clé de signature compromise | Suspension des releases, révocation de la clé dans la politique de confiance, nouvelle clé, republication |
| Compte GitHub compromis | Suspendre les releases, révoquer les accès, auditer les workflows, republier depuis un environnement sain |
| Faux positif d'intégrité | Identifier le fichier et la cause, corriger, ne jamais désactiver silencieusement les contrôles |

La procédure détaillée pas à pas se trouve dans `installer/Key/LISEZMOI.md`.

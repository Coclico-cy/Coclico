# Paramètres Coclico

Les Paramètres centralisent toutes les options de personnalisation de l'apparence et du comportement de Coclico. Toutes les préférences sont sauvegardées automatiquement dans %AppData%\Coclico\settings.json et restaurées au prochain lancement.

## Section Apparence

### Couleur d'accent
- La couleur d'accent est appliquée à tous les éléments interactifs de l'interface : boutons, sélections actives, barres de progression, indicateurs, bordures de focus
- **Format** : valeur hexadécimale #RRGGBB (ex : #6366F1, #FF5722, #00BCD4)
- **Valeur par défaut** : #6366F1 (Indigo)
- Des **présets de thème** rapides sont disponibles : Indigo, Purple, Blue, Green, Red, Orange, etc.
- Le changement de couleur est appliqué immédiatement dans toute l'interface sans redémarrage

### Thème de fond (Background Mode)
- Contrôle le style de l'arrière-plan de la fenêtre principale
- **UltraDark** (par défaut) : fond noir très profond (#050505), idéal pour les écrans OLED et les environnements sombres
- D'autres modes sont disponibles selon le thème choisi (Dark, Medium, etc.)
- Se combine avec la couleur d'accent pour un rendu cohérent

### Opacité des cartes (CardOpacity)
- Contrôle le niveau de transparence des panneaux, cartes et surfaces de fond de l'interface
- **Échelle** : 0.0 (totalement transparent) → 1.0 (totalement opaque)
- **Valeur par défaut** : 0.07 (très légèrement visible, crée un effet de profondeur subtil)
- Valeurs recommandées : 0.05 à 0.15 pour un rendu moderne et épuré
- L'effet est visible immédiatement lors du déplacement du slider

### Taille de police (FontSize)
- Ajuste la taille du texte dans toute l'interface Coclico simultanément
- **Valeur par défaut** : 13pt
- **Plage recommandée** : 11pt (compact) à 16pt (confort de lecture)
- Le changement s'applique en temps réel lors du déplacement du slider
- Utile pour les écrans haute résolution (4K) ou les utilisateurs malvoyants

## Section Interface

### Largeur de la barre latérale (SidebarWidth)
- Ajuste la largeur de la sidebar de navigation à gauche
- **Valeur par défaut** : 220 pixels
- Une sidebar plus large affiche plus de texte dans les labels de navigation
- Une sidebar plus étroite laisse plus d'espace au contenu principal

### Mode compact (CompactMode)
- **Activé** : réduit les marges, paddings et espacements dans toute l'interface
- **Désactivé** (par défaut) : interface avec espacements confortables
- Recommandé pour les petits écrans (laptop 13"-15") ou pour maximiser le contenu visible
- Compatible avec toutes les autres options d'apparence

## Section Langue

### Sélection de la langue
- **fr** : Français (langue par défaut de Coclico)
- **en** : English
- Tous les textes de l'interface (labels, boutons, messages, dialogues) changent immédiatement
- Aucun redémarrage requis
- La langue est appliquée via LocalizationService qui charge les ressources correspondantes

## Section Comportement

### Démarrage automatique (LaunchAtStartup)
- **Activé** : Coclico se lance automatiquement à chaque démarrage/connexion Windows
- **Désactivé** (par défaut) : lancement manuel uniquement
- **Mécanisme** : ajoute/supprime une entrée dans la clé de registre Windows : HKCU\Software\Microsoft\Windows\CurrentVersion\Run
- La valeur "Coclico" pointe vers l'exécutable de l'application
- Ne nécessite pas de droits administrateur (clé HKCU = utilisateur courant)

### Réduire dans la barre des tâches – Tray (MinimizeToTray)
- **Activé** : cliquer sur le X de la fenêtre réduit Coclico dans le system tray (zone de notification, coin bas-droit de l'écran) au lieu de le fermer
- **Désactivé** (par défaut) : le X ferme complètement l'application
- **Icône tray** : apparaît dans la zone de notification
- **Clic gauche sur l'icône** : rouvre la fenêtre principale de Coclico
- **Clic droit sur l'icône** : menu contextuel avec options Ouvrir et Quitter
- Permet de garder Coclico actif en arrière-plan (surveillance, flow chains) sans fenêtre visible

### Portée d'installation Winget (WingetScope)
- Affecte le comportement du module Installeur
- **machine** (valeur par défaut) : `winget install --scope machine` — installe pour tous les utilisateurs du PC dans Program Files. Nécessite une élévation UAC (droits administrateur). Recommandé pour les logiciels professionnels.
- **user** : `winget install --scope user` — installe uniquement dans %LocalAppData% pour le compte Windows courant. Pas besoin de droits admin. Idéal dans les environnements d'entreprise sans droits admin.

## Fichier de configuration
- **Emplacement** : %AppData%\Coclico\settings.json (ex : C:\Users\Martin\AppData\Roaming\Coclico\settings.json)
- **Format** : JSON avec clés en camelCase
- **Sauvegarde** : automatique à chaque modification de paramètre
- **Restauration** : au démarrage de Coclico, tous les paramètres sont rechargés depuis ce fichier
- Le fichier peut être supprimé manuellement pour revenir aux valeurs par défaut
- **Contenu** : language, accentColor, themePreset, backgroundMode, cardOpacity, fontSize, sidebarWidth, compactMode, wingetScope, launchAtStartup, minimizeToTray

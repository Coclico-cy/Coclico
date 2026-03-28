// Source de vérité : code C# — Coclico v1.0.4
// Modules confirmés depuis : fr.xaml, WorkflowPipeline.cs, DeepCleaningService.cs, SettingsService.cs

export default {
  // ── Navigation ──────────────────────────────────────────────────────────────
  nav_home: 'Accueil',
  nav_modules: 'Modules',
  nav_ai: 'Intelligence IA',
  nav_performance: 'Performance',
  nav_docs: 'Docs',
  nav_download: 'Télécharger',
  nav_guide: 'Guide',
  nav_wiki: 'Wiki Dev',
  nav_faq: 'FAQ',
  nav_changelog: 'Nouveautés',
  nav_open_menu: 'Ouvrir le menu',
  nav_close_menu: 'Fermer le menu',

  // ── Hero ─────────────────────────────────────────────────────────────────────
  hero_badge: 'Version 1.0.4 — Stable',
  hero_title_line1: 'Maîtrisez votre',
  hero_title_accent: 'Windows.',
  hero_title_line2: 'Entièrement.',
  hero_sub: '8 modules de précision. 1 interface souveraine. Télémétrie, automatisation, nettoyage profond et IA locale embarquée — contrôle absolu.',
  hero_cta_download: 'Télécharger Coclico',
  hero_cta_discover: 'Découvrir les modules',
  hero_scroll: 'Défiler',

  // ── Statistiques ─────────────────────────────────────────────────────────────
  stat_modules: 'Modules actifs',
  stat_nodes: 'Types de nœuds',
  stat_conditions: 'Opérateurs de condition',
  stat_sources: 'Sources de détection',
  stat_local: '100% Local & Privé',

  // ── Section Modules ──────────────────────────────────────────────────────────
  section_modules_title: 'Chaque outil. Une seule plateforme. Zéro compromis.',
  section_modules_sub: 'Coclico centralise 8 modules puissants dans une interface unifiée Fluent Design.',

  // Modules (noms exacts depuis fr.xaml)
  mod_dashboard_title: 'Tableau de Bord',
  mod_dashboard_desc: 'Surveillance temps réel toutes les 3 secondes : CPU%, RAM utilisée/totale, espace disque, uptime Windows, processus actifs, logiciels détectés. Modes Zen, Gamer et Travail en 1 clic.',
  mod_dashboard_tag1: 'Polling 3s',
  mod_dashboard_tag2: 'CPU & RAM',
  mod_dashboard_tag3: '3 modes rapides',

  mod_cleaning_title: 'Nettoyage Système',
  mod_cleaning_desc: '10 catégories sélectionnables : temporaires Windows, cache navigateurs, logs système, corbeille, miniatures, rapports d\'erreur, anciens installeurs, DNS, prefetch. Estimation d\'espace avant nettoyage.',
  mod_cleaning_tag1: '10 catégories',
  mod_cleaning_tag2: 'Pré-estimation',
  mod_cleaning_tag3: 'Deep Clean',

  mod_ram_title: 'RAM Cleaner',
  mod_ram_desc: 'Surveillance temps réel RAM physique, virtuelle (Commit) et Pagefile. Opérations natives P/Invoke : Working Sets, File Cache, Standby List, DNS, ARP, Heap Compact et plus. 3 profils : Rapide, Normal, Profond. Mode auto sur seuil ou intervalle.',
  mod_ram_tag1: 'P/Invoke natif',
  mod_ram_tag2: '3 profils',
  mod_ram_tag3: 'Auto-nettoyage',

  mod_scanner_title: 'App Scanner',
  mod_scanner_desc: 'Audit complet de toutes les applications installées. Détecte chaque source, affiche nom, version, éditeur, taille, chemin et date d\'installation. Résultats triables et filtrables.',
  mod_scanner_tag1: 'Multi-sources',
  mod_scanner_tag2: 'Audit détaillé',
  mod_scanner_tag3: 'Filtrable',

  mod_installer_title: 'Installeur Rapide',
  mod_installer_desc: 'Interface graphique pour Winget. Catalogue organisé par catégories : Internet, Runtimes, Dev, Gaming, Création, Système. Portée Machine ou Utilisateur configurable. Installation en 1 clic.',
  mod_installer_tag1: '6 catégories',
  mod_installer_tag2: '1 clic',
  mod_installer_tag3: 'Portée configurable',

  mod_programs_title: 'Applications',
  mod_programs_desc: 'Bibliothèque complète détectée depuis 8 sources : Registre Windows (HKLM + HKCU), Steam, Epic Games, GOG, Ubisoft, EA App, Rockstar, Microsoft Store. Renommez, catégorisez, ajoutez des .exe manuellement.',
  mod_programs_tag1: '8 sources',
  mod_programs_tag2: 'Catégories custom',
  mod_programs_tag3: 'Récemment utilisées',

  mod_flowchains_title: 'Flow Chains',
  mod_flowchains_desc: 'Éditeur visuel d\'automatisation drag-and-drop. 28 types de nœuds (app, commande, PowerShell, HTTP, fichier, RAM, son, screenshot…) avec retry et timeout par nœud. 10 opérateurs de condition. Boucles, parallèle. Déclencheur hotkey ou intervalle.',
  mod_flowchains_tag1: '28 types de nœuds',
  mod_flowchains_tag2: '10 conditions',
  mod_flowchains_tag3: 'Hotkey global',

  mod_settings_title: 'Paramètres',
  mod_settings_desc: 'Personnalisation complète : couleur d\'accentuation (#RRGGBB), thème Indigo/Dark/Light, opacité des cartes, taille de police, largeur sidebar, mode compact, 4 langues du site, démarrage Windows, minimiser dans le tray.',
  mod_settings_tag1: 'Thème custom',
  mod_settings_tag2: 'Profils multiples',
  mod_settings_tag3: 'JSON local',

  // ── Flow Chains (détail) ──────────────────────────────────────────────────────
  flowchains_title: 'Orchestrez chaque flux de travail.',
  flowchains_desc: 'L\'éditeur Flow Chains permet de créer des séquences d\'automatisation complexes par simple drag-and-drop. 28 types de nœuds, retry et timeout par nœud, 10 opérateurs de condition, boucles, exécution parallèle.',
  flowchains_feat_nodes: '28 types de nœuds',
  flowchains_feat_conditions: '10 opérateurs de condition',
  flowchains_feat_trigger: 'Déclencheur hotkey global',
  flowchains_feat_save: 'Sauvegarde automatique JSON',
  flowchains_feat_error: '3 comportements sur erreur',
  flowchains_doc_link: 'Voir la documentation complète',

  // ── IA Locale ────────────────────────────────────────────────────────────────
  ai_title: 'Une intelligence qui vit dans votre machine.',
  ai_desc: 'Assistant conversationnel intégré via LLamaSharp v0.26 et un modèle GGUF local. Aucune donnée ne quitte votre machine. Posez des questions, obtenez des explications et déclenchez des actions directement depuis le chat.',
  ai_feat1_title: '100% local et privé',
  ai_feat1_desc: 'Modèle GGUF via LLamaSharp 0.26 — aucune connexion Internet requise',
  ai_feat2_title: 'RAG intégré',
  ai_feat2_desc: 'Connaissance complète de tous les modules et fonctionnalités de Coclico',
  ai_feat3_title: 'Actions directes',
  ai_feat3_desc: 'Ouvre des modules, lance des analyses, exécute des nettoyages depuis le chat',
  ai_feat4_title: 'GPU optionnel',
  ai_feat4_desc: 'Backend Vulkan/CUDA pour une inférence plus rapide — CPU par défaut',
  ai_feat5_title: 'Contexte isolé',
  ai_feat5_desc: 'Dual-executor LLM : contexte chat séparé du contexte moteur autonome',

  // ── Téléchargement ───────────────────────────────────────────────────────────
  download_badge: 'Version 1.0.4 · Stable',
  download_title: 'Votre système. Élevé. Souverain.',
  download_sub: 'Téléchargez Coclico gratuitement. Lancez Coclico.exe en administrateur — aucun installeur requis.',
  download_btn: 'Télécharger Coclico 1.0.4',
  download_docs_btn: 'Documentation complète',
  download_req1: 'Windows 10 22H2 (build 22621)+ ou Windows 11',
  download_req2: '64 bits uniquement',
  download_req3: '~300 Mo (sans modèle IA)',
  download_req4: '4 Go RAM min · 8 Go recommandé pour l\'IA',

  // ── Footer ───────────────────────────────────────────────────────────────────
  footer_tagline: 'Gestion Windows souveraine. 8 modules. Un centre de commande.',
  footer_col_modules: 'Modules',
  footer_col_features: 'Fonctionnalités',
  footer_col_resources: 'Ressources',
  footer_link_dashboard: 'Tableau de Bord',
  footer_link_cleaning: 'Nettoyage',
  footer_link_ram: 'RAM Cleaner',
  footer_link_scanner: 'App Scanner',
  footer_link_installer: 'Installeur',
  footer_link_programs: 'Applications',
  footer_link_flowchains: 'Flow Chains',
  footer_link_ai: 'Coclico IA',
  footer_link_guide: 'Guide & Tutoriel',
  footer_link_wiki: 'Wiki Développeur',
  footer_link_faq: 'FAQ',
  footer_link_download: 'Télécharger',
  footer_copyright: '© 2026 Coclico',
  footer_built_with: 'Construit avec .NET 10 & WPF-UI 4.2 · Windows 10/11',

  // ── Guide ────────────────────────────────────────────────────────────────────
  guide_hero_badge: 'Guide Utilisateur Complet',
  guide_hero_title: 'Guide & Tutoriel',
  guide_hero_desc: 'Tout ce que vous devez savoir pour maîtriser Coclico — de l\'installation à l\'automatisation avancée avec les Flow Chains.',
  guide_nav_start: 'Démarrage',
  guide_nav_install: 'Installation',
  guide_nav_first_use: 'Première utilisation',
  guide_nav_interface: 'Interface générale',
  guide_nav_modules: 'Les 8 Modules',
  guide_nav_ai: 'Coclico IA',
  guide_nav_ref: 'Références',
  guide_nav_shortcuts: 'Raccourcis clavier',
  guide_nav_files: 'Fichiers & Données',

  // ── Wiki ─────────────────────────────────────────────────────────────────────
  wiki_hero_badge: 'Référence Technique',
  wiki_hero_title: 'Wiki Développeur',
  wiki_hero_desc: 'Architecture MVVM, DI Container, Services, Flow Chains, IA LLamaSharp, Localisation — documentation technique complète.',

  // ── FAQ ──────────────────────────────────────────────────────────────────────
  faq_hero_badge: 'Support',
  faq_hero_title: 'Foire Aux Questions',
  faq_hero_desc: 'Réponses aux questions les plus fréquentes sur l\'installation, l\'utilisation et les fonctionnalités de Coclico.',

  // ── Docs ─────────────────────────────────────────────────────────────────────
  docs_hero_badge: 'Documentation',
  docs_hero_title: 'Documentation Complète',
  docs_hero_desc: 'Référence complète de toutes les fonctionnalités, API et configurations de Coclico.',

  // ── Meta SEO ─────────────────────────────────────────────────────────────────
  home_meta_desc: 'Coclico v1.0.4 — Gestionnaire système Windows. 8 modules : Tableau de bord, Nettoyage, RAM Cleaner, App Scanner, Installeur, Applications, Flow Chains, Paramètres. IA locale LLamaSharp. 100% local & privé.',

  // ── UI commun ────────────────────────────────────────────────────────────────
  search_placeholder: 'Rechercher dans la documentation...',
  search_no_results: 'Aucun résultat.',
  toggle_dark: 'Mode sombre',
  toggle_light: 'Mode clair',
  back_to_top: 'Haut de page',
  copy_code: 'Copier',
  copied: 'Copié !',
  on_this_page: 'Sur cette page',
  prev_page: 'Précédent',
  next_page: 'Suivant',
} as const;

export type UI = typeof import('./fr').default;
export type UIKey = keyof UI;

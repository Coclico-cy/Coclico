import fr from './locales/fr';
import en from './locales/en';
import de from './locales/de';
import es from './locales/es';

// ── Types ────────────────────────────────────────────────────────────────────

export const languages = {
  fr: { label: 'Français', flag: '🇫🇷' },
  en: { label: 'English',  flag: '🇬🇧' },
  de: { label: 'Deutsch',  flag: '🇩🇪' },
  es: { label: 'Español',  flag: '🇪🇸' },
} as const;

export type Lang = keyof typeof languages;
export const defaultLang: Lang = 'fr';
export const supportedLangs = Object.keys(languages) as Lang[];

const ui = { fr, en, de, es } as const;

// Type union de toutes les clés disponibles (basé sur fr qui fait référence)
export type UIKey = keyof typeof fr;

// ── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Extrait la langue depuis l'URL.
 * Exemples :
 *   /              → 'fr' (défaut, prefixDefaultLocale: false)
 *   /en/docs/      → 'en'
 *   /de/wiki/      → 'de'
 */
export function getLangFromUrl(url: URL): Lang {
  const segments = url.pathname.split('/').filter(Boolean);
  const first = segments[0] as Lang;
  if (first && first in languages) return first;
  return defaultLang;
}

/**
 * Retourne la fonction de traduction pour une langue donnée.
 * Fallback automatique vers le français si une clé est manquante.
 *
 * Usage dans un composant Astro :
 *   const t = useTranslations(lang);
 *   t('hero_title_line1')
 */
export function useTranslations(lang: Lang) {
  return function t(key: UIKey): string {
    const langDict = ui[lang] as Record<string, string>;
    const fallback = ui[defaultLang] as Record<string, string>;
    return langDict[key] ?? fallback[key] ?? key;
  };
}

/**
 * Construit le chemin localisé vers une page.
 * La langue par défaut (fr) n'a pas de préfixe (prefixDefaultLocale: false).
 *
 * getLocalePath('/docs/')         → '/docs/'         (fr)
 * getLocalePath('/docs/', 'en')   → '/en/docs/'      (en)
 */
export function getLocalePath(path: string, lang: Lang = defaultLang): string {
  // Préfixe avec le BASE configuré (import.meta.env.BASE_URL) pour garantir
  // que les chemins fonctionnent en dev et en production (ex: /Coclico/)
  const BASE = (import.meta.env.BASE_URL || '/');
  const base = BASE.endsWith('/') ? BASE : BASE + '/';
  const p = path === '/' ? '' : path.startsWith('/') ? path.slice(1) : path;

  if (lang === defaultLang) return `${base}${p}`;
  return `${base}${lang}/${p}`;
}

/**
 * Retourne la liste des variantes de langue pour une page donnée.
 * Utile pour les balises <link rel="alternate" hreflang="...">
 */
export function getAlternateLinks(path: string): { lang: Lang; href: string }[] {
  return supportedLangs.map((lang) => ({
    lang,
    href: getLocalePath(path, lang),
  }));
}

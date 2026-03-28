import { defineConfig } from 'astro/config';
import tailwind from '@astrojs/tailwind';
import sitemap from '@astrojs/sitemap';

// https://astro.build/config
export default defineConfig({
  outDir: '../docs',
  // Astro vide le dossier outDir avant chaque build — les anciens fichiers sont remplacés
  trailingSlash: 'always',

  i18n: {
    defaultLocale: 'fr',
    locales: ['fr', 'en', 'de', 'es'],
    routing: {
      // false = Le français (défaut) est à la racine /
      // Les autres langues ont leur préfixe : /en/, /de/, /es/
      prefixDefaultLocale: false,
    },
    fallback: {
      en: 'fr',
      de: 'fr',
      es: 'fr',
    },
  },

  integrations: [
    sitemap(),
    tailwind({
      applyBaseStyles: false, // On gère nos propres styles de base dans global.css
    }),
  ],

  vite: {
    // GSAP est côté client uniquement — on s'assure qu'Astro ne tente pas de le SSR
    ssr: {
      noExternal: ['gsap'],
    },
  },

  // GitHub Pages : https://coclico-cy.github.io/Coclico/
  site: 'https://coclico-cy.github.io',
  base: '/Coclico',
});

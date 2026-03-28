/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{astro,html,js,jsx,md,mdx,svelte,ts,tsx,vue}'],
  darkMode: 'class', // Toggle via classe .dark sur <html>
  theme: {
    extend: {
      fontFamily: {
        display: ['Syne', 'system-ui', 'sans-serif'],
        sans: ['DM Sans', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'monospace'],
      },
      colors: {
        // Palette Coclico — reprend les variables CSS tokens
        brand: {
          blue: '#4a6cf7',
          purple: '#7c3aed',
          cyan: '#06b6d4',
        },
        surface: {
          base: '#03030a',
          raised: '#07071a',
          card: 'rgba(255,255,255,0.03)',
          'card-hover': 'rgba(255,255,255,0.055)',
        },
      },
      backgroundImage: {
        'gradient-radial': 'radial-gradient(var(--tw-gradient-stops))',
        'gradient-mesh':
          'radial-gradient(at 40% 20%, hsla(228,100%,74%,0.08) 0px, transparent 50%), radial-gradient(at 80% 0%, hsla(264,100%,74%,0.08) 0px, transparent 50%), radial-gradient(at 0% 50%, hsla(196,100%,74%,0.05) 0px, transparent 50%)',
      },
      animation: {
        'fade-in': 'fadeIn 0.6s ease forwards',
        'slide-up': 'slideUp 0.6s ease forwards',
        float: 'float 6s ease-in-out infinite',
      },
      keyframes: {
        fadeIn: {
          from: { opacity: '0' },
          to: { opacity: '1' },
        },
        slideUp: {
          from: { opacity: '0', transform: 'translateY(24px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
        float: {
          '0%, 100%': { transform: 'translateY(0px)' },
          '50%': { transform: 'translateY(-10px)' },
        },
      },
      typography: {
        DEFAULT: {
          css: {
            '--tw-prose-body': 'rgba(240,240,248,0.75)',
            '--tw-prose-headings': '#f0f0f8',
            '--tw-prose-links': '#4a6cf7',
            '--tw-prose-bold': '#f0f0f8',
            '--tw-prose-code': '#4a6cf7',
            '--tw-prose-pre-bg': '#080814',
          },
        },
      },
    },
  },
  plugins: [],
};

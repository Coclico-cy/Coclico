(() => {
  'use strict';

  const supportedLanguages = ['fr', 'en', 'de', 'es', 'it', 'ja', 'ko', 'pt', 'ru', 'zh'];
    fr: { name: 'Français', nativeName: 'Français', flag: '🇫🇷' },
    en: { name: 'English', nativeName: 'English', flag: '🇬🇧' },
    de: { name: 'German', nativeName: 'Deutsch', flag: '🇩🇪' },
    es: { name: 'Spanish', nativeName: 'Español', flag: '🇪🇸' },
    it: { name: 'Italian', nativeName: 'Italiano', flag: '🇮🇹' },
    ja: { name: 'Japanese', nativeName: '日本語', flag: '🇯🇵' },
    ko: { name: 'Korean', nativeName: '한국어', flag: '🇰🇷' },
    pt: { name: 'Portuguese', nativeName: 'Português', flag: '🇧🇷' },
    ru: { name: 'Russian', nativeName: 'Русский', flag: '🇷🇺' },
    zh: { name: 'Chinese', nativeName: '中文', flag: '🇨🇳' }
  };

  const pageMeta = {
    index: {
      title: {
        fr: 'Coclico — Gestionnaire Système Windows',
        en: 'Coclico — Windows System Manager',
        de: 'Coclico — Windows-Systemmanager',
        es: 'Coclico — Gestor del sistema Windows',
        it: 'Coclico — Gestore di sistema Windows',
        ja: 'Coclico — Windows システムマネージャー',
        ko: 'Coclico — Windows 시스템 관리자',
        pt: 'Coclico — Gestor de Sistema Windows',
        ru: 'Coclico — Менеджер системы Windows',
        zh: 'Coclico — Windows 系统管理器'
      },
      description: {
        fr: 'Coclico — Gestionnaire système Windows complet. 10 langues, IA locale, automatisation, nettoyage, RAM Cleaner et plus encore.',
        en: 'Coclico — Complete Windows system manager. 10 languages, local AI, automation, cleaning, RAM Cleaner and more.',
        de: 'Coclico — Vollständiger Windows-Systemmanager. 10 Sprachen, lokale KI, Automatisierung, Bereinigung, RAM Cleaner und mehr.',
        es: 'Coclico — Gestor completo del sistema Windows. 10 idiomas, IA local, automatización, limpieza, RAM Cleaner y más.',
        it: 'Coclico — Gestore completo del sistema Windows. 10 lingue, IA locale, automazione, pulizia, RAM Cleaner e altro.',
        ja: 'Coclico — Windows システムの完全管理ツール。10言語、ローカルAI、自動化、クリーンアップ、RAM Cleaner など。',
        ko: 'Coclico — Windows 시스템을 위한 완전한 관리 도구. 10개 언어, 로컬 AI, 자동화, 정리, RAM Cleaner 등.',
        pt: 'Coclico — Gerenciador completo do sistema Windows. 10 idiomas, IA local, automação, limpeza, RAM Cleaner e mais.',
        ru: 'Coclico — Полный менеджер системы Windows. 10 языков, локальный ИИ, автоматизация, очистка, RAM Cleaner и многое другое.',
        zh: 'Coclico — 完整的 Windows 系统管理工具。10 种语言、本地 AI、自动化、清理、RAM Cleaner 等。'
      }
    },
    tutorial: {
      title: {
        fr: 'Coclico — Guide & Tutoriel',
        en: 'Coclico — Guide & Tutorial',
        de: 'Coclico — Anleitung & Tutorial',
        es: 'Coclico — Guía y tutorial',
        it: 'Coclico — Guida e tutorial',
        ja: 'Coclico — ガイド & チュートリアル',
        ko: 'Coclico — 가이드 및 튜토리얼',
        pt: 'Coclico — Guia e tutorial',
        ru: 'Coclico — Руководство и учебник',
        zh: 'Coclico — 指南与教程'
      },
      description: {
        fr: 'Coclico — Guide & Tutoriel complet. Apprenez à maîtriser chaque module de Coclico : Dashboard, Flow Chains, RAM Cleaner, IA et plus.',
        en: 'Coclico — Complete guide & tutorial. Learn to master every Coclico module: Dashboard, Flow Chains, RAM Cleaner, AI and more.',
        de: 'Coclico — Vollständige Anleitung & Tutorial. Lernen Sie jedes Coclico-Modul kennen: Dashboard, Flow Chains, RAM Cleaner, KI und mehr.',
        es: 'Coclico — Guía y tutorial completos. Aprenda a dominar cada módulo de Coclico: Dashboard, Flow Chains, RAM Cleaner, IA y más.',
        it: 'Coclico — Guida e tutorial completi. Impara a padroneggiare ogni modulo di Coclico: Dashboard, Flow Chains, RAM Cleaner, IA e altro.',
        ja: 'Coclico — 完全ガイド & チュートリアル。Dashboard、Flow Chains、RAM Cleaner、AI など、Coclico の各モジュールを使いこなす方法を学べます。',
        ko: 'Coclico — 전체 가이드 및 튜토리얼. Dashboard, Flow Chains, RAM Cleaner, AI 등 Coclico의 각 모듈을 익히는 방법을 배워보세요.',
        pt: 'Coclico — Guia e tutorial completos. Aprenda a dominar cada módulo do Coclico: Dashboard, Flow Chains, RAM Cleaner, IA e mais.',
        ru: 'Coclico — Полное руководство и учебник. Изучите все модули Coclico: Dashboard, Flow Chains, RAM Cleaner, ИИ и многое другое.',
        zh: 'Coclico — 完整指南与教程。学习掌握 Coclico 的每个模块：Dashboard、Flow Chains、RAM Cleaner、AI 等。'
      }
    },
    wiki: {
      title: {
        fr: 'Coclico — Wiki Développeur',
        en: 'Coclico — Developer Wiki',
        de: 'Coclico — Entwickler-Wiki',
        es: 'Coclico — Wiki de desarrollador',
        it: 'Coclico — Wiki per sviluppatori',
        ja: 'Coclico — 開発者Wiki',
        ko: 'Coclico — 개발자 위키',
        pt: 'Coclico — Wiki do desenvolvedor',
        ru: 'Coclico — Wiki для разработчиков',
        zh: 'Coclico — 开发者 Wiki'
      },
      description: {
        fr: 'Coclico — Wiki Développeur. Architecture MVVM, DI Container, Services, Flow Chains, IA, Localisation — documentation technique complète.',
        en: 'Coclico — Developer Wiki. MVVM architecture, DI container, services, Flow Chains, AI, localization — complete technical documentation.',
        de: 'Coclico — Entwickler-Wiki. MVVM-Architektur, DI-Container, Services, Flow Chains, KI, Lokalisierung — vollständige technische Dokumentation.',
        es: 'Coclico — Wiki de desarrollador. Arquitectura MVVM, contenedor DI, servicios, Flow Chains, IA, localización — documentación técnica completa.',
        it: 'Coclico — Wiki per sviluppatori. Architettura MVVM, container DI, servizi, Flow Chains, IA, localizzazione — documentazione tecnica completa.',
        ja: 'Coclico — 開発者Wiki。MVVM アーキテクチャ、DI コンテナ、サービス、Flow Chains、AI、ローカライズなどの完全な技術ドキュメント。',
        ko: 'Coclico — 개발자 위키. MVVM 아키텍처, DI 컨테이너, 서비스, Flow Chains, AI, 지역화 등 완전한 기술 문서.',
        pt: 'Coclico — Wiki do desenvolvedor. Arquitetura MVVM, contêiner DI, serviços, Flow Chains, IA, localização — documentação técnica completa.',
        ru: 'Coclico — Wiki для разработчиков. Архитектура MVVM, контейнер DI, сервисы, Flow Chains, ИИ, локализация — полная техническая документация.',
        zh: 'Coclico — 开发者 Wiki。MVVM 架构、DI 容器、服务、Flow Chains、AI、本地化等完整技术文档。'
      }
    },
    faq: {
      title: {
        fr: 'Coclico — FAQ',
        en: 'Coclico — FAQ',
        de: 'Coclico — FAQ',
        es: 'Coclico — FAQ',
        it: 'Coclico — FAQ',
        ja: 'Coclico — FAQ',
        ko: 'Coclico — FAQ',
        pt: 'Coclico — FAQ',
        ru: 'Coclico — FAQ',
        zh: 'Coclico — FAQ'
      },
      description: {
        fr: 'Coclico FAQ — Réponses aux questions fréquentes sur l\'installation, les modules, l\'IA et les Flow Chains.',
        en: 'Coclico FAQ — Answers to common questions about installation, modules, AI and Flow Chains.',
        de: 'Coclico FAQ — Antworten auf häufige Fragen zu Installation, Modulen, KI und Flow Chains.',
        es: 'Coclico FAQ — Respuestas a preguntas frecuentes sobre instalación, módulos, IA y Flow Chains.',
        it: 'Coclico FAQ — Risposte alle domande frequenti su installazione, moduli, IA e Flow Chains.',
        ja: 'Coclico FAQ — インストール、モジュール、AI、Flow Chains に関するよくある質問への回答。',
        ko: 'Coclico FAQ — 설치, 모듈, AI, Flow Chains에 대한 자주 묻는 질문과 답변.',
        pt: 'Coclico FAQ — Respostas para perguntas frequentes sobre instalação, módulos, IA e Flow Chains.',
        ru: 'Coclico FAQ — Ответы на частые вопросы об установке, модулях, ИИ и Flow Chains.',
        zh: 'Coclico FAQ — 关于安装、模块、AI 和 Flow Chains 的常见问题解答。'
      }
    }
  };

  const cache = new Map();
  let baseLookupPromise = null;
  let currentLanguage = null;

  const skipTags = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE', 'NOSCRIPT', 'TEXTAREA', 'SELECT', 'OPTION']);
  const decorativeTags = new Set(['SVG', 'IMG', 'CANVAS', 'VIDEO', 'AUDIO', 'IFRAME', 'OBJECT', 'EMBED']);
  const inlineFriendlyTags = new Set(['BR', 'SPAN', 'STRONG', 'EM', 'B', 'I', 'SMALL', 'SUB', 'SUP', 'CODE', 'MARK', 'U', 'A']);

  function ensureStyles() {
    if (document.getElementById('coclico-i18n-styles')) return;

    const style = document.createElement('style');
    style.id = 'coclico-i18n-styles';
    style.textContent = `
      .coclico-lang-wrap {
        display: inline-flex;
        align-items: center;
        gap: 0.45rem;
      }

      .coclico-lang-label {
        font-size: 0.72rem;
        font-weight: 700;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: rgba(240, 240, 248, 0.62);
        user-select: none;
      }

      .coclico-lang-switch {
        appearance: none;
        -webkit-appearance: none;
        -moz-appearance: none;
        border-radius: 999px;
        border: 1px solid rgba(122, 92, 255, 0.45);
        background: linear-gradient(180deg, rgba(22, 16, 42, 0.96), rgba(12, 10, 26, 0.92));
        color: #f5f2ff;
        padding: 0.42rem 0.92rem;
        min-width: 10.5rem;
        font: inherit;
        font-size: 0.82rem;
        line-height: 1;
        box-shadow: 0 10px 24px rgba(0, 0, 0, 0.22);
        backdrop-filter: blur(14px);
        cursor: pointer;
        transition: border-color 160ms ease, transform 160ms ease, box-shadow 160ms ease, background 160ms ease;
      }

      .coclico-lang-switch:hover,
      .coclico-lang-switch:focus-visible {
        border-color: rgba(106, 216, 255, 0.72);
        box-shadow: 0 0 0 3px rgba(74, 108, 247, 0.12), 0 14px 30px rgba(0, 0, 0, 0.3);
        transform: translateY(-1px);
        outline: none;
      }

      .coclico-lang-switch option {
        background: #0f1020;
        color: #f4f0ff;
      }
    `;
    document.head.appendChild(style);
  }

  function normalizeText(value) {
    return (value || '').replace(/\s+/g, ' ').trim();
  }

  function decodeHtml(value) {
    const textArea = document.createElement('textarea');
    textArea.innerHTML = value || '';
    return textArea.value;
  }

  function stripMarkup(value) {
    return decodeHtml(String(value || '').replace(/<[^>]*>/g, ''));
  }

  function plainText(value) {
    return normalizeText(stripMarkup(value));
  }

  function isTranslatedWhole(el) {
    return el.hasAttribute('data-coclico-i18n-whole');
  }

  function hasTranslatedAncestor(node) {
    let current = node.parentElement;
    while (current) {
      if (isTranslatedWhole(current)) return true;
      current = current.parentElement;
    }
    return false;
  }

  function canTranslateWholeElement(el) {
    if (!el || skipTags.has(el.tagName)) return false;
    if (el.closest('[data-coclico-i18n-skip]')) return false;
    if (el.querySelector(':scope > svg, :scope > img, :scope > canvas, :scope > video, :scope > audio, :scope > iframe, :scope > object, :scope > embed')) {
      return false;
    }

    const childElements = Array.from(el.children);
    return childElements.every(child => inlineFriendlyTags.has(child.tagName));
  }

  function buildLookup(dictionary) {
    const lookup = new Map();

    for (const [key, value] of Object.entries(dictionary || {})) {
      if (typeof value !== 'string') continue;
      const normalized = plainText(value);
      if (normalized && !lookup.has(normalized)) {
        lookup.set(normalized, key);
      }
    }

    return lookup;
  }

  function loadDictionaryXHR(url) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('GET', url, true);
      xhr.responseType = 'json';
      xhr.onload = () => {
        // status 0 = file:// protocol (local file access)
        if (xhr.status === 0 || (xhr.status >= 200 && xhr.status < 300)) {
          resolve(xhr.response);
        } else {
          reject(new Error(`XHR failed: ${xhr.status}`));
        }
      };
      xhr.onerror = () => reject(new Error('XHR network error'));
      xhr.send();
    });
  }

  async function loadDictionary(lang) {
    const language = supportedLanguages.includes(lang) ? lang : 'fr';
    if (cache.has(language)) return cache.get(language);

    if (window.__coclico_i18n && window.__coclico_i18n[language]) {
      const fallback = window.__coclico_i18n[language];
      cache.set(language, fallback);
      return fallback;
    }

    const url = new URL(`${language}.json`, document.baseURI).href;

    let json;
    try {
      const response = await fetch(url, { cache: 'no-cache' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      json = await response.json();
    } catch {
      // Fallback to XHR — required for file:// protocol (CORS blocks fetch)
      json = await loadDictionaryXHR(url);
    }

    if (!json || typeof json !== 'object') {
      throw new Error(`Invalid JSON for language: ${language}`);
    }

    cache.set(language, json);
    return json;
  }

  async function loadBaseLookup() {
    if (!baseLookupPromise) {
      baseLookupPromise = loadDictionary('fr').then(dictionary => buildLookup(dictionary));
    }
    return baseLookupPromise;
  }

  function applyMeta(language) {
    const pageKey = document.documentElement.dataset.coclicoPage || document.body?.dataset?.coclicoPage || getPageFromLocation();
    const meta = pageMeta[pageKey];
    if (!meta) return;

    const title = meta.title?.[language] || meta.title?.fr || meta.title?.en;
    const description = meta.description?.[language] || meta.description?.fr || meta.description?.en;

    if (title) {
      document.title = title;
    }

    if (description) {
      const metaDescription = document.querySelector('meta[name="description"]');
      if (metaDescription) metaDescription.setAttribute('content', description);

      const ogDescription = document.querySelector('meta[property="og:description"]');
      if (ogDescription) ogDescription.setAttribute('content', description);

      const twitterDescription = document.querySelector('meta[name="twitter:description"]');
      if (twitterDescription) twitterDescription.setAttribute('content', description);
    }

    if (title) {
      const ogTitle = document.querySelector('meta[property="og:title"]');
      if (ogTitle) ogTitle.setAttribute('content', title);

      const twitterTitle = document.querySelector('meta[name="twitter:title"]');
      if (twitterTitle) twitterTitle.setAttribute('content', title);
    }
  }

  function getPageFromLocation() {
    const path = (window.location.pathname || '').toLowerCase();
    if (path.endsWith('/index.html') || path.endsWith('/')) return 'index';
    if (path.endsWith('/tutorial.html')) return 'tutorial';
    if (path.endsWith('/wiki.html')) return 'wiki';
    if (path.endsWith('/faq.html')) return 'faq';
    return 'index';
  }

  function applyToExplicitBindings(root, dictionary) {
    root.querySelectorAll('[data-i18n]').forEach(el => {
      const key = el.getAttribute('data-i18n');
      if (!key || typeof dictionary[key] !== 'string') return;
      if (el.getAttribute('data-i18n-html') === 'true') {
        el.innerHTML = dictionary[key];
      } else {
        el.textContent = plainText(dictionary[key]);
      }
    });

    root.querySelectorAll('[data-i18n-attr]').forEach(el => {
      const key = el.getAttribute('data-i18n');
      if (!key || typeof dictionary[key] !== 'string') return;
      const attrs = el.getAttribute('data-i18n-attr').split(',').map(item => item.trim()).filter(Boolean);
      const value = plainText(dictionary[key]);
      attrs.forEach(attr => el.setAttribute(attr, value));
    });
  }

  function translateWholeElements(root, dictionary, lookup) {
    root.querySelectorAll('body *').forEach(el => {
      if (!el || isTranslatedWhole(el)) return;
      if (!canTranslateWholeElement(el)) return;
      if (el.closest('[data-coclico-i18n-skip]')) return;
      if (skipTags.has(el.tagName)) return;
      if (el.querySelector('svg, img, canvas, video, audio, iframe, object, embed')) return;

      const normalized = normalizeText(el.textContent || '');
      if (!normalized) return;

      const key = lookup.get(normalized);
      if (!key || typeof dictionary[key] !== 'string') return;

      el.innerHTML = dictionary[key];
      el.setAttribute('data-coclico-i18n-whole', 'true');
    });
  }

  function translateAttributes(root, dictionary, lookup) {
    root.querySelectorAll('input, textarea, button, a, select, option, label, summary').forEach(el => {
      ['placeholder', 'aria-label', 'title'].forEach(attribute => {
        if (!el.hasAttribute(attribute)) return;
        const value = el.getAttribute(attribute) || '';
        const normalized = normalizeText(value);
        const key = lookup.get(normalized);
        if (!key || typeof dictionary[key] !== 'string') return;
        el.setAttribute(attribute, plainText(dictionary[key]));
      });
    });
  }

  function translateTextNodes(root, dictionary, lookup) {
    const walker = document.createTreeWalker(root.body || root, NodeFilter.SHOW_TEXT);
    const nodes = [];

    while (walker.nextNode()) {
      nodes.push(walker.currentNode);
    }

    for (const node of nodes) {
      if (!node || !node.parentElement) continue;
      if (hasTranslatedAncestor(node)) continue;
      const parent = node.parentElement;
      if (skipTags.has(parent.tagName) || parent.closest('[data-coclico-i18n-skip]')) continue;

      const raw = node.nodeValue || '';
      const normalized = normalizeText(raw);
      if (!normalized) continue;

      const key = lookup.get(normalized);
      if (!key || typeof dictionary[key] !== 'string') continue;

      const leading = raw.match(/^\s*/)?.[0] || '';
      const trailing = raw.match(/\s*$/)?.[0] || '';
      node.nodeValue = `${leading}${plainText(dictionary[key])}${trailing}`;
    }
  }

  function bindLanguageSelectors(language) {
    document.querySelectorAll('#langSwitch, [data-coclico-lang-switch]').forEach(select => {
      if (!(select instanceof HTMLSelectElement)) return;
      if (select.value !== language) select.value = language;

      if (select.dataset.coclicoBound === 'true') return;
      select.dataset.coclicoBound = 'true';
      select.setAttribute('aria-label', plainText((languageMeta[language] || languageMeta.en).name));

      select.addEventListener('change', async event => {
        const target = (event.target && event.target.value ? event.target.value : language).toLowerCase();
        await setLanguage(target);
      });
    });
  }

  async function applyLanguage(language) {
    const selected = supportedLanguages.includes(language) ? language : 'fr';
    const [dictionary, lookup] = await Promise.all([
      loadDictionary(selected),
      loadBaseLookup()
    ]);

    currentLanguage = selected;
    document.documentElement.lang = selected;
    document.documentElement.dataset.coclicoLang = selected;

    ensureStyles();
    applyMeta(selected);
    applyToExplicitBindings(document, dictionary);
    translateWholeElements(document, dictionary, lookup);
    translateAttributes(document, dictionary, lookup);
    translateTextNodes(document, dictionary, lookup);
    bindLanguageSelectors(selected);

    try {
      localStorage.setItem('coclico_lang', selected);
    } catch {
      // ignore storage failures
    }
  }

  async function setLanguage(language) {
    await applyLanguage(language);
  }

  function getInitialLanguage() {
    try {
      const stored = localStorage.getItem('coclico_lang');
      if (stored && supportedLanguages.includes(stored)) return stored;
    } catch {
      // ignore storage access issues
    }

    const browser = (navigator.language || navigator.userLanguage || 'en').slice(0, 2).toLowerCase();
    if (supportedLanguages.includes(browser)) return browser;
    return 'fr';
  }

  async function init() {
    const initial = getInitialLanguage();
    await applyLanguage(initial);
  }

  window.CoclicoI18n = {
    supportedLanguages,
    languageMeta,
    init,
    setLanguage,
    getLanguage: () => currentLanguage || getInitialLanguage()
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      init().catch(error => console.error('[CoclicoI18n]', error));
    }, { once: true });
  } else {
    init().catch(error => console.error('[CoclicoI18n]', error));
  }
})();


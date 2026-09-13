// i18n.js — Internationalisation FR / EN / AR + RTL (Contrainte C5)
// Architecture : fichiers JSON de traduction + attributs data-i18n dans le HTML.
// RTL : changer dir="rtl" sur <html> suffit, le CSS s'adapte automatiquement.

"use strict";

const I18N_KEY = 'app-lang';
const SUPPORTED = ['fr', 'en', 'ar'];
const RTL_LANGS = new Set(['ar']);

let currentLang = localStorage.getItem(I18N_KEY) || 'fr';
let translations = {};

/** Charge le fichier JSON de traduction pour la langue donnée. */
async function loadLocale(lang) {
    const res = await fetch(`/locales/${lang}.json`);
    if (!res.ok) throw new Error(`Locale ${lang} introuvable`);
    return res.json();
}

/** Traduit une clé. Retourne la clé brute si non trouvée (fallback visible). */
function t(key) {
    return translations[key] || key;
}

/** Applique les traductions à tous les éléments [data-i18n] du DOM. */
function applyTranslations() {
    document.querySelectorAll('[data-i18n]').forEach((el) => {
        const key = el.dataset.i18n;
        const attr = el.dataset.i18nAttr; // ex: data-i18n-attr="placeholder"
        if (attr) {
            el.setAttribute(attr, t(key));
        } else {
            el.textContent = t(key);
        }
    });
    document.title = t('app.title');
}

/** Change la langue, applique RTL si nécessaire, re-rend les traductions. */
async function setLanguage(lang) {
    if (!SUPPORTED.includes(lang)) return;
    try {
        translations = await loadLocale(lang);
    } catch (e) {
        console.warn('[i18n] Impossible de charger la locale :', lang, e);
        return;
    }
    currentLang = lang;
    localStorage.setItem(I18N_KEY, lang);

    // Direction du texte — RTL pour l'arabe, LTR pour les autres
    const dir = RTL_LANGS.has(lang) ? 'rtl' : 'ltr';
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;

    applyTranslations();
}

/** Crée et insère le sélecteur de langue dans le header. */
function createLanguageSelector() {
    const selector = document.createElement('div');
    selector.className = 'lang-selector';
    selector.setAttribute('role', 'navigation');
    selector.setAttribute('aria-label', 'Sélection de la langue');

    const labels = { fr: '🇫🇷 FR', en: '🇬🇧 EN', ar: 'AR 🇸🇦' };

    SUPPORTED.forEach((lang) => {
        const btn = document.createElement('button');
        btn.className = 'lang-btn';
        btn.dataset.lang = lang;
        btn.textContent = labels[lang];
        btn.setAttribute('aria-label', `Changer la langue en ${lang.toUpperCase()}`);
        if (lang === currentLang) btn.classList.add('active');

        btn.onclick = () => {
            setLanguage(lang);
            selector.querySelectorAll('.lang-btn').forEach((b) =>
                b.classList.toggle('active', b.dataset.lang === lang)
            );
        };
        selector.appendChild(btn);
    });

    // Insère avant le div#session dans le header
    const header = document.querySelector('header');
    const session = document.getElementById('session');
    if (header && session) header.insertBefore(selector, session);
    else if (header) header.appendChild(selector);
}

// Initialisation au chargement du DOM
window.addEventListener('DOMContentLoaded', async () => {
    createLanguageSelector();
    await setLanguage(currentLang);
});

// Export pour usage éventuel depuis app.js
window.i18n = { t, setLanguage };

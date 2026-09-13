// sw.js — Service Worker (Contrainte C4 : offline + faible bande passante)
//
// Stratégies :
//   GET /api/availability  → Network-first avec timeout 5 s, fallback cache
//   Actifs statiques       → Cache-first (HTML, CSS, JS)
//   Autres API (POST...)   → Network-only (mutations jamais mises en cache)

const CACHE_NAME = 'hotel-booking-v1';
const STATIC_ASSETS = ['/', '/index.html', '/styles.css', '/app.js', '/i18n.js'];
const NETWORK_TIMEOUT_MS = 5000; // 5 s — adapté 2G/3G

// --- Installation ---
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(STATIC_ASSETS))
    );
    self.skipWaiting();
});

// --- Activation : purge des anciens caches ---
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// --- Interception des requêtes ---
self.addEventListener('fetch', (event) => {
    const { request } = event;
    const url = new URL(request.url);

    // Disponibilités : Network-first avec timeout → fallback cache → réponse offline
    if (url.pathname.startsWith('/api/availability') && request.method === 'GET') {
        event.respondWith(networkFirstWithTimeout(request));
        return;
    }

    // Actifs statiques : Cache-first
    if (
        request.method === 'GET' &&
        (url.pathname === '/' ||
            url.pathname.endsWith('.html') ||
            url.pathname.endsWith('.css') ||
            url.pathname.endsWith('.js') ||
            url.pathname.startsWith('/locales/'))
    ) {
        event.respondWith(cacheFirst(request));
        return;
    }

    // Toutes les autres requêtes (POST, DELETE, autres GET API) : Network-only
    // Les mutations ne sont jamais cachées — cohérence garantie
});

/**
 * Network-first avec timeout.
 * Réseau répond dans le délai → cache + retourne.
 * Timeout ou hors ligne → cache si dispo, sinon réponse offline structurée.
 */
async function networkFirstWithTimeout(request) {
    const cache = await caches.open(CACHE_NAME);

    const networkPromise = fetch(request.clone()).then((response) => {
        if (response.ok) cache.put(request, response.clone());
        return response;
    });

    const timeoutPromise = new Promise((_, reject) =>
        setTimeout(() => reject(new Error('timeout')), NETWORK_TIMEOUT_MS)
    );

    try {
        return await Promise.race([networkPromise, timeoutPromise]);
    } catch {
        const cached = await cache.match(request);
        if (cached) return cached;
        return offlineFallbackJson({
            offline: true,
            message: 'Données indisponibles hors connexion. Reconnectez-vous pour voir les disponibilités.',
        });
    }
}

/** Cache-first : retourne le cache immédiatement s'il existe, sinon réseau + mise en cache. */
async function cacheFirst(request) {
    const cached = await caches.match(request);
    if (cached) return cached;
    const response = await fetch(request);
    if (response.ok) {
        const cache = await caches.open(CACHE_NAME);
        cache.put(request, response.clone());
    }
    return response;
}

function offlineFallbackJson(data) {
    return new Response(JSON.stringify(data), {
        status: 503,
        headers: { 'Content-Type': 'application/json', 'X-Offline': 'true' },
    });
}

// Enregistrement du Service Worker (offline + faible bande — Contrainte C4)
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/sw.js')
            .then(() => console.log('[SW] Service Worker enregistré'))
            .catch((err) => console.warn('[SW] Enregistrement échoué :', err));
    });
}

// Enregistrement du Service Worker (offline + faible bande — Contrainte C4)
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/sw.js')
            .then(() => console.log('[SW] Service Worker enregistré'))
            .catch((err) => console.warn('[SW] Enregistrement échoué :', err));
    });
}

// Front minimal (vanilla JS) — même origine que l'API (proxy Nginx), donc pas de CORS.
"use strict";

const state = {
    token: localStorage.getItem("token") || null,
    email: localStorage.getItem("email") || null,
    role: localStorage.getItem("role") || null,
    selected: new Set(),
    bookings: JSON.parse(localStorage.getItem("bookings") || "[]"),
};

// --- Helpers DOM ---
const $ = (id) => document.getElementById(id);
const show = (el) => el.classList.remove("hidden");
const hide = (el) => el.classList.add("hidden");

function message(text, kind = "error") {
    const box = $("message");
    box.textContent = text;
    box.className = `message ${kind}`;
    show(box);
    if (kind === "success") setTimeout(() => hide(box), 3000);
}
function clearMessage() { hide($("message")); }

// --- Appels API ---
async function api(path, { method = "GET", body = null, auth = false } = {}) {
    const headers = {};
    if (body) headers["Content-Type"] = "application/json";
    if (auth && state.token) headers["Authorization"] = `Bearer ${state.token}`;

    const res = await fetch(path, { method, headers, body: body ? JSON.stringify(body) : null });

    // Affiche le nœud ayant traité la requête (démonstration de la distribution).
    if (path === "/health") { try { return await res.json(); } catch { return null; } }

    let data = null;
    try { data = await res.json(); } catch { /* réponse sans corps */ }

    if (!res.ok) {
        const msg = data && data.error ? data.error.message : `Erreur HTTP ${res.status}`;
        throw new Error(msg);
    }
    return data;
}

async function refreshNode() {
    try {
        const h = await api("/health");
        if (h && h.node) $("node").textContent = `nœud ${h.node.substring(0, 8)}`;
    } catch { /* ignore */ }
}

// --- Session ---
function persistSession() {
    if (state.token) {
        localStorage.setItem("token", state.token);
        localStorage.setItem("email", state.email);
        localStorage.setItem("role", state.role);
    } else {
        localStorage.removeItem("token");
        localStorage.removeItem("email");
        localStorage.removeItem("role");
    }
}
function persistBookings() {
    localStorage.setItem("bookings", JSON.stringify(state.bookings));
}

function renderSession() {
    if (state.token) {
        hide($("auth"));
        show($("app"));
        show($("session"));
        $("who").textContent = state.email;
        renderBookings();
        refreshNode();
        loadAnalytics(); // tableau de bord admin si rôle ADMIN (C6)
    } else {
        show($("auth"));
        hide($("app"));
        hide($("session"));
    }
}

async function authenticate(path) {
    clearMessage();
    const email = $("email").value.trim();
    const password = $("password").value;
    try {
        const res = await api(path, { method: "POST", body: { email, password } });
        state.token = res.token;
        state.email = res.email;
        state.role = res.role;
        persistSession();
        renderSession();
        message("Connecté à la plateforme de réservation d'hôtel.", "success");
    } catch (e) {
        message(e.message);
    }
}

function logout() {
    state.token = state.email = state.role = null;
    state.selected.clear();
    persistSession();
    renderSession();
}

// --- Disponibilités ---
async function search() {
    clearMessage();
    state.selected.clear();
    const type = $("type").value;
    const from = $("from").value;
    const to = $("to").value;
    try {
        const res = await fetch(`/api/availability?type=${encodeURIComponent(type)}&from=${from}&to=${to}`);
        const data = await res.json();
        // Détecte si la réponse vient du cache Service Worker (offline)
        if (data && data.offline) {
            message('📶 Mode hors-ligne : affichage des dernières disponibilités en cache.', 'warn');
            return;
        }
        if (!res.ok) throw new Error(data?.error?.message || `Erreur HTTP ${res.status}`);
        renderResults(data);
        refreshNode();
    } catch (e) {
        message(e.message);
    }
}

function renderResults(items) {
    const box = $("results");
    box.textContent = "";
    if (!items || items.length === 0) {
        const p = document.createElement("p");
        p.className = "muted";
        p.textContent = "Aucune chambre disponible pour ces dates.";
        box.appendChild(p);
        hide($("btn-book"));
        return;
    }
    items.slice(0, 60).forEach((it) => {
        const el = document.createElement("div");
        el.className = "room-card";

        // textContent : les données du serveur ne sont jamais interprétées
        // comme du HTML (protection XSS).
        const title = document.createElement("div");
        title.className = "room-title";
        title.textContent = `Chambre ${it.resourceId}`;
        el.appendChild(title);

        const sid = document.createElement("div");
        sid.className = "sid";
        sid.textContent = `slot ${it.slotId}`;
        el.appendChild(sid);

        const dates = document.createElement("div");
        dates.textContent = `${it.periodStart} → ${it.periodEnd}`;
        el.appendChild(dates);

        el.onclick = () => {
            if (state.selected.has(it.slotId)) { state.selected.delete(it.slotId); el.classList.remove("selected"); }
            else { state.selected.add(it.slotId); el.classList.add("selected"); }
            $("btn-book").classList.toggle("hidden", state.selected.size === 0);
        };
        box.appendChild(el);
    });
}

// --- Réservations ---
async function book() {
    clearMessage();
    const slotIds = Array.from(state.selected);
    if (slotIds.length === 0) return;
    try {
        const res = await api("/api/bookings", { method: "POST", body: { slotIds }, auth: true });
        state.bookings.unshift({
            bookingId: res.bookingId, status: res.status,
            expiresAt: res.expiresAt, slotIds: res.slotIds,
        });
        persistBookings();
        renderBookings();
        message(`Réservation ${res.bookingId} créée (en attente de confirmation).`, "success");
        search(); // rafraîchit la disponibilité
    } catch (e) {
        message(e.message);
    }
}

async function confirmBooking(id) {
    clearMessage();
    try {
        const res = await api(`/api/bookings/${id}/confirm`, { method: "POST", auth: true });
        updateBooking(id, { status: res.status, expiresAt: null });
        message(`Réservation ${id} confirmée. Chambre(s) réservée(s).`, "success");
    } catch (e) {
        // Le hold a pu expirer entre-temps : refléter le vrai statut côté UI.
        if (/expir/i.test(e.message)) { updateBooking(id, { status: "EXPIRED", expiresAt: null }); search(); }
        message(e.message);
    }
}

async function cancelBooking(id) {
    clearMessage();
    try {
        await api(`/api/bookings/${id}`, { method: "DELETE", auth: true });
        updateBooking(id, { status: "CANCELLED", expiresAt: null });
        message(`Réservation ${id} annulée. Chambre(s) libérée(s).`, "success");
        search();
    } catch (e) { message(e.message); }
}

function updateBooking(id, patch) {
    const b = state.bookings.find((x) => x.bookingId === id);
    if (b) Object.assign(b, patch);
    persistBookings();
    renderBookings();
}

function renderBookings() {
    const box = $("bookings");
    if (state.bookings.length === 0) {
        box.textContent = "";
        const p = document.createElement("p");
        p.className = "muted";
        p.textContent = "Aucune réservation pour l'instant.";
        box.appendChild(p);
        return;
    }
    box.textContent = "";
    state.bookings.forEach((b) => {
        const el = document.createElement("div");
        el.className = `booking ${b.status}`;

        const meta = document.createElement("div");
        meta.className = "meta";

        const strong = document.createElement("strong");
        strong.textContent = `#${b.bookingId}`;
        meta.appendChild(strong);

        const badge = document.createElement("span");
        badge.className = `badge ${b.status}`;
        badge.textContent = b.status;
        meta.appendChild(badge);

        const info = document.createElement("span");
        info.className = "muted";
        info.textContent = ` chambres ${b.slotIds.join(", ")}`;
        meta.appendChild(info);

        if (b.status === "HOLD" && b.expiresAt) {
            const sep = document.createTextNode(" · ");
            meta.appendChild(sep);
            const cd = document.createElement("span");
            cd.className = "countdown";
            cd.dataset.exp = b.expiresAt;
            cd.dataset.id = String(b.bookingId);
            meta.appendChild(cd);
        }
        el.appendChild(meta);

        const actions = document.createElement("div");
        actions.className = "row";
        if (b.status === "HOLD") {
            const c = document.createElement("button");
            c.textContent = "Confirmer"; c.className = "ok";
            c.onclick = () => confirmBooking(b.bookingId);
            actions.appendChild(c);
        }
        if (b.status === "HOLD" || b.status === "CONFIRMED") {
            const x = document.createElement("button");
            x.textContent = "Annuler"; x.className = "danger";
            x.onclick = () => cancelBooking(b.bookingId);
            actions.appendChild(x);
        }
        el.appendChild(actions);
        box.appendChild(el);
    });
}

// Met à jour les comptes à rebours des holds ; bascule en EXPIRED à échéance.
function tickCountdowns() {
    let changed = false;
    document.querySelectorAll(".countdown").forEach((el) => {
        const exp = new Date(el.dataset.exp).getTime();
        const rem = Math.floor((exp - Date.now()) / 1000);
        const id = Number(el.dataset.id);
        if (rem <= 0) {
            const b = state.bookings.find((x) => x.bookingId === id);
            if (b && b.status === "HOLD") { b.status = "EXPIRED"; changed = true; }
        } else {
            const m = String(Math.floor(rem / 60)).padStart(2, "0");
            const s = String(rem % 60).padStart(2, "0");
            el.textContent = `expire dans ${m}:${s}`;
        }
    });
    if (changed) { persistBookings(); renderBookings(); }
}
setInterval(tickCountdowns, 1000);

// --- Câblage ---
$("btn-login").onclick = () => authenticate("/api/auth/login");
$("btn-register").onclick = () => authenticate("/api/auth/register");
$("logout").onclick = logout;
$("btn-search").onclick = search;
$("btn-book").onclick = book;

// Pré-remplit des dates par défaut dans le futur (arrivée = demain, départ = +2 j)
// pour rester cohérent avec le refus des dates passées côté API (FIX 1).
(function setDefaultDates() {
    const fmt = (d) => d.toISOString().slice(0, 10);
    const arrival = new Date(); arrival.setDate(arrival.getDate() + 1);
    const departure = new Date(); departure.setDate(departure.getDate() + 2);
    if (!$("from").value) $("from").value = fmt(arrival);
    if (!$("to").value) $("to").value = fmt(departure);
})();

renderSession();

// --- Analytics (Admin) — Contrainte C6 ---
async function loadAnalytics() {
    if (state.role !== 'ADMIN') return;
    try {
        const res = await fetch('/api/admin/analytics', {
            headers: { 'Authorization': `Bearer ${state.token}` }
        });
        if (!res.ok) return;
        const data = await res.json();
        renderAnalytics(data);
    } catch { /* non critique */ }
}

function renderAnalytics(data) {
    let panel = $('analytics-panel');
    if (!panel) {
        panel = document.createElement('section');
        panel.id = 'analytics-panel';
        panel.className = 'card';
        panel.setAttribute('aria-labelledby', 'analytics-title');
        $('app').appendChild(panel);
    }

    const s = data.summary;
    const rooms = data.topRooms || [];
    const trend = data.weeklyTrend || [];

    panel.innerHTML = `
        <h2 id="analytics-title">📊 Tableau de bord — Données anonymisées</h2>
        <div class="analytics-grid">
            <div class="stat-card">
                <div class="stat-value">${s.total}</div>
                <div class="stat-label">Réservations totales</div>
            </div>
            <div class="stat-card">
                <div class="stat-value ok">${s.confirmed}</div>
                <div class="stat-label">Confirmées</div>
            </div>
            <div class="stat-card">
                <div class="stat-value warn">${s.cancelled}</div>
                <div class="stat-label">Annulées</div>
            </div>
            <div class="stat-card">
                <div class="stat-value muted">${s.cancellationRatePct}%</div>
                <div class="stat-label">Taux d'annulation</div>
            </div>
        </div>

        <h3>Top chambres</h3>
        <table class="analytics-table" role="table" aria-label="Réservations par chambre">
            <thead>
                <tr><th>Chambre</th><th>Total</th><th>Confirmées</th><th>Annulées</th><th>Taux annul.</th></tr>
            </thead>
            <tbody>
                ${rooms.map(r => `
                    <tr>
                        <td>Chambre ${r.resourceId}</td>
                        <td>${r.totalBookings}</td>
                        <td class="ok">${r.confirmedBookings}</td>
                        <td class="warn">${r.cancelledBookings}</td>
                        <td>${r.cancellationRatePct}%</td>
                    </tr>`).join('')}
            </tbody>
        </table>

        <h3>Tendance hebdomadaire</h3>
        <table class="analytics-table" role="table" aria-label="Tendance hebdomadaire des réservations">
            <thead>
                <tr><th>Semaine</th><th>Total</th><th>Confirmées</th><th>Annulées</th></tr>
            </thead>
            <tbody>
                ${trend.map(w => `
                    <tr>
                        <td>${w.weekStart}</td>
                        <td>${w.totalBookings}</td>
                        <td class="ok">${w.confirmed}</td>
                        <td class="warn">${w.cancelled}</td>
                    </tr>`).join('')}
            </tbody>
        </table>
        <p class="muted" style="font-size:.8rem;margin-top:.5rem">⚠️ Données agrégées — aucune information personnelle exposée (RGPD).</p>
    `;
}
